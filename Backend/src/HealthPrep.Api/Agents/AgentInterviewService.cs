using System.Text.Json;
using HealthPrep.Domain.Agents;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthPrep.Api.Agents;

public sealed class AgentInterviewService(HealthPrepDbContext db, InterviewAccess access, ElevenLabsProvider provider, IOptions<ElevenLabsOptions> options)
{
    public async Task<object> GetVisit(Guid visitId, HttpContext context, CancellationToken ct)
    {
        var patient = access.PatientId(context);
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == visitId && x.ExternalPatientId == patient, ct)
            ?? throw new AgentApiException(404, "Visit was not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var interview = await EnsureInterview(visitId, ct);
        await transaction.CommitAsync(ct);
        return new { interviewId = interview.Id, interview = Info(interview, appointment.ScheduledAt) };
    }

    public async Task<object> CreateInvitation(Guid visitId, Guid tenantId, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == visitId && x.TenantId == tenantId, ct)
            ?? throw new AgentApiException(404, "Visit was not found.");
        var now = DateTimeOffset.UtcNow;
        if (appointment.ScheduledAt <= now) throw new AgentApiException(410, "Visit has already started.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var interview = await EnsureInterview(visitId, ct);
        await Lock(interview.Id, ct);
        if (interview.Status == "completed") throw new AgentApiException(410, "Interview is already completed.");
        var old = await db.InterviewInvitations.Where(x => x.InterviewId == interview.Id && x.RevokedAt == null).ToListAsync(ct);
        foreach (var invitation in old) invitation.Revoke(now);
        var token = InterviewInvitation.CreateToken();
        var expires = now.AddDays(7) < appointment.ScheduledAt ? now.AddDays(7) : appointment.ScheduledAt;
        var created = new InterviewInvitation(interview.Id, InterviewInvitation.Hash(token), expires);
        db.InterviewInvitations.Add(created);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new { invitationId = created.Id, invitationUrl = $"/i/{token}", expiresAt = expires };
    }

    public async Task RevokeInvitation(Guid visitId, Guid tenantId, CancellationToken ct)
    {
        if (!await db.Appointments.AnyAsync(x => x.Id == visitId && x.TenantId == tenantId, ct))
            throw new AgentApiException(404, "Visit was not found.");
        var interview = await db.AgentInterviews.SingleOrDefaultAsync(x => x.AppointmentId == visitId, ct);
        if (interview is null) return;
        await db.InterviewInvitations.Where(x => x.InterviewId == interview.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<object> GetInfo(InterviewScope scope, CancellationToken ct)
    {
        var date = await db.Appointments.Where(x => x.Id == scope.Interview.AppointmentId).Select(x => x.ScheduledAt).SingleAsync(ct);
        return Info(scope.Interview, date);
    }
    private static object Info(AgentInterview interview, DateTimeOffset date) =>
        new { displayName = "Wywiad przed wizytą", visitDate = date, status = interview.Status };

    public async Task<object> Start(InterviewScope scope, string mode, CancellationToken ct)
    {
        if (mode is not ("voice" or "text")) throw new AgentApiException(400, "Mode must be voice or text.");
        provider.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var interview = await Lock(scope.Interview.Id, ct);
        var now = DateTimeOffset.UtcNow;
        if (interview.Status == "completed") throw new AgentApiException(410, "Interview is already completed.");
        if (!await db.Appointments.AnyAsync(x => x.Id == interview.AppointmentId && x.ScheduledAt > now, ct))
            throw new AgentApiException(410, "Visit has already started.");
        if (scope.Invitation is not null)
        {
            // Conditional SQL increment prevents two simultaneous requests exceeding the invitation limit.
            var reserved = await db.InterviewInvitations.Where(x => x.Id == scope.Invitation.Id && x.RevokedAt == null && x.ExpiresAt > now && x.SessionCount < x.MaxSessions)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.SessionCount, i => i.SessionCount + 1), ct);
            if (reserved == 0) throw new AgentApiException(429, "Invitation is inactive or its session limit was reached.");
        }
        else if (await db.AgentSessions.CountAsync(x => x.InterviewId == interview.Id, ct) >= 12)
            throw new AgentApiException(429, "Interview session limit reached.");
        var session = new AgentSession(interview.Id, scope.Invitation?.Id, mode, now);
        var credential = await provider.CreateCredential(mode, ct);
        if (!string.IsNullOrWhiteSpace(credential.ConversationId)) session.Bind(credential.ConversationId);
        db.AgentSessions.Add(session);
        interview.Start();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // No personal identifiers or API keys are exposed to the client/provider.
        var result = new Dictionary<string, object?>
        {
            ["sessionId"] = session.Id, ["mode"] = mode, ["provider"] = "elevenlabs",
            ["conversationId"] = credential.ConversationId, ["userId"] = session.TechnicalUserId,
            ["dynamicVariables"] = new { language = "pl", visit_type = "wywiad przed wizytą" },
            [mode == "voice" ? "conversationToken" : "signedUrl"] = credential.Value
        };
        return result;
    }

