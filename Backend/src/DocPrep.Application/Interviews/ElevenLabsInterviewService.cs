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

public sealed class ElevenLabsInterviewService(IDocPrepStore store, ICredentialService credentials, IElevenLabsClient elevenLabs, IClock clock)
{
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
        return new(interview.Id, Status(interview.Status), interview.FinalReport, interview.StructuredDataJson);
    }

    public async Task<InterviewInvitationAccess> AuthorizeInvitation(string rawToken, CancellationToken ct)
    {
        var invitation = await ValidInvitation(rawToken, ct);
        invitation.Open(clock.UtcNow);
        await store.Save(ct);
        return new(invitation.InterviewId, invitation.Id, invitation.ExpiresAt);
    }

    public async Task<AgentInterviewView> PublicView(string rawToken, CancellationToken ct)
    {
        var invitation = await ValidInvitation(rawToken, ct);
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        return await View(interview, ct);
    }

    public async Task<AgentSessionResult> CreateSession(Guid interviewId, InterviewSessionMode mode, AgentInterviewAccess access, CancellationToken ct)
    {
        var interview = await AuthorizedInterview(interviewId, access, ct);
        var visit = await ActiveVisit(interview.VisitProcessId, ct);
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
            return new(session.Id, mode.ToString().ToLowerInvariant(), session.Provider, credential.ConversationToken, credential.SignedUrl, credential.ConversationId);
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
        var invitation = await ValidInvitation(rawToken, ct);
        return await CreateSession(invitation.InterviewId, mode, AgentInterviewAccess.ForInvitation(invitation.InterviewId, invitation.Id), ct);
    }

    public async Task SetProviderConversation(Guid sessionId, string conversationId, AgentInterviewAccess access, CancellationToken ct)
    {
        var session = await store.GetAgentInterviewSession(sessionId, ct) ?? throw new NotFoundError();
        await AuthorizedInterview(session.InterviewId, access, ct);
        session.SetConversation(conversationId, clock.UtcNow);
        await store.Save(ct);
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
        if (await store.GetWebhookEvent("elevenlabs", externalEventId, ct) is not null) return;

        var session = await store.GetAgentSessionByConversation(conversationId, ct) ?? throw new NotFoundError("Unknown ElevenLabs conversation.");
        var webhookEvent = new ExternalWebhookEvent("elevenlabs", externalEventId, payloadHash, clock.UtcNow);
        store.Add(webhookEvent);
        if (session.Status != InterviewSessionStatus.Completed)
        {
            var transcript = data.TryGetProperty("transcript", out var transcriptElement) ? transcriptElement.GetRawText() : "[]";
            var analysis = data.TryGetProperty("analysis", out var analysisElement) ? analysisElement.GetRawText() : "{}";
            var metadata = data.TryGetProperty("metadata", out var metadataElement) ? metadataElement.GetRawText() : "{}";
            var report = data.TryGetProperty("analysis", out var reportAnalysis) && reportAnalysis.TryGetProperty("transcript_summary", out var summary)
                ? summary.GetString() : null;
            var structured = data.TryGetProperty("analysis", out var structuredAnalysis) && structuredAnalysis.TryGetProperty("data_collection_results", out var collection)
                ? collection.GetRawText() : analysis;
            session.Complete(transcript, analysis, metadata, clock.UtcNow);
            var interview = await store.GetAgentInterview(session.InterviewId, ct) ?? throw new NotFoundError();
            interview.Complete(report, structured, clock.UtcNow);
            foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct)) invitation.Complete(clock.UtcNow);
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

    private async Task<InterviewInvitation> ValidInvitation(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 500) throw new NotFoundError();
        var invitation = await store.FindInterviewInvitation(credentials.Hash(rawToken), ct) ?? throw new NotFoundError();
        if (!invitation.IsUsable(clock.UtcNow)) throw new ConflictError("agent_invitation.inactive", "The invitation is no longer active.");
        var interview = await store.GetAgentInterview(invitation.InterviewId, ct) ?? throw new NotFoundError();
        if (interview.Status is AgentInterviewStatus.Completed or AgentInterviewStatus.Cancelled or AgentInterviewStatus.Expired)
            throw new ConflictError("agent_interview.inactive", "The interview is no longer active.");
        await ActiveVisit(interview.VisitProcessId, ct);
        return invitation;
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
        return new(interview.Id, "Wywiad przed wizytą", visit.ScheduledAt, Status(interview.Status), interview.InterviewType,
            await store.CountAgentInterviewSessions(interview.Id, ct));
    }

    private static string Status(AgentInterviewStatus status) => status.ToString().ToLowerInvariant();
    public static string PayloadHash(ReadOnlySpan<byte> payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
}
