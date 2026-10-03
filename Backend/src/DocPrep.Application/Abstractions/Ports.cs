using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Sharing;
using DocPrep.Domain.Supplementation;
using DocPrep.Domain.Visits;
using DocPrep.Application.Contracts;

namespace DocPrep.Application.Abstractions;

public interface IDocPrepStore
{
    Task<PatientIdentity?> FindPatient(string correlationKey, CancellationToken ct);
    Task<VisitProcess?> GetVisit(Guid id, CancellationToken ct);
    Task<InterviewDraft?> GetDraft(Guid visitId, CancellationToken ct);
    Task<PatientAccessGrant?> FindAccessByLinkHash(string hash, CancellationToken ct);
    Task<PatientAccessGrant?> FindAccessByCodeHash(string hash, CancellationToken ct);
    Task<IReadOnlyList<PatientAccessGrant>> GetAccessGrants(Guid visitId, CancellationToken ct);
    Task<IReadOnlyList<VisitProcess>> GetFacilityVisits(Guid facilityId, CancellationToken ct);
    Task<IReadOnlyList<VisitProcess>> GetPatientVisits(Guid patientIdentityId, CancellationToken ct);
    Task<IReadOnlyList<ReportVersion>> GetVersions(Guid visitId, CancellationToken ct);
    Task<ReportVersion?> GetVersion(Guid visitId, Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<ReportEvidence>> GetReportEvidence(Guid versionId, CancellationToken ct);
    Task<SharingConsent?> GetActiveConsent(Guid visitId, CancellationToken ct);
    Task<IReadOnlyList<ObservationProposal>> GetObservations(Guid draftId, CancellationToken ct);
    Task<SupplementationRound?> GetOpenRound(Guid visitId, CancellationToken ct);
    Task<int> CountRounds(Guid visitId, CancellationToken ct);
    Task<DeliveryAttempt?> GetLatestDelivery(Guid visitId, CancellationToken ct);
    Task<DeletionRequest?> GetDeletionRequest(Guid id, CancellationToken ct);
    Task DeletePatientData(Guid patientIdentityId, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task Save(CancellationToken ct);
}

public interface IPatientDataProtector
{
    string CorrelationKey(string pesel);
    string Protect(string value);
    string Unprotect(string value);
}
public interface ICredentialService
{
    string GenerateLinkToken(); string GenerateVisitCode(); string Hash(string value);
}
public interface IPatientSessionStore
{
    Task<string> Create(Guid visitId, DateTimeOffset expiresAt, CancellationToken ct);
    Task<Guid?> Resolve(string token, CancellationToken ct);
    Task RevokeForVisit(Guid visitId, CancellationToken ct);
}
public interface INotificationSender
{
    Task<NotificationResult> Send(string channel, string destination, string linkToken, string visitCode, CancellationToken ct);
    Task<NotificationResult> SendSupplementation(string channel, string destination, CancellationToken ct);
}
public sealed record NotificationResult(bool Delivered, string? ProviderId, string? Error);
public interface IInterviewQuestionProvider { Task<string?> Next(InterviewDraft draft, CancellationToken ct); }
public interface IReportRenderer { byte[] Render(ReportSnapshot snapshot); }
public interface ITranscriptionService { Task<string> Transcribe(Stream audio, string contentType, CancellationToken ct); }
public interface IClock { DateTimeOffset UtcNow { get; } }
