namespace DocPrep.Domain.Sharing;

public sealed class SharingConsent
{
    private SharingConsent() { }
    public SharingConsent(Guid visitId, Guid facilityId, string decisionSource, DateTimeOffset now)
    { Id = Guid.NewGuid(); VisitProcessId = visitId; FacilityId = facilityId; DecisionSource = decisionSource; GrantedAt = now; }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public Guid FacilityId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string DecisionSource { get; private set; } = "";
    public bool IsActive => RevokedAt is null;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
