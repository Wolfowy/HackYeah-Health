using DocPrep.Domain.Common;
using DocPrep.Domain.Visits;

namespace DocPrep.UnitTests;

public sealed class VisitProcessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    private static VisitProcess Visit() => new(Guid.NewGuid(), Guid.NewGuid(), "visit-1", Now.AddDays(2), Now.AddDays(3), "doctor-1", ContactChannel.Sms, Now);

    [Fact] public void Opening_link_starts_process() { var x = Visit(); x.Open(Now); Assert.Equal(VisitStatus.InProgress, x.Status); }
    [Fact] public void Approval_without_consent_remains_awaiting_sharing_decision() { var x = Visit(); x.Open(Now); x.RegisterApprovedVersion(Guid.NewGuid(), Now); x.PublishApprovedVersion(false, Now); Assert.Equal(VisitStatus.AwaitingApproval, x.Status); }
    [Fact] public void Approval_with_active_consent_is_shared() { var x = Visit(); var version = Guid.NewGuid(); x.Open(Now); x.RegisterApprovedVersion(version, Now); x.PublishApprovedVersion(true, Now); Assert.Equal(VisitStatus.Shared, x.Status); Assert.Equal(version, x.LatestSharedVersionId); }
    [Fact] public void Revoking_consent_blocks_shared_version_and_returns_to_work() { var x = Visit(); x.Open(Now); x.RegisterApprovedVersion(Guid.NewGuid(), Now); x.PublishApprovedVersion(true, Now); x.RevokeConsent(Now); Assert.Equal(VisitStatus.InProgress, x.Status); Assert.Null(x.LatestSharedVersionId); }
    [Fact] public void Expired_process_rejects_patient_work() { var x = Visit(); x.Expire(Now.AddDays(4)); Assert.Throws<DomainException>(() => x.Open(Now.AddDays(4))); }
    [Fact] public void Cancelled_process_blocks_facility_access() { var x = Visit(); x.Open(Now); x.RegisterApprovedVersion(Guid.NewGuid(), Now); x.PublishApprovedVersion(true, Now); x.Cancel(Now); Assert.False(x.CanFacilityReadSharedReport(true)); }
}
