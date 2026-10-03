using System.Security.Cryptography;
using System.Text;

namespace HealthPrep.Domain.Agents;

// A visit may have several transport sessions, but only one interview result.
public sealed class AgentInterview
{
    private AgentInterview() { }
    public AgentInterview(Guid appointmentId) { Id = Guid.NewGuid(); AppointmentId = appointmentId; }
    public Guid Id { get; private set; }
    public Guid AppointmentId { get; private set; }
    public string Status { get; private set; } = "pending";
    public string? Summary { get; private set; }
    public string StructuredDataJson { get; private set; } = "{}";
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ResultSessionStartedAt { get; private set; }
    public void Start() => Status = "in_progress";
    public void Processing() { if (Status != "completed") Status = "processing"; }
    public void Complete(AgentSession session, DateTimeOffset now)
    {
        // A late webhook from an earlier voice/text session must not replace a newer result.
        if (ResultSessionStartedAt > session.StartedAt) return;
        Status = "completed";
        Summary = session.Summary;
        StructuredDataJson = session.StructuredDataJson;
        ResultSessionStartedAt = session.StartedAt;
        CompletedAt = now;
    }
}

public sealed class AgentSession
{
    private AgentSession() { }
    public AgentSession(Guid interviewId, Guid? invitationId, string mode, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); InterviewId = interviewId; InvitationId = invitationId;
        Mode = mode; StartedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid InterviewId { get; private set; }
    public Guid? InvitationId { get; private set; }
    public string Mode { get; private set; } = "voice";
    public string Status { get; private set; } = "connecting";
    public string? ProviderConversationId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? TranscriptJson { get; private set; }
    public string? AnalysisJson { get; private set; }
    public string? MetadataJson { get; private set; }
    public string StructuredDataJson { get; private set; } = "{}";
    public string? Summary { get; private set; }
    public string TechnicalUserId => $"session_{Id:N}";
    public bool ContinuesInterview { get; private set; }
    public void Bind(string conversationId)
    {
        if (ProviderConversationId is not null && ProviderConversationId != conversationId)
            throw new InvalidOperationException("Conversation is already bound.");
        ProviderConversationId = conversationId;
        if (Status == "connecting") Status = "connected";
    }
    public void End(bool continuesInterview = false) { ContinuesInterview = continuesInterview; if (Status != "completed") Status = "processing"; }
    public void Fail() { if (Status != "completed") Status = "failed"; }
    public void Complete(string transcript, string analysis, string metadata, string structuredData, string? summary, DateTimeOffset now)
    {
        if (Status == "completed") return;
        TranscriptJson = transcript; AnalysisJson = analysis; MetadataJson = metadata;
        StructuredDataJson = structuredData; Summary = summary; CompletedAt = now; Status = "completed";
    }
}

public sealed class InterviewInvitation
{
    private InterviewInvitation() { }
    public InterviewInvitation(Guid interviewId, string tokenHash, DateTimeOffset expiresAt)
    { Id = Guid.NewGuid(); InterviewId = interviewId; TokenHash = tokenHash; ExpiresAt = expiresAt; }
    public Guid Id { get; private set; }
    public Guid InterviewId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int SessionCount { get; private set; }
    public int MaxSessions { get; private set; } = 3;
    public bool CanAccess(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
    public bool CanStart(DateTimeOffset now) => CanAccess(now) && SessionCount < MaxSessions;
    public void Revoke(DateTimeOffset now) => RevokedAt = now;
    public static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class ElevenWebhookSignature
{
    public static bool Verify(byte[] rawBody, string header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header)) return false;
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=")) { if (timestamp is not null) return false; timestamp = part[2..]; }
            if (part.StartsWith("v0=")) signatures.Add(part[3..]);
        }
        // Match the provider's 30-minute retry tolerance, also rejecting future timestamps.
        if (!long.TryParse(timestamp, out var seconds) || Math.Abs((double)now.ToUnixTimeSeconds() - seconds) > 1800) return false;
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var input = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(input, 0); rawBody.CopyTo(input, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), input);
        foreach (var signature in signatures)
        {
            try { if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature))) return true; }
            catch (FormatException) { }
        }
        return false;
    }
}
