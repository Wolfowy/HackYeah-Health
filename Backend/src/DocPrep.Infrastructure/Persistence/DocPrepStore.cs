using Microsoft.EntityFrameworkCore;
using DocPrep.Application.Abstractions;
using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Sharing;
using DocPrep.Domain.Supplementation;
using DocPrep.Domain.Visits;

namespace DocPrep.Infrastructure.Persistence;

internal sealed class DocPrepStore(DocPrepDbContext db) : IDocPrepStore
{
    public Task<PatientIdentity?> FindPatient(string correlationKey, CancellationToken ct) => db.Patients.SingleOrDefaultAsync(x => x.CorrelationKey == correlationKey, ct);
    public Task<VisitProcess?> GetVisit(Guid id, CancellationToken ct) => db.Visits.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<InterviewDraft?> GetDraft(Guid visitId, CancellationToken ct) => db.Drafts.Include(x => x.Answers).Include(x => x.Symptoms).ThenInclude(x => x.Timeline)
        .Include(x => x.Medications).Include(x => x.Allergies).Include(x => x.ChronicConditions).Include(x => x.PatientQuestions).Include(x => x.Clarifications).AsSplitQuery().SingleOrDefaultAsync(x => x.VisitProcessId == visitId, ct);
    public Task<PatientAccessGrant?> FindAccessByLinkHash(string hash, CancellationToken ct) => db.AccessGrants.SingleOrDefaultAsync(x => x.LinkTokenHash == hash, ct);
    public Task<PatientAccessGrant?> FindAccessByCodeHash(string hash, CancellationToken ct) => db.AccessGrants.SingleOrDefaultAsync(x => x.VisitCodeHash == hash, ct);
    public async Task<IReadOnlyList<PatientAccessGrant>> GetAccessGrants(Guid visitId, CancellationToken ct) => await db.AccessGrants.Where(x => x.VisitProcessId == visitId).ToListAsync(ct);
    public async Task<IReadOnlyList<VisitProcess>> GetFacilityVisits(Guid facilityId, CancellationToken ct) => await db.Visits.Where(x => x.FacilityId == facilityId).OrderBy(x => x.ScheduledAt).ToListAsync(ct);
    public async Task<IReadOnlyList<VisitProcess>> GetPatientVisits(Guid patientIdentityId, CancellationToken ct) => await db.Visits.Where(x => x.PatientIdentityId == patientIdentityId).OrderBy(x => x.ScheduledAt).ToListAsync(ct);
    public async Task<IReadOnlyList<ReportVersion>> GetVersions(Guid visitId, CancellationToken ct) => await db.ReportVersions.Where(x => x.VisitProcessId == visitId).OrderBy(x => x.VersionNumber).ToListAsync(ct);
    public Task<ReportVersion?> GetVersion(Guid visitId, Guid versionId, CancellationToken ct) => db.ReportVersions.SingleOrDefaultAsync(x => x.VisitProcessId == visitId && x.Id == versionId, ct);
    public async Task<IReadOnlyList<ReportEvidence>> GetReportEvidence(Guid versionId, CancellationToken ct) => await db.ReportEvidence.Where(x => x.ReportVersionId == versionId).ToListAsync(ct);
    public Task<SharingConsent?> GetActiveConsent(Guid visitId, CancellationToken ct) => db.Consents.OrderByDescending(x => x.GrantedAt).FirstOrDefaultAsync(x => x.VisitProcessId == visitId && x.RevokedAt == null, ct);
    public async Task<IReadOnlyList<ObservationProposal>> GetObservations(Guid draftId, CancellationToken ct) => await db.Observations.Include(x => x.Evidence).Where(x => x.InterviewDraftId == draftId).ToListAsync(ct);
    public Task<SupplementationRound?> GetOpenRound(Guid visitId, CancellationToken ct) => db.SupplementationRounds.Include(x => x.Questions).ThenInclude(x => x.Answer).FirstOrDefaultAsync(x => x.VisitProcessId == visitId && (x.Status == SupplementationRoundStatus.Open || x.Status == SupplementationRoundStatus.AwaitingApproval), ct);
    public Task<int> CountRounds(Guid visitId, CancellationToken ct) => db.SupplementationRounds.CountAsync(x => x.VisitProcessId == visitId, ct);
    public Task<DeliveryAttempt?> GetLatestDelivery(Guid visitId, CancellationToken ct) => db.DeliveryAttempts.OrderByDescending(x => x.AttemptedAt).FirstOrDefaultAsync(x => x.VisitProcessId == visitId, ct);
    public Task<Domain.Privacy.DeletionRequest?> GetDeletionRequest(Guid id, CancellationToken ct) => db.DeletionRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public void Remove<T>(T entity) where T : class => db.Remove(entity);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task DeletePatientData(Guid patientIdentityId, CancellationToken ct)
    { await db.Patients.Where(x => x.Id == patientIdentityId).ExecuteDeleteAsync(ct); }
}
