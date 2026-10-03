namespace DocPrep.Domain.Privacy;

public enum DeletionStatus { Pending, Processing, Completed, Failed }
public sealed class DeletionRequest
{
    private DeletionRequest() { }
    public DeletionRequest(string patientCorrelationKey, Guid requestedByFacilityId, string verificationReference, DateTimeOffset now)
    { Id = Guid.NewGuid(); PatientCorrelationKey = patientCorrelationKey; RequestedByFacilityId = requestedByFacilityId; VerificationReference = verificationReference; Status = DeletionStatus.Pending; RequestedAt = now; }
    public Guid Id { get; private set; }
    public string PatientCorrelationKey { get; private set; } = ""; public Guid RequestedByFacilityId { get; private set; }
    public string VerificationReference { get; private set; } = ""; public DeletionStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? LastError { get; private set; }
    public void Processing() => Status = DeletionStatus.Processing;
    public void Complete(DateTimeOffset now) { Status = DeletionStatus.Completed; CompletedAt = now; LastError = null; }
    public void Fail(string error) { Status = DeletionStatus.Failed; LastError = error; }
}

public sealed class AuditEvent
{
    private AuditEvent() { }
    public AuditEvent(Guid? facilityId, Guid? visitId, string actor, string action, DateTimeOffset now)
    { Id = Guid.NewGuid(); FacilityId = facilityId; VisitProcessId = visitId; Actor = actor; Action = action; OccurredAt = now; }
    public Guid Id { get; private set; }
    public Guid? FacilityId { get; private set; }
    public Guid? VisitProcessId { get; private set; }
    public string Actor { get; private set; } = ""; public string Action { get; private set; } = ""; public DateTimeOffset OccurredAt { get; private set; }
}
