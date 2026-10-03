using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Visits;

namespace DocPrep.Application.Interviews;

public sealed class ElevenLabsInterviewService(IDocPrepStore store, ICredentialService credentials, IElevenLabsClient elevenLabs,
    InterviewExtractionService extraction, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<AgentInterviewView> GetByVisit(Guid visitId, CancellationToken ct)
    {
        var interview = await store.GetAgentInterviewByVisit(visitId, ct) ?? throw new NotFoundError();
        return await View(interview, ct);
    }

    public async Task<AgentInterviewView> Get(Guid interviewId, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await AuthorizedInterview(interviewId, access, ct);
        return await View(interview, ct);
    }

    public async Task<AgentInterviewResultView> Result(Guid interviewId, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await AuthorizedInterview(interviewId, access, ct);
        NormalizedInterviewData? structured = null;
        if (!string.IsNullOrWhiteSpace(interview.StructuredDataJson))
            try { structured = JsonSerializer.Deserialize<NormalizedInterviewData>(interview.StructuredDataJson, JsonOptions); } catch (JsonException) { }
        IReadOnlyList<ExtractionIssue> issues = [];
        try { issues = JsonSerializer.Deserialize<List<ExtractionIssue>>(interview.ExtractionIssuesJson, JsonOptions) ?? []; } catch (JsonException) { }
        return new(interview.Id, Status(interview.Status), interview.FinalReport, interview.StructuredDataJson,
            structured, interview.StructuredDataSchemaVersion, interview.ExtractionStatus.ToString().ToLowerInvariant(),
            interview.ImportStatus.ToString().ToLowerInvariant(), issues);
    }

    public async Task<InterviewInvitationAccess> AuthorizeInvitation(string rawToken, CancellationToken ct)
    {
        var invitation = await ReviewInvitation(rawToken, ct);
        invitation.Open(clock.UtcNow);
        await store.Save(ct);
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        return new(invitation.InterviewId, invitation.Id, interview.VisitProcessId, invitation.ExpiresAt);
    }

    public async Task<AgentInterviewView> PublicView(string rawToken, CancellationToken ct)
    {
        var invitation = await ReviewInvitation(rawToken, ct);
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        return await View(interview, ct);
    }

    public async Task<AgentSessionResult> CreateSession(Guid interviewId, InterviewSessionMode mode, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await AuthorizedInterview(interviewId, access, ct);
        interview.EnsureCanStart();
        var visit = await ActiveVisit(interview.VisitProcessId, ct);
        var previousSessions = await store.GetAgentInterviewSessions(interviewId, ct);
        var previousInterview = (await store.GetAgentInterviewsByVisit(visit.Id, ct))
            .Where(x => x.Generation < interview.Generation).OrderByDescending(x => x.Generation).FirstOrDefault();
        var round = await store.GetOpenRound(visit.Id, ct);
        var continuation = InterviewContinuationContext.Build(previousSessions, previousInterview?.FinalReport,
            round?.Questions.Where(x => x.Answer is null).Select(x => x.Text));
        InterviewInvitation? invitation = null;
        if (access.InvitationId is not null)
        {
            invitation = await store.GetInterviewInvitation(access.InvitationId.Value, ct) ?? throw new ForbiddenError();
            if (invitation.InterviewId != interviewId || !invitation.IsUsable(clock.UtcNow)) throw new ForbiddenError();
        }

        var session = new AgentInterviewSession(interview.Id, access.UserId, mode, clock.UtcNow);
        store.Add(session);
        await store.Save(ct);
        try
        {
            var participant = access.UserId is not null ? $"usr_{access.UserId:N}" : $"anon_{session.Id:N}";
            var credential = mode == InterviewSessionMode.Voice
                ? await elevenLabs.CreateVoiceCredential(participant, ct)
                : await elevenLabs.CreateTextCredential(participant, ct);
            session.CredentialIssued(credential.ConversationId, clock.UtcNow);
            invitation?.UseSession(clock.UtcNow);
            interview.Start(clock.UtcNow);
            store.Add(new AuditEvent(visit.FacilityId, visit.Id, participant, $"agent_session.started:{session.Id}:{mode}", clock.UtcNow));
            await store.Save(ct);
            return new(session.Id, mode.ToString().ToLowerInvariant(), session.Provider, credential.ConversationToken,
                credential.SignedUrl, credential.ConversationId, participant, new Dictionary<string, object>
                {
                    ["language"] = "pl", ["visit_type"] = "wywiad przed wizytą", ["interview_type"] = interview.InterviewType,
                    ["is_continuation"] = !string.IsNullOrWhiteSpace(continuation),
                    ["previous_conversation_summary"] = continuation
                });
        }
        catch
        {
            session.Fail();
            await store.Save(ct);
            throw;
        }
    }

    public async Task<AgentSessionResult> CreatePublicSession(string rawToken, InterviewSessionMode mode, CancellationToken ct)
    {
        var invitation = await ExecutableInvitation(rawToken, ct);
        return await CreateSession(invitation.InterviewId, mode, AgentInterviewAccess.ForInvitation(invitation.InterviewId, invitation.Id), ct);
    }

    public async Task SetProviderConversation(Guid sessionId, string conversationId, AgentInterviewAccess access, CancellationToken ct)
    {
        var session = await store.GetAgentInterviewSession(sessionId, ct) ?? throw new NotFoundError();
        await AuthorizedInterview(session.InterviewId, access, ct);
        session.SetConversation(conversationId, clock.UtcNow);
        await store.Save(ct);
    }

    public async Task EndSession(Guid sessionId, bool continuesInterview, AgentInterviewAccess access, CancellationToken ct)
    {
        var session = await store.GetAgentInterviewSession(sessionId, ct) ?? throw new NotFoundError();
        var interview = await AuthorizedInterview(session.InterviewId, access, ct);
        session.End(continuesInterview);
        if (!continuesInterview) interview.Processing();
        await store.Save(ct);
    }

    public async Task<AgentInterviewResultView> RetryImport(Guid interviewId, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await AuthorizedInterview(interviewId, access, ct);
        if (string.IsNullOrWhiteSpace(interview.StructuredDataJson))
            throw new ConflictError("extraction.not_available", "No normalized extraction is available.");
        var data = JsonSerializer.Deserialize<NormalizedInterviewData>(interview.StructuredDataJson, JsonOptions)
            ?? throw new ConflictError("extraction.invalid", "The normalized extraction is invalid.");
        if (!await extraction.Import(interview.Id, interview.ImportedFromSessionId ?? Guid.Empty, data, ct))
            throw new ConflictError("import.manual_changes", "The draft contains newer patient changes and cannot be overwritten.");
        await store.Save(ct);
        return await Result(interviewId, access, ct);
    }

    public async Task Recover(Guid sessionId, AgentInterviewAccess access, string expectedAgentId, CancellationToken ct)
    {
        var session = await store.GetAgentInterviewSession(sessionId, ct) ?? throw new NotFoundError();
        await AuthorizedInterview(session.InterviewId, access, ct);
        if (string.IsNullOrWhiteSpace(session.ProviderConversationId))
            throw new ConflictError("agent_session.not_bound", "The session has no provider conversation identifier.");
        var data = await elevenLabs.GetConversation(session.ProviderConversationId, ct);
        var timestamp = clock.UtcNow.ToUnixTimeSeconds();
        var raw = Encoding.UTF8.GetBytes($"{{\"type\":\"post_call_transcription\",\"event_timestamp\":{timestamp},\"data\":{data.GetRawText()}}}");
        using var document = JsonDocument.Parse(raw);
        await ProcessWebhook(document.RootElement, PayloadHash(raw), expectedAgentId, ct);
    }

    public async Task ProcessWebhook(JsonElement root, string payloadHash, string expectedAgentId, CancellationToken ct)
    {
        if (!root.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "post_call_transcription") return;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("conversation_id", out var conversationElement))
            throw new ArgumentException("Webhook conversation_id is required.");
        var conversationId = conversationElement.GetString() ?? throw new ArgumentException("Webhook conversation_id is required.");
        var agentId = data.TryGetProperty("agent_id", out var agentElement) ? agentElement.GetString() : null;
        if (!string.Equals(agentId, expectedAgentId, StringComparison.Ordinal)) throw new ForbiddenError("Unexpected ElevenLabs agent.");
        var timestamp = root.TryGetProperty("event_timestamp", out var timestampElement) ? timestampElement.ToString() : "unknown";
        var externalEventId = $"post_call_transcription:{conversationId}:{timestamp}";
        var webhookEvent = await store.GetWebhookEvent("elevenlabs", externalEventId, ct);
        if (webhookEvent?.ProcessedAt is not null) return;
        if (webhookEvent is null)
        {
            webhookEvent = new ExternalWebhookEvent("elevenlabs", externalEventId, payloadHash, clock.UtcNow);
            store.Add(webhookEvent);
            // Potwierdzenie odbioru przeżywa błąd dalszego mapowania; ponowienie tego samego webhooka dokończy pracę.
            await store.Save(ct);
        }

        var session = await store.GetAgentSessionByConversation(conversationId, ct) ?? throw new NotFoundError("Unknown ElevenLabs conversation.");
        var interview = await store.GetAgentInterview(session.InterviewId, ct) ?? throw new NotFoundError();
        webhookEvent.AttachToVisit(interview.VisitProcessId);
        if (session.Status != InterviewSessionStatus.Completed)
        {
            var transcript = data.TryGetProperty("transcript", out var transcriptElement) ? transcriptElement.GetRawText() : "[]";
            var analysis = data.TryGetProperty("analysis", out var analysisElement) ? analysisElement.GetRawText() : "{}";
            var metadata = data.TryGetProperty("metadata", out var metadataElement) ? metadataElement.GetRawText() : "{}";
            var report = data.TryGetProperty("analysis", out var reportAnalysis) && reportAnalysis.TryGetProperty("transcript_summary", out var summary)
                ? summary.GetString() : null;
            var continuesInterview = session.Status == InterviewSessionStatus.Abandoned;
            session.Complete(transcript, analysis, metadata, clock.UtcNow);
            if (!continuesInterview && !await store.HasNewerAgentSession(interview.Id, session.CreatedAt, ct))
            {
                var outcomes = new List<ExtractionOutcome>();
                foreach (var completedSession in await store.GetAgentInterviewSessions(interview.Id, ct))
                {
                    if (string.IsNullOrWhiteSpace(completedSession.AnalysisJson)) continue;
                    try
                    {
                        using var analysisDocument = JsonDocument.Parse(completedSession.AnalysisJson);
                        if (analysisDocument.RootElement.TryGetProperty("data_collection_results", out var values))
                            outcomes.Add(extraction.Normalize(values));
                    }
                    catch (JsonException)
                    {
                        outcomes.Add(new(ExtractionStatus.Failed, null,
                            [new("data", "invalid_json", "Nie można odczytać danych sesji.")]));
                    }
                }
                var outcome = extraction.Merge(outcomes);
                var normalizedJson = outcome.Data is null ? null : JsonSerializer.Serialize(outcome.Data, JsonOptions);
                interview.RecordExtraction(normalizedJson, outcome.Data?.SchemaVersion, outcome.Status,
                    JsonSerializer.Serialize(outcome.Issues, JsonOptions), session.Id);
                if (outcome.Data is null || !await extraction.Import(interview.Id, session.Id, outcome.Data, ct))
                    interview.ImportFailed();
                interview.Complete(report, normalizedJson, clock.UtcNow);
                foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct)) invitation.Complete(clock.UtcNow);
            }
            var visit = await store.GetVisit(interview.VisitProcessId, ct);
            if (visit is not null) store.Add(new AuditEvent(visit.FacilityId, visit.Id, "elevenlabs-webhook", $"agent_session.completed:{session.Id}", clock.UtcNow));
        }
        webhookEvent.Process(clock.UtcNow);
        await store.Save(ct);
    }

    private async Task<AgentInterview> AuthorizedInterview(Guid interviewId, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await store.GetAgentInterview(interviewId, ct) ?? throw new NotFoundError();
        if (access.InterviewId is not null && access.InterviewId != interview.Id) throw new ForbiddenError();
        if (access.VisitId is not null && access.VisitId != interview.VisitProcessId) throw new ForbiddenError();
        if (access.InterviewId is null && access.VisitId is null) throw new ForbiddenError();
        if (access.InvitationId is not null)
        {
            var invitation = await store.GetInterviewInvitation(access.InvitationId.Value, ct) ?? throw new ForbiddenError();
            if (invitation.InterviewId != interview.Id || invitation.RevokedAt is not null || clock.UtcNow >= invitation.ExpiresAt) throw new ForbiddenError();
        }
        return interview;
    }

    private async Task<InterviewInvitation> ExecutableInvitation(string rawToken, CancellationToken ct)
    {
        var invitation = await FindInvitation(rawToken, ct);
        if (!invitation.IsUsable(clock.UtcNow)) throw new ConflictError("agent_invitation.inactive", "The invitation is no longer active.");
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        if (interview.Status is AgentInterviewStatus.Completed or AgentInterviewStatus.Cancelled or AgentInterviewStatus.Expired)
            throw new ConflictError("agent_interview.inactive", "The interview is no longer active.");
        await ActiveVisit(interview.VisitProcessId, ct);
        return invitation;
    }

    private async Task<InterviewInvitation> ReviewInvitation(string rawToken, CancellationToken ct)
    {
        var invitation = await FindInvitation(rawToken, ct);
        if (!invitation.CanReview(clock.UtcNow)) throw new ConflictError("agent_invitation.inactive", "The invitation is no longer active.");
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        await ActiveVisit(interview.VisitProcessId, ct);
        return invitation;
    }

    private async Task<InterviewInvitation> FindInvitation(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 500) throw new NotFoundError();
        return await store.FindInterviewInvitation(credentials.Hash(rawToken), ct) ?? throw new NotFoundError();
    }

    private async Task<VisitProcess> ActiveVisit(Guid visitId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError();
        visit.Expire(clock.UtcNow);
        if (visit.Status is VisitStatus.Cancelled or VisitStatus.Expired) throw new ConflictError("visit.inactive", "The visit is inactive.");
        return visit;
    }

    private async Task<AgentInterviewView> View(AgentInterview interview, CancellationToken ct)
    {
        var visit = await store.GetVisit(interview.VisitProcessId, ct) ?? throw new NotFoundError();
        var details = new VisitDetails(visit.Id, visit.ExternalVisitId, visit.ScheduledAt, visit.TimeZone, visit.ServiceExpiresAt,
            new(visit.AssignedClinicianId, visit.DoctorName, visit.DoctorSpecialty),
            new(visit.FacilityId, visit.FacilityName, visit.FacilityAddress), visit.Room, visit.VisitType,
            visit.LocationInstructions, VisitStatusName(visit.Status), interview.Id, Status(interview.Status));
        return new(interview.Id, "Wywiad przed wizytą", visit.ScheduledAt, Status(interview.Status), interview.InterviewType,
            await store.CountAgentInterviewSessions(interview.Id, ct), details);
    }

    private static string Status(AgentInterviewStatus status) => status == AgentInterviewStatus.InProgress ? "in_progress" : status.ToString().ToLowerInvariant();
    private static string VisitStatusName(VisitStatus status) => status switch
    {
        VisitStatus.NotStarted => "not_started", VisitStatus.InProgress => "in_progress",
        VisitStatus.AwaitingApproval => "awaiting_approval", VisitStatus.RequiresSupplementation => "requires_supplementation",
        _ => status.ToString().ToLowerInvariant()
    };
    public static string PayloadHash(ReadOnlySpan<byte> payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
}