    public async Task Bind(AgentSession session, string conversationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || conversationId.Length > 200)
            throw new AgentApiException(400, "Conversation identifier is invalid.");
        if (session.ProviderConversationId is not null && session.ProviderConversationId != conversationId)
            throw new AgentApiException(409, "Conversation identifier does not match the issued credential.");
        if (session.ProviderConversationId is null && !await provider.OwnsConversation(conversationId, session.TechnicalUserId, ct))
            throw new AgentApiException(403, "Provider conversation ownership could not be confirmed.");
        session.Bind(conversationId);
        await db.SaveChangesAsync(ct);
    }

    public async Task End(AgentSession session, bool continuesInterview, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var interview = await Lock(session.InterviewId, ct);
        await db.Entry(session).ReloadAsync(ct);
        session.End(continuesInterview);
        if (!continuesInterview) interview.Processing();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<object> Result(InterviewScope scope, CancellationToken ct)
    {
        await db.Entry(scope.Interview).ReloadAsync(ct);
        var interview = scope.Interview;
        return new { status = interview.Status, summary = interview.Summary, structuredData = JsonSerializer.Deserialize<JsonElement>(interview.StructuredDataJson) };
    }

    public async Task ProcessWebhook(JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("type", out var type))
            throw new AgentApiException(400, "Webhook payload is invalid.");
        if (type.GetString() != "post_call_transcription") return;
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("agent_id", out var agent) || agent.GetString() != options.Value.AgentId ||
            !data.TryGetProperty("conversation_id", out var conversation) || string.IsNullOrWhiteSpace(conversation.GetString()))
            throw new AgentApiException(400, "Webhook conversation is invalid.");
        var id = conversation.GetString()!;
        var session = await db.AgentSessions.SingleOrDefaultAsync(x => x.ProviderConversationId == id, ct);
        // A signed webhook can arrive before the client's binding request.
        if (session is null && data.TryGetProperty("user_id", out var user) && user.GetString() is { } userId &&
            userId.StartsWith("session_") && Guid.TryParseExact(userId[8..], "N", out var sessionId))
            session = await db.AgentSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.ProviderConversationId == null, ct);
        if (session is null) throw new AgentApiException(503, "Conversation binding is not available yet.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var interview = await Lock(session.InterviewId, ct);
        await db.Entry(session).ReloadAsync(ct);
        if (session.Status == "completed") return;
        session.Bind(id);
        if (!data.TryGetProperty("status", out var status) || status.GetString() != "done")
        { session.Fail(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return; }
        var analysis = data.TryGetProperty("analysis", out var a) && a.ValueKind == JsonValueKind.Object ? a : JsonSerializer.SerializeToElement(new { });
        string Raw(JsonElement source, string property, string fallback) => source.TryGetProperty(property, out var value) ? value.GetRawText() : fallback;
        var summary = analysis.TryGetProperty("transcript_summary", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        session.Complete(Raw(data, "transcript", "[]"), analysis.GetRawText(), Raw(data, "metadata", "{}"),
            Raw(analysis, "data_collection_results", "{}"), summary, DateTimeOffset.UtcNow);
        if (!session.ContinuesInterview && !await db.AgentSessions.AnyAsync(x => x.InterviewId == interview.Id && x.StartedAt > session.StartedAt, ct))
            interview.Complete(session, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // The existing appointment approval/sharing workflow remains an explicit patient action.
    }

    private async Task<AgentInterview> EnsureInterview(Guid visitId, CancellationToken ct)
    {
        // Lock the visit row to serialize concurrent creation of its unique interview.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM healthprep.\"Appointments\" WHERE \"Id\" = {visitId} FOR UPDATE", ct);
        var interview = await db.AgentInterviews.SingleOrDefaultAsync(x => x.AppointmentId == visitId, ct);
        if (interview is not null) return interview;
        interview = new(visitId);
        db.AgentInterviews.Add(interview);
        await db.SaveChangesAsync(ct);
        return interview;
    }

    private async Task<AgentInterview> Lock(Guid id, CancellationToken ct)
    {
        var interview = await db.AgentInterviews.FromSqlInterpolated($"SELECT * FROM healthprep.agent_interviews WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(ct);
        await db.Entry(interview).ReloadAsync(ct);
        return interview;
    }
}
