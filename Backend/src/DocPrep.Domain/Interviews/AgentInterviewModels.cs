using DocPrep.Domain.Common;

namespace DocPrep.Domain.Interviews;

public enum AgentInterviewStatus { Pending, InProgress, Processing, Completed, Cancelled, Expired }
public enum InterviewSessionMode { Voice, Text }
public enum InterviewSessionStatus { Created, TokenIssued, Connected, Processing, Completed, Failed, Abandoned }

public sealed class AgentInterview
{
    private AgentInterview() { }

    public AgentInterview(Guid visitProcessId, string interviewType, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        VisitProcessId = visitProcessId;
        InterviewType = Guard.Required(interviewType, nameof(interviewType), 100);
        Status = AgentInterviewStatus.Pending;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public AgentInterviewStatus Status { get; private set; }
    public string InterviewType { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FinalReport { get; private set; }
    public string? StructuredDataJson { get; private set; }

    public void Start(DateTimeOffset now)
    {
        if (Status is AgentInterviewStatus.Completed or AgentInterviewStatus.Cancelled or AgentInterviewStatus.Expired)
            throw new DomainException("agent_interview.inactive", "The interview cannot be started.");
        StartedAt ??= now;
        Status = AgentInterviewStatus.InProgress;
    }

    public void Complete(string? finalReport, string? structuredDataJson, DateTimeOffset now)
    {
        if (Status == AgentInterviewStatus.Cancelled) throw new DomainException("agent_interview.cancelled", "The interview is cancelled.");
        FinalReport = finalReport;
        StructuredDataJson = structuredDataJson;
        CompletedAt = now;
        Status = AgentInterviewStatus.Completed;
    }

    public void Processing()
    {
        if (Status is AgentInterviewStatus.Pending or AgentInterviewStatus.InProgress)
            Status = AgentInterviewStatus.Processing;
    }
}

public sealed class AgentInterviewSession
{
    private AgentInterviewSession() { }

    public AgentInterviewSession(Guid interviewId, Guid? userId, InterviewSessionMode mode, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        InterviewId = interviewId;
        UserId = userId;
        Mode = mode;
        Provider = "elevenlabs";
        Status = InterviewSessionStatus.Created;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid InterviewId { get; private set; }
    public Guid? UserId { get; private set; }
    public InterviewSessionMode Mode { get; private set; }
    public string Provider { get; private set; } = "";
    public string? ProviderConversationId { get; private set; }
    public InterviewSessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConnectedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? TranscriptJson { get; private set; }
    public string? AnalysisJson { get; private set; }
    public string? MetadataJson { get; private set; }

    public void CredentialIssued(string? conversationId, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(conversationId)) ProviderConversationId = Guard.Required(conversationId, nameof(conversationId), 200);
        Status = InterviewSessionStatus.TokenIssued;
    }

    public void SetConversation(string conversationId, DateTimeOffset now)
    {
        var normalized = Guard.Required(conversationId, nameof(conversationId), 200);
        if (ProviderConversationId is not null && !ProviderConversationId.Equals(normalized, StringComparison.Ordinal))
            throw new DomainException("agent_session.conversation_mismatch", "A different provider conversation is already assigned.");
        ProviderConversationId = normalized;
        ConnectedAt ??= now;
        if (Status is InterviewSessionStatus.Created or InterviewSessionStatus.TokenIssued) Status = InterviewSessionStatus.Connected;
    }

    public void Complete(string transcriptJson, string analysisJson, string metadataJson, DateTimeOffset now)
    {
        TranscriptJson = transcriptJson;
        AnalysisJson = analysisJson;
        MetadataJson = metadataJson;
        CompletedAt = now;
        Status = InterviewSessionStatus.Completed;
    }

    public void Fail() => Status = InterviewSessionStatus.Failed;
    public void End(bool continuesInterview)
    {
        if (Status == InterviewSessionStatus.Completed) return;
        // Abandoned transport continues in another session; its webhook still stores the transcript.
        Status = continuesInterview ? InterviewSessionStatus.Abandoned : InterviewSessionStatus.Processing;
    }
}

public sealed class InterviewInvitation
{
    private InterviewInvitation() { }

    public InterviewInvitation(Guid interviewId, string tokenHash, DateTimeOffset expiresAt, int maxSessionCount, DateTimeOffset now)
    {
        if (expiresAt <= now) throw new DomainException("agent_invitation.invalid_expiry", "Invitation expiry must be in the future.");
        if (maxSessionCount is < 1 or > 10) throw new DomainException("agent_invitation.invalid_limit", "Session limit must be between 1 and 10.");
        Id = Guid.NewGuid();
        InterviewId = interviewId;
        TokenHash = Guard.Required(tokenHash, nameof(tokenHash), 128);
        CreatedAt = now;
        ExpiresAt = expiresAt;
        MaxSessionCount = maxSessionCount;
    }

    public Guid Id { get; private set; }
    public Guid InterviewId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? FirstOpenedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int SessionCount { get; private set; }
    public int MaxSessionCount { get; private set; }
    public uint ConcurrencyVersion { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && CompletedAt is null && now < ExpiresAt && SessionCount < MaxSessionCount;
    public void Open(DateTimeOffset now)
    {
        EnsureUsable(now);
        FirstOpenedAt ??= now;
    }
    public void UseSession(DateTimeOffset now)
    {
        EnsureUsable(now);
        SessionCount++;
        ConcurrencyVersion++;
    }
    public void Complete(DateTimeOffset now) { CompletedAt ??= now; ConcurrencyVersion++; }
    public void Revoke(DateTimeOffset now) { RevokedAt ??= now; ConcurrencyVersion++; }
    private void EnsureUsable(DateTimeOffset now)
    {
        if (RevokedAt is not null) throw new DomainException("agent_invitation.revoked", "The invitation was revoked.");
        if (CompletedAt is not null) throw new DomainException("agent_invitation.completed", "The interview was already completed.");
        if (now >= ExpiresAt) throw new DomainException("agent_invitation.expired", "The invitation expired.");
        if (SessionCount >= MaxSessionCount) throw new DomainException("agent_invitation.session_limit", "The invitation session limit was reached.");
    }
}

public sealed class ExternalWebhookEvent
{
    private ExternalWebhookEvent() { }
    public ExternalWebhookEvent(string provider, string externalEventId, string payloadHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); Provider = Guard.Required(provider, nameof(provider), 50);
        ExternalEventId = Guard.Required(externalEventId, nameof(externalEventId), 300);
        PayloadHash = Guard.Required(payloadHash, nameof(payloadHash), 128); ReceivedAt = now;
    }
    public Guid Id { get; private set; }
    public string Provider { get; private set; } = "";
    public string ExternalEventId { get; private set; } = "";
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string PayloadHash { get; private set; } = "";
    public void Process(DateTimeOffset now) => ProcessedAt ??= now;
}
