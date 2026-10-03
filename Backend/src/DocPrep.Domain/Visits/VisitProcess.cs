using DocPrep.Domain.Common;

namespace DocPrep.Domain.Visits;

public enum VisitStatus { NotStarted, InProgress, AwaitingApproval, Shared, RequiresSupplementation, Expired, Cancelled }
public enum ContactChannel { Sms, Email }

public sealed class VisitProcess
{
    private VisitProcess() { }

    public VisitProcess(Guid facilityId, Guid patientIdentityId, string externalVisitId, DateTimeOffset scheduledAt,
        DateTimeOffset serviceExpiresAt, string? assignedClinicianId, ContactChannel channel, DateTimeOffset now)
    {
        if (scheduledAt <= now) throw new DomainException("visit.invalid_date", "The visit must be scheduled in the future.");
        if (serviceExpiresAt < scheduledAt) throw new DomainException("visit.invalid_expiry", "Service expiry cannot precede the visit.");
        Id = Guid.NewGuid(); FacilityId = facilityId; PatientIdentityId = patientIdentityId;
        ExternalVisitId = Guard.Required(externalVisitId, nameof(externalVisitId), 100);
        ScheduledAt = scheduledAt; ServiceExpiresAt = serviceExpiresAt; AssignedClinicianId = assignedClinicianId?.Trim();
        ContactChannel = channel; Status = VisitStatus.NotStarted; CreatedAt = UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid FacilityId { get; private set; }
    public Guid PatientIdentityId { get; private set; }
    public string ExternalVisitId { get; private set; } = "";
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset ServiceExpiresAt { get; private set; }
    public string? AssignedClinicianId { get; private set; }
    public ContactChannel ContactChannel { get; private set; }
    public VisitStatus Status { get; private set; }
    public Guid? LatestApprovedVersionId { get; private set; }
    public Guid? LatestSharedVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint ConcurrencyVersion { get; private set; }

    public void Open(DateTimeOffset now)
    {
        EnsurePatientWorkAllowed(now);
        if (Status == VisitStatus.NotStarted) SetStatus(VisitStatus.InProgress, now);
    }

    public void MarkDraftChanged(DateTimeOffset now)
    {
        EnsurePatientWorkAllowed(now);
        SetStatus(VisitStatus.InProgress, now);
    }

    public void ReadyForApproval(DateTimeOffset now)
    {
        EnsurePatientWorkAllowed(now);
        SetStatus(VisitStatus.AwaitingApproval, now);
    }

    public void RegisterApprovedVersion(Guid versionId, DateTimeOffset now)
    {
        EnsurePatientWorkAllowed(now);
        LatestApprovedVersionId = versionId;
        SetStatus(VisitStatus.AwaitingApproval, now);
    }

    public void PublishApprovedVersion(bool hasActiveConsent, DateTimeOffset now)
    {
        if (LatestApprovedVersionId is null) throw new DomainException("report.approval_required", "An approved report is required.");
        if (hasActiveConsent) { LatestSharedVersionId = LatestApprovedVersionId; SetStatus(VisitStatus.Shared, now); }
        else SetStatus(VisitStatus.AwaitingApproval, now);
    }

    public void GrantConsent(DateTimeOffset now)
    {
        EnsurePatientWorkAllowed(now);
        if (LatestApprovedVersionId is null) throw new DomainException("consent.report_required", "An approved report is required.");
        LatestSharedVersionId = LatestApprovedVersionId;
        SetStatus(VisitStatus.Shared, now);
    }

    public void RevokeConsent(DateTimeOffset now)
    {
        EnsureNotCancelled();
        LatestSharedVersionId = null;
        SetStatus(VisitStatus.InProgress, now);
    }

    public void RequireSupplementation(DateTimeOffset now)
    {
        EnsureActive(now);
        if (LatestSharedVersionId is null) throw new DomainException("supplementation.shared_report_required", "A shared report is required.");
        SetStatus(VisitStatus.RequiresSupplementation, now);
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != VisitStatus.Cancelled && now >= ServiceExpiresAt) SetStatus(VisitStatus.Expired, now);
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureNotCancelled();
        LatestSharedVersionId = null;
        SetStatus(VisitStatus.Cancelled, now);
    }

    public bool CanFacilityReadSharedReport(bool activeConsent) =>
        activeConsent && LatestSharedVersionId is not null && Status != VisitStatus.Cancelled;

    private void EnsurePatientWorkAllowed(DateTimeOffset now)
    {
        EnsureNotCancelled();
        if (now >= ServiceExpiresAt || Status == VisitStatus.Expired)
            throw new DomainException("visit.expired", "The visit preparation process has expired.");
    }

    private void EnsureActive(DateTimeOffset now) => EnsurePatientWorkAllowed(now);
    private void EnsureNotCancelled() { if (Status == VisitStatus.Cancelled) throw new DomainException("visit.cancelled", "The visit process is cancelled."); }
    private void SetStatus(VisitStatus value, DateTimeOffset now) { Status = value; UpdatedAt = now; ConcurrencyVersion++; }
}
