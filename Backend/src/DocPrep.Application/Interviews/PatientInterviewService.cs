using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Sharing;
using DocPrep.Domain.Supplementation;
using DocPrep.Domain.Privacy;

namespace DocPrep.Application.Interviews;

public sealed class PatientInterviewService(IDocPrepStore store, IInterviewQuestionProvider questions, IReportRenderer renderer, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PatientInterviewView> Get(Guid visitId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError(); var draft = await Draft(visitId, ct);
        var observations = await store.GetObservations(draft.Id, ct); var versions = await store.GetVersions(visitId, ct);
        var consent = await store.GetActiveConsent(visitId, ct); var round = await store.GetOpenRound(visitId, ct);
        return new(visit.Id, visit.ScheduledAt, visit.ServiceExpiresAt, visit.Status, MapDraft(draft), observations.Select(MapPatientObservation).ToList(),
            round is null ? null : MapRound(round), versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault()?.VersionNumber, consent is not null);
    }

    public async Task<string?> SubmitAnswer(Guid visitId, SubmitAnswerCommand command, CancellationToken ct)
    {
        var visit = await ActiveVisit(visitId, ct); var draft = await Draft(visitId, ct);
        draft.AddAnswer(command.Question, command.Answer, command.Mode, clock.UtcNow); visit.MarkDraftChanged(clock.UtcNow); await store.Save(ct);
        return await questions.Next(draft, ct);
    }

    public async Task<PatientInterviewView> ReplaceDraft(Guid visitId, ReplaceDraftCommand command, CancellationToken ct)
    {
        var visit = await ActiveVisit(visitId, ct); var draft = await Draft(visitId, ct);
        draft.Replace(command.ConsultationReason, command.Symptoms, command.Medications, command.Allergies, command.ChronicConditions, command.Questions, clock.UtcNow);
        visit.MarkDraftChanged(clock.UtcNow); await RebuildObservations(visit, draft, ct); await store.Save(ct); return await Get(visitId, ct);
    }

    public async Task<PatientInterviewView> DecideObservation(Guid visitId, Guid observationId, ObservationDecisionCommand command, CancellationToken ct)
    {
        await ActiveVisit(visitId, ct); var draft = await Draft(visitId, ct);
        var observation = (await store.GetObservations(draft.Id, ct)).SingleOrDefault(x => x.Id == observationId) ?? throw new NotFoundError();
        observation.Decide(command.Decision, command.EditedText, clock.UtcNow); await store.Save(ct); return await Get(visitId, ct);
    }

    public async Task Complete(Guid visitId, CancellationToken ct)
    { var visit = await ActiveVisit(visitId, ct); visit.ReadyForApproval(clock.UtcNow); await store.Save(ct); }

    public async Task<ReportVersionView> Approve(Guid visitId, ApproveReportCommand command, CancellationToken ct)
    {
        var visit = await ActiveVisit(visitId, ct); var draft = await Draft(visitId, ct);
        if (draft.Clarifications.Count > 0 && !command.ConfirmIncompleteReport)
            throw new ConflictError("incomplete_report.confirmation_required", "Explicit confirmation is required for an incomplete report.");
        var observations = await store.GetObservations(draft.Id, ct);
        if (observations.Any(x => x.Decision == ObservationDecision.Pending))
            throw new ConflictError("observation.decision_required", "Every observation requires a patient decision.");
        var versions = await store.GetVersions(visitId, ct); var versionId = Guid.NewGuid(); var now = clock.UtcNow;
        var round = await store.GetOpenRound(visitId, ct);
        if (round?.Status == SupplementationRoundStatus.Open && round.Questions.Any(x => x.Answer is null))
            throw new ConflictError("supplementation.answers_missing", "All clinician questions require an answer.");
        var snapshot = BuildSnapshot(versionId, versions.Count + 1, visit, draft, observations, round, command.ConfirmIncompleteReport, now);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var version = new ReportVersion(versionId, visitId, snapshot.VersionNumber, snapshot.SchemaVersion, json, Hash(Encoding.UTF8.GetBytes(json)), command.ConfirmIncompleteReport, draft.Revision, now);
        store.Add(version);
        foreach (var observation in observations.Where(x => x.Decision is ObservationDecision.Accepted or ObservationDecision.EditedAndAccepted))
            foreach (var evidence in observation.Evidence)
                store.Add(new ReportEvidence(versionId, observation.Id, evidence.SourceVisitId, evidence.SourceReportVersionId, evidence.SourceDate, evidence.SourceFragment));
        visit.RegisterApprovedVersion(versionId, now); if (round is not null) round.Close(now);
        store.Add(new AuditEvent(visit.FacilityId, visitId, "patient-session", $"report.approved:{versionId}", now)); await store.Save(ct);
        try
        {
            var pdf = renderer.Render(snapshot); version.CompletePdf(pdf, Hash(pdf));
            visit.PublishApprovedVersion(await store.GetActiveConsent(visitId, ct) is not null, now); await store.Save(ct);
        }
        catch { version.FailPdf(); await store.Save(ct); throw new ConflictError("report.generation_failed", "The content was approved, but PDF generation failed and can be retried without reapproval."); }
        return new(version.Id, version.VersionNumber, version.ApprovedAt, version.ConfirmedIncomplete);
    }

    public async Task SetConsent(Guid visitId, ConsentCommand command, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError(); var existing = await store.GetActiveConsent(visitId, ct);
        if (command.Granted)
        {
            if (visit.LatestApprovedVersionId is null || (await store.GetVersion(visitId, visit.LatestApprovedVersionId.Value, ct))?.PdfStatus != PdfGenerationStatus.Ready)
                throw new ConflictError("report.not_ready", "Both JSON and PDF must be ready before sharing.");
            if (existing is null) store.Add(new SharingConsent(visitId, visit.FacilityId, "patient-session", clock.UtcNow));
            visit.GrantConsent(clock.UtcNow);
        }
        else { existing?.Revoke(clock.UtcNow); visit.RevokeConsent(clock.UtcNow); }
        store.Add(new AuditEvent(visit.FacilityId, visitId, "patient-session", command.Granted ? "consent.granted" : "consent.revoked", clock.UtcNow));
        await store.Save(ct);
    }

    public async Task<(string Json, byte[] Pdf, int Version)> GetLatestReport(Guid visitId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError();
        if (visit.LatestApprovedVersionId is null) throw new NotFoundError("No approved report exists.");
        var version = await store.GetVersion(visitId, visit.LatestApprovedVersionId.Value, ct) ?? throw new NotFoundError();
        if (version.PdfStatus != PdfGenerationStatus.Ready) throw new ConflictError("report.generation_failed", "PDF generation must be retried.");
        return (version.SnapshotJson, version.PdfData, version.VersionNumber);
    }

    public async Task RegeneratePdf(Guid visitId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError();
        if (visit.LatestApprovedVersionId is null) throw new NotFoundError("No approved report exists.");
        var version = await store.GetVersion(visitId, visit.LatestApprovedVersionId.Value, ct) ?? throw new NotFoundError();
        var snapshot = JsonSerializer.Deserialize<ReportSnapshot>(version.SnapshotJson, JsonOptions) ?? throw new ConflictError("report.invalid_snapshot", "Stored snapshot is invalid.");
        try { var pdf = renderer.Render(snapshot); version.CompletePdf(pdf, Hash(pdf)); visit.PublishApprovedVersion(await store.GetActiveConsent(visitId, ct) is not null, clock.UtcNow); await store.Save(ct); }
        catch { version.FailPdf(); await store.Save(ct); throw new ConflictError("report.generation_failed", "PDF generation failed again."); }
    }

    public async Task AnswerSupplementation(Guid visitId, IReadOnlyList<SupplementationAnswerCommand> answers, CancellationToken ct)
    {
        await ActiveVisit(visitId, ct); var round = await store.GetOpenRound(visitId, ct) ?? throw new NotFoundError("No open supplementation round.");
        foreach (var answer in answers)
        {
            var question = round.Questions.SingleOrDefault(x => x.Id == answer.QuestionId) ?? throw new NotFoundError("Supplementation question not found.");
            question.AnswerQuestion(answer.Answer, answer.Mode, clock.UtcNow);
        }
        round.MarkAnswersReady(); var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError(); visit.ReadyForApproval(clock.UtcNow); await store.Save(ct);
    }

    private async Task RebuildObservations(Domain.Visits.VisitProcess visit, InterviewDraft draft, CancellationToken ct)
    {
        foreach (var old in await store.GetObservations(draft.Id, ct)) store.Remove(old);
        var history = new List<(Domain.Visits.VisitProcess Visit, ReportVersion Version, ReportSnapshot Snapshot)>();
        foreach (var previousVisit in (await store.GetPatientVisits(visit.PatientIdentityId, ct)).Where(x => x.Id != visit.Id))
            foreach (var version in (await store.GetVersions(previousVisit.Id, ct)).OrderByDescending(x => x.VersionNumber).Take(1))
            { var snapshot = JsonSerializer.Deserialize<ReportSnapshot>(version.SnapshotJson, JsonOptions); if (snapshot is not null) history.Add((previousVisit, version, snapshot)); }

        foreach (var symptom in draft.Symptoms)
        {
            var matches = history.SelectMany(x => x.Snapshot.Symptoms.Where(s => s.Name.Equals(symptom.Name, StringComparison.OrdinalIgnoreCase)).Select(s => (x.Visit, x.Version, Symptom: s))).ToList();
            var kind = matches.Count == 0 ? ObservationKind.New : matches.Any(x => x.Symptom.Severity != symptom.Severity) ? ObservationKind.Changed : matches.Count > 1 ? ObservationKind.Pattern : ObservationKind.Recurring;
            var text = kind switch { ObservationKind.New => $"{symptom.Name} is newly reported.", ObservationKind.Changed => $"The reported severity of {symptom.Name} differs from an earlier interview.", ObservationKind.Pattern => $"{symptom.Name} has been reported in multiple interviews.", _ => $"{symptom.Name} has been reported again." };
            var observation = new ObservationProposal(draft.Id, symptom.Name, kind, text, clock.UtcNow);
            foreach (var match in matches) observation.Evidence.Add(new(observation.Id, match.Visit.Id, match.Version.Id, match.Visit.ScheduledAt, $"{match.Symptom.Name}; severity {match.Symptom.Severity?.ToString() ?? "unknown"}"));
            store.Add(observation);
        }
        var currentNames = draft.Symptoms.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in history.SelectMany(x => x.Snapshot.Symptoms.Select(s => (x.Visit, x.Version, Symptom: s))).Where(x => !currentNames.Contains(x.Symptom.Name)).GroupBy(x => x.Symptom.Name, StringComparer.OrdinalIgnoreCase))
        {
            var observation = new ObservationProposal(draft.Id, previous.Key, ObservationKind.InsufficientData, $"{previous.Key} was not mentioned in this interview; this does not mean it has resolved.", clock.UtcNow);
            foreach (var match in previous) observation.Evidence.Add(new(observation.Id, match.Visit.Id, match.Version.Id, match.Visit.ScheduledAt, match.Symptom.Name));
            store.Add(observation);
        }
    }

    private static ReportSnapshot BuildSnapshot(Guid id, int number, Domain.Visits.VisitProcess visit, InterviewDraft draft, IReadOnlyList<ObservationProposal> observations, SupplementationRound? round, bool incomplete, DateTimeOffset now) =>
        new(id, number, 1, visit.Id, visit.ExternalVisitId, visit.FacilityId, visit.ScheduledAt, now, draft.ConsultationReason ?? "",
            draft.Symptoms.Select(x => new ReportSymptom(x.Name, x.StartedOn, x.Frequency, x.Severity, x.DailyImpact, x.Description, x.Timeline.Select(t => new TimelineData(t.OccurredOn, t.Period, t.Description)).ToList(), x.Source.ToString())).ToList(),
            draft.Medications.Select(x => new ReportMedication(x.Name, x.Dose, x.Schedule, x.Source.ToString())).ToList(),
            draft.Allergies.Select(x => new ReportAllergy(x.Substance, x.Reaction, x.Source.ToString())).ToList(),
            draft.ChronicConditions.Select(x => new ReportCondition(x.Name, x.Description, x.Source.ToString())).ToList(), draft.PatientQuestions.Select(x => x.Text).ToList(),
            draft.Clarifications.Select(x => new ReportClarification(x.FieldPath, x.Kind.ToString(), x.Message)).ToList(),
            observations.Where(x => x.Decision is ObservationDecision.Accepted or ObservationDecision.EditedAndAccepted).Select(x => new ReportObservation(x.Id, x.SymptomName, x.Kind.ToString(), x.ReportText, x.Decision == ObservationDecision.EditedAndAccepted, "AiObservation")).ToList(),
            round?.Questions.Where(x => x.Answer is not null).Select(x => new ReportSupplementation(x.Text, x.Answer!.Text, x.Answer.Mode.ToString(), "Clinician")).ToList() ?? [], incomplete);

    private async Task<Domain.Visits.VisitProcess> ActiveVisit(Guid id, CancellationToken ct)
    { var visit = await store.GetVisit(id, ct) ?? throw new NotFoundError(); visit.Open(clock.UtcNow); return visit; }
    private async Task<InterviewDraft> Draft(Guid visitId, CancellationToken ct) => await store.GetDraft(visitId, ct) ?? throw new NotFoundError();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static PatientObservationView MapPatientObservation(ObservationProposal x) => new(x.Id, x.SymptomName, x.Kind, x.PatientEditedText ?? x.OriginalText, x.Decision, x.Decision == ObservationDecision.EditedAndAccepted);
    private static DraftView MapDraft(InterviewDraft x) => new(x.ConsultationReason, x.Revision,
        x.Symptoms.Select(s => new SymptomData(s.Name, s.StartedOn, s.StartedOnState, s.Frequency, s.Severity, s.DailyImpact, s.Description, s.Timeline.Select(t => new TimelineData(t.OccurredOn, t.Period, t.Description)).ToList())).ToList(),
        x.Medications.Select(m => new MedicationData(m.Name, m.Dose, m.DoseState, m.Schedule)).ToList(), x.Allergies.Select(a => new AllergyData(a.Substance, a.Reaction)).ToList(),
        x.ChronicConditions.Select(c => new ConditionData(c.Name, c.Description)).ToList(), x.PatientQuestions.Select(q => q.Text).ToList(),
        x.Clarifications.Select(c => new ClarificationView(c.Id, c.FieldPath, c.Kind.ToString(), c.Message)).ToList());
    private static SupplementationRoundView MapRound(SupplementationRound x) => new(x.Id, x.Number, x.Status.ToString(), x.Questions.Select(q => new SupplementationQuestionView(q.Id, q.Text, q.Answer?.Text, q.Answer?.Mode)).ToList());
}
