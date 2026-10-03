using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Supplementation;

namespace DocPrep.Application.Reports;

public sealed class FacilityReportService(IDocPrepStore store, IClock clock, INotificationSender notifications,
    IPatientDataProtector protector, ICredentialService credentials)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ReportVersionView>> Versions(Guid facilityId, Guid visitId, string? clinicianId, CancellationToken ct)
    {
        var visit = await AuthorizedVisit(facilityId, visitId, clinicianId, ct);
        if (!visit.CanFacilityReadSharedReport(await store.GetActiveConsent(visitId, ct) is not null)) throw new ForbiddenError("Patient consent is required.");
        return (await store.GetVersions(visitId, ct)).Where(x => x.PdfStatus == Domain.Reports.PdfGenerationStatus.Ready).Select(x => new ReportVersionView(x.Id, x.VersionNumber, x.ApprovedAt, x.ConfirmedIncomplete)).ToList();
    }

    public async Task<ClinicianReportView> Get(Guid facilityId, Guid visitId, Guid versionId, string? clinicianId, CancellationToken ct)
    {
        var visit = await AuthorizedVisit(facilityId, visitId, clinicianId, ct);
        if (!visit.CanFacilityReadSharedReport(await store.GetActiveConsent(visitId, ct) is not null)) throw new ForbiddenError("Patient consent is required.");
        var version = await store.GetVersion(visitId, versionId, ct) ?? throw new NotFoundError();
        if (version.PdfStatus != Domain.Reports.PdfGenerationStatus.Ready) throw new NotFoundError();
        var snapshot = JsonSerializer.Deserialize<ReportSnapshot>(version.SnapshotJson, JsonOptions) ?? throw new ConflictError("report.invalid_snapshot", "Stored report snapshot is invalid.");
        var availableEvidence = new List<ObservationEvidenceView>();
        foreach (var evidence in await store.GetReportEvidence(versionId, ct))
        {
            var sourceVisit = await store.GetVisit(evidence.SourceVisitId, ct); if (sourceVisit?.FacilityId != facilityId) continue;
            if (await store.GetActiveConsent(sourceVisit.Id, ct) is null || !sourceVisit.CanFacilityReadSharedReport(true)) continue;
            availableEvidence.Add(new(evidence.ObservationId, evidence.SourceVisitId, evidence.SourceReportVersionId, evidence.SourceDate, evidence.SourceFragment));
        }
        store.Add(new AuditEvent(facilityId, visitId, "clinician-api", $"report.read:{versionId}", clock.UtcNow)); await store.Save(ct);
        return new(snapshot, availableEvidence);
    }

    public async Task<byte[]> Pdf(Guid facilityId, Guid visitId, Guid versionId, string? clinicianId, CancellationToken ct)
    { await Get(facilityId, visitId, versionId, clinicianId, ct); return (await store.GetVersion(visitId, versionId, ct))!.PdfData; }

    public async Task AddQuestions(Guid facilityId, Guid visitId, AddClinicianQuestionsCommand command, CancellationToken ct)
    {
        var visit = await AuthorizedVisit(facilityId, visitId, command.ClinicianId, ct);
        if (!visit.CanFacilityReadSharedReport(await store.GetActiveConsent(visitId, ct) is not null)) throw new ForbiddenError("A shared report is required.");
        if (visit.AssignedClinicianId is not null && !visit.AssignedClinicianId.Equals(command.ClinicianId, StringComparison.Ordinal)) throw new ForbiddenError();
        var round = await store.GetOpenRound(visitId, ct);
        string? invitationToken = null;
        if (round is null)
        {
            round = new SupplementationRound(visitId, await store.CountRounds(visitId, ct) + 1, command.ClinicianId, clock.UtcNow);
            store.Add(round);
            var interview = new DocPrep.Domain.Interviews.AgentInterview(visitId, "supplementation", clock.UtcNow,
                await store.CountAgentInterviews(visitId, ct) + 1);
            store.Add(interview);
            invitationToken = credentials.GenerateLinkToken();
            store.Add(new DocPrep.Domain.Interviews.InterviewInvitation(interview.Id, credentials.Hash(invitationToken),
                visit.ServiceExpiresAt, 3, clock.UtcNow, protector.Protect(invitationToken)));
        }
        foreach (var question in command.Questions) round.AddQuestion(question, command.ClinicianId, clock.UtcNow);
        visit.RequireSupplementation(clock.UtcNow);
        var delivery = await store.GetLatestDelivery(visitId, ct);
        await store.Save(ct);
        if (delivery is not null && invitationToken is not null) await notifications.SendSupplementation(delivery.Channel,
            protector.Unprotect(delivery.EncryptedDestination), invitationToken, ct);
        store.Add(new AuditEvent(facilityId, visitId, command.ClinicianId, "supplementation.questions_added", clock.UtcNow)); await store.Save(ct);
    }

    private async Task<Domain.Visits.VisitProcess> AuthorizedVisit(Guid facilityId, Guid visitId, string? clinicianId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError();
        if (visit.FacilityId != facilityId) throw new NotFoundError();
        if (string.IsNullOrWhiteSpace(visit.AssignedClinicianId) || !visit.AssignedClinicianId.Equals(clinicianId, StringComparison.Ordinal)) throw new ForbiddenError();
        return visit;
    }
}
