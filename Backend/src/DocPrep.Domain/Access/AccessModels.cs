using DocPrep.Domain.Common;

namespace DocPrep.Domain.Access;

public sealed class PatientIdentity
{
    private PatientIdentity() { }
    public PatientIdentity(string correlationKey, string encryptedPesel, DateTimeOffset now)
    { Id = Guid.NewGuid(); CorrelationKey = Guard.Required(correlationKey, nameof(correlationKey), 128); EncryptedPesel = encryptedPesel; CreatedAt = now; }
    public Guid Id { get; private set; }
    public string CorrelationKey { get; private set; } = "";
    public string EncryptedPesel { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class PatientAccessGrant
{
    private PatientAccessGrant() { }
    public PatientAccessGrant(Guid visitId, string linkTokenHash, string visitCodeHash, int generation, DateTimeOffset validUntil, DateTimeOffset now)
    { Id = Guid.NewGuid(); VisitProcessId = visitId; LinkTokenHash = linkTokenHash; VisitCodeHash = visitCodeHash; Generation = generation; ValidUntil = validUntil; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public string LinkTokenHash { get; private set; } = "";
    public string VisitCodeHash { get; private set; } = "";
    public int Generation { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsValid(DateTimeOffset now) => RevokedAt is null && now < ValidUntil;
    public void MarkOpened(DateTimeOffset now) { if (!IsValid(now)) throw new DomainException("access.expired", "Access has expired."); OpenedAt ??= now; }
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public enum DeliveryStatus { Pending, Delivered, Failed }
public sealed class DeliveryAttempt
{
    private DeliveryAttempt() { }
    public DeliveryAttempt(Guid visitId, Guid accessGrantId, string channel, string encryptedDestination, DateTimeOffset now)
    { Id = Guid.NewGuid(); VisitProcessId = visitId; AccessGrantId = accessGrantId; Channel = channel; EncryptedDestination = encryptedDestination; Status = DeliveryStatus.Pending; AttemptedAt = now; }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public Guid AccessGrantId { get; private set; }
    public string Channel { get; private set; } = ""; public string EncryptedDestination { get; private set; } = "";
    public DeliveryStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
    public void Delivered(string? providerId) { Status = DeliveryStatus.Delivered; ProviderMessageId = providerId; }
    public void Failed(string reason) { Status = DeliveryStatus.Failed; FailureReason = reason; }
}
