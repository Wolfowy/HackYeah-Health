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
    public async Task<IAsyncDisposable> LockFacilitySchedule(Guid facilityId, CancellationToken ct)
    {
        // A connection-level lock covers committed saves and is shared by admin and integration bookings.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({facilityId.ToString()}, 0))", ct);
            return new ScheduleLock(db, facilityId);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    private sealed class ScheduleLock(DocPrepDbContext db, Guid facilityId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({facilityId.ToString()}, 0))", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    public Task<PatientIdentity?> FindPatient(string correlationKey, CancellationToken ct) => db.Patients.SingleOrDefaultAsync(x => x.CorrelationKey == correlationKey, ct);
    public Task<VisitProcess?> FindVisit(Guid facilityId, string externalVisitId, CancellationToken ct) => db.Visits.SingleOrDefaultAsync(x => x.FacilityId == facilityId && x.ExternalVisitId == externalVisitId, ct);
    public Task<VisitProcess?> GetVisit(Guid id, CancellationToken ct) => db.Visits.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<InterviewDraft?> GetDraft(Guid visitId, CancellationToken ct) => db.Drafts.Include(x => x.Answers).Include(x => x.Symptoms).ThenInclude(x => x.Timeline)
        .Include(x => x.Medications).Include(x => x.Allergies).Include(x => x.ChronicConditions).Include(x => x.PatientQuestions).Include(x => x.Clarifications).AsSplitQuery().SingleOrDefaultAsync(x => x.VisitProcessId == visitId, ct);
    public Task<PatientAccessGrant?> FindAccessByLinkHash(string hash, CancellationToken ct) => db.AccessGrants.SingleOrDefaultAsync(x => x.LinkTokenHash == hash, ct);
    public Task<PatientAccessGrant?> FindAccessByCodeHash(string hash, CancellationToken ct) => db.AccessGrants.SingleOrDefaultAsync(x => x.VisitCodeHash == hash, ct);
    public async Task<IReadOnlyList<PatientAccessGrant>> GetAccessGrants(Guid visitId, CancellationToken ct) => await db.AccessGrants.Where(x => x.VisitProcessId == visitId).ToListAsync(ct);
    public async Task<IReadOnlyList<VisitProcess>> GetFacilityVisits(Guid facilityId, CancellationToken ct, string? clinicianId = null) =>
        await db.Visits.Where(x => x.FacilityId == facilityId && (clinicianId == null || x.AssignedClinicianId == clinicianId))
            .OrderBy(x => x.ScheduledAt).ToListAsync(ct);
    public Task<ReceptionDetails?> GetReceptionDetails(Guid visitId, CancellationToken ct) =>
        db.ReceptionDetails.SingleOrDefaultAsync(x => x.VisitProcessId == visitId, ct);
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
    public Task<AgentInterview?> GetAgentInterview(Guid id, CancellationToken ct) => db.AgentInterviews.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<AgentInterview?> GetAgentInterviewByVisit(Guid visitId, CancellationToken ct) => db.AgentInterviews.OrderByDescending(x => x.Generation).FirstOrDefaultAsync(x => x.VisitProcessId == visitId, ct);
    public async Task<IReadOnlyList<AgentInterview>> GetAgentInterviewsByVisit(Guid visitId, CancellationToken ct) =>
        await db.AgentInterviews.Where(x => x.VisitProcessId == visitId).OrderBy(x => x.Generation).ToListAsync(ct);
    public Task<int> CountAgentInterviews(Guid visitId, CancellationToken ct) => db.AgentInterviews.CountAsync(x => x.VisitProcessId == visitId, ct);
    public Task<AgentInterviewSession?> GetAgentInterviewSession(Guid id, CancellationToken ct) => db.AgentInterviewSessions.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<AgentInterviewSession>> GetAgentInterviewSessions(Guid interviewId, CancellationToken ct) => await db.AgentInterviewSessions.Where(x => x.InterviewId == interviewId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    public Task<int> CountAgentInterviewSessions(Guid interviewId, CancellationToken ct) => db.AgentInterviewSessions.CountAsync(x => x.InterviewId == interviewId, ct);
    public Task<bool> HasNewerAgentSession(Guid interviewId, DateTimeOffset createdAt, CancellationToken ct) => db.AgentInterviewSessions.AnyAsync(x => x.InterviewId == interviewId && x.CreatedAt > createdAt, ct);
    public Task<AgentInterviewSession?> GetAgentSessionByConversation(string conversationId, CancellationToken ct) => db.AgentInterviewSessions.SingleOrDefaultAsync(x => x.ProviderConversationId == conversationId, ct);
    public Task<InterviewInvitation?> FindInterviewInvitation(string tokenHash, CancellationToken ct) => db.InterviewInvitations.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
    public Task<InterviewInvitation?> GetInterviewInvitation(Guid id, CancellationToken ct) => db.InterviewInvitations.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<InterviewInvitation>> GetInterviewInvitations(Guid interviewId, CancellationToken ct) => await db.InterviewInvitations.Where(x => x.InterviewId == interviewId).ToListAsync(ct);
    public Task<ExternalWebhookEvent?> GetWebhookEvent(string provider, string externalEventId, CancellationToken ct) => db.ExternalWebhookEvents.SingleOrDefaultAsync(x => x.Provider == provider && x.ExternalEventId == externalEventId, ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public void Remove<T>(T entity) where T : class => db.Remove(entity);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task DeletePatientData(Guid patientIdentityId, CancellationToken ct)
    { await db.Patients.Where(x => x.Id == patientIdentityId).ExecuteDeleteAsync(ct); }
}
