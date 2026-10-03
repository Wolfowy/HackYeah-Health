using System.Text.Json;
using System.Text.Json.Serialization;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;

namespace DocPrep.Application.Interviews;

public sealed class InterviewExtractionService(IDocPrepStore store, IClock clock)
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ExtractionOutcome Normalize(JsonElement providerCollection)
    {
        var issues = new List<ExtractionIssue>();
        try
        {
            var payload = Unwrap(providerCollection);
            if (payload.ValueKind != JsonValueKind.Object)
                return Failed("data", "invalid_format", "Dane wywiadu nie są obiektem JSON.");

            var input = payload.Deserialize<Input>(JsonOptions) ?? new Input();
            if (string.IsNullOrWhiteSpace(input.ConsultationReason) && TryProviderString(payload, "reason", out var reason))
                input.ConsultationReason = reason;

            var symptoms = (input.Symptoms ?? []).Where(x => Required(x.Name, "symptoms.name", issues)).Select((x, index) =>
            {
                if (x.Severity is < 0 or > 10)
                {
                    issues.Add(new($"symptoms.{index}.severity", "invalid", "Nasilenie musi mieścić się w zakresie 0–10."));
                    x.Severity = null;
                }
                var state = x.StartedOnState ?? (x.StartedOn is not null || !string.IsNullOrWhiteSpace(x.StartedOnText) ? FieldState.Provided : FieldState.NotAsked);
                return new NormalizedSymptom(Cut(x.Name!, 200)!, x.StartedOn, state, Cut(x.StartedOnText, 200),
                    Cut(x.Frequency, 500), x.Severity, Cut(x.Course, 1000), Cut(x.DailyImpact, 2000),
                    Cut(x.Description, 4000), (x.Timeline ?? []).Where(t => Required(t.Description, "timeline.description", issues))
                        .Select(t => new TimelineData(t.OccurredOn, Cut(t.Period, 200), Cut(t.Description!, 2000)!)).ToList());
            }).ToList();

            var medications = (input.Medications ?? []).Where(x => Required(x.Name, "medications.name", issues))
                .Select(x => new NormalizedMedication(Cut(x.Name!, 200)!, Cut(x.Dose, 200),
                    x.DoseState ?? (!string.IsNullOrWhiteSpace(x.Dose) ? FieldState.Provided : FieldState.NotAsked),
                    Cut(x.Schedule, 500), Cut(x.Reason, 1000))).ToList();
            var allergies = (input.Allergies ?? []).Where(x => Required(x.Substance, "allergies.substance", issues))
                .Select(x => new NormalizedAllergy(Cut(x.Substance!, 200)!, Cut(x.Reaction, 1000))).ToList();
            var conditions = (input.ChronicConditions ?? []).Where(x => Required(x.Name, "chronicConditions.name", issues))
                .Select(x => new NormalizedCondition(Cut(x.Name!, 200)!, Cut(x.Description, 2000))).ToList();
            var questions = (input.PatientQuestions ?? []).Where(x => Required(x, "patientQuestions", issues)).Select(x => Cut(x!, 1000)!).ToList();

            if (string.IsNullOrWhiteSpace(input.ConsultationReason))
                issues.Add(new("consultationReason", "missing", "Brakuje powodu konsultacji."));
            foreach (var item in symptoms.Select((value, index) => (value, index)))
                AddStateIssue(issues, $"symptoms.{item.index}.startedOn", item.value.StartedOnState, "początek objawu");
            foreach (var item in medications.Select((value, index) => (value, index)))
                AddStateIssue(issues, $"medications.{item.index}.dose", item.value.DoseState, "dawkę leku");
            AddStateIssue(issues, "medications", input.MedicationsState ?? FieldState.NotAsked, "listę leków");
            AddStateIssue(issues, "allergies", input.AllergiesState ?? FieldState.NotAsked, "informacje o alergiach");
            AddStateIssue(issues, "chronicConditions", input.ChronicConditionsState ?? FieldState.NotAsked, "informacje o chorobach przewlekłych");
            var data = new NormalizedInterviewData(CurrentSchemaVersion, Cut(input.ConsultationReason, 2000), symptoms,
                medications, allergies, conditions, questions, Cut(input.AdditionalNotes, 8000),
                input.MedicationsState ?? FieldState.NotAsked, input.AllergiesState ?? FieldState.NotAsked,
                input.ChronicConditionsState ?? FieldState.NotAsked, issues);
            var status = issues.Count == 0 ? ExtractionStatus.Ready : ExtractionStatus.Partial;
            return new(status, data, issues);
        }
        catch (JsonException)
        {
            return Failed("data", "invalid_json", "Pole interview_json nie zawiera poprawnego JSON.");
        }
    }

    public ExtractionOutcome Merge(IEnumerable<ExtractionOutcome> outcomes)
    {
        var valid = outcomes.Where(x => x.Data is not null).ToList();
        if (valid.Count == 0) return Failed("data", "missing", "ElevenLabs nie zwrócił danych strukturalnych.");
        var issues = valid.SelectMany(x => x.Issues).ToList();
        var result = valid[0].Data!;
        foreach (var next in valid.Skip(1).Select(x => x.Data!))
        {
            result = result with
            {
                ConsultationReason = next.ConsultationReason ?? result.ConsultationReason,
                Symptoms = MergeBy(result.Symptoms, next.Symptoms, x => x.Name),
                Medications = MergeBy(result.Medications, next.Medications, x => x.Name),
                Allergies = MergeBy(result.Allergies, next.Allergies, x => x.Substance),
                ChronicConditions = MergeBy(result.ChronicConditions, next.ChronicConditions, x => x.Name),
                PatientQuestions = result.PatientQuestions.Concat(next.PatientQuestions).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AdditionalNotes = next.AdditionalNotes ?? result.AdditionalNotes,
                MedicationsState = Later(result.MedicationsState, next.MedicationsState),
                AllergiesState = Later(result.AllergiesState, next.AllergiesState),
                ChronicConditionsState = Later(result.ChronicConditionsState, next.ChronicConditionsState),
                Issues = issues
            };
        }
        return new(issues.Count == 0 ? ExtractionStatus.Ready : ExtractionStatus.Partial, result, issues);
    }

    public async Task<bool> Import(Guid interviewId, Guid sourceSessionId, NormalizedInterviewData data, CancellationToken ct)
    {
        var interview = await store.GetAgentInterview(interviewId, ct);
        if (interview is null) return false;
        var draft = await store.GetDraft(interview.VisitProcessId, ct);
        if (draft is null) return false;
        if (interview.ImportedDraftRevision is not null && draft.Revision != interview.ImportedDraftRevision)
            return false; // późny webhook nie może skasować ręcznej poprawki pacjenta
        if (interview.InterviewType.Equals("supplementation", StringComparison.OrdinalIgnoreCase))
            data = MergeWithDraft(draft, data);

        draft.Replace(data.ConsultationReason ?? "Nie podano", data.Symptoms.Select(x => new SymptomData(x.Name,
                x.StartedOn, x.StartedOnState, x.Frequency, x.Severity, x.DailyImpact,
                Join(x.Course, x.Description, x.StartedOnText), x.Timeline)).ToList(),
            data.Medications.Select(x => new MedicationData(x.Name, x.Dose, x.DoseState, x.Schedule, x.Reason)).ToList(),
            data.Allergies.Select(x => new AllergyData(x.Substance, x.Reaction)).ToList(),
            data.ChronicConditions.Select(x => new ConditionData(x.Name, x.Description)).ToList(),
            data.PatientQuestions, data.AdditionalNotes, data.MedicationsState, data.AllergiesState,
            data.ChronicConditionsState, clock.UtcNow);
        var visit = await store.GetVisit(interview.VisitProcessId, ct);
        if (visit is null) return false;
        visit.ReadyForApproval(clock.UtcNow);
        await RebuildObservations(visit, draft, ct);
        interview.ImportSucceeded(draft.Revision);
        return true;
    }

    private async Task RebuildObservations(Domain.Visits.VisitProcess visit, InterviewDraft draft, CancellationToken ct)
    {
        foreach (var old in await store.GetObservations(draft.Id, ct)) store.Remove(old);
        var history = new List<(Domain.Visits.VisitProcess Visit, Domain.Reports.ReportVersion Version, ReportSnapshot Snapshot)>();
        foreach (var previousVisit in (await store.GetPatientVisits(visit.PatientIdentityId, ct)).Where(x => x.Id != visit.Id))
            foreach (var version in (await store.GetVersions(previousVisit.Id, ct)).OrderByDescending(x => x.VersionNumber).Take(1))
            {
                var snapshot = JsonSerializer.Deserialize<ReportSnapshot>(version.SnapshotJson, JsonOptions);
                if (snapshot is not null) history.Add((previousVisit, version, snapshot));
            }
        foreach (var symptom in draft.Symptoms)
        {
            var matches = history.SelectMany(x => x.Snapshot.Symptoms.Where(s => s.Name.Equals(symptom.Name, StringComparison.OrdinalIgnoreCase))
                .Select(s => (x.Visit, x.Version, Symptom: s))).ToList();
            var kind = matches.Count == 0 ? ObservationKind.New : matches.Any(x => x.Symptom.Severity != symptom.Severity)
                ? ObservationKind.Changed : matches.Count > 1 ? ObservationKind.Pattern : ObservationKind.Recurring;
            var text = kind switch
            {
                ObservationKind.New => $"Objaw „{symptom.Name}” został zgłoszony po raz pierwszy.",
                ObservationKind.Changed => $"Nasilenie objawu „{symptom.Name}” różni się od poprzedniego wywiadu.",
                ObservationKind.Pattern => $"Objaw „{symptom.Name}” pojawiał się w wielu wywiadach.",
                _ => $"Objaw „{symptom.Name}” został zgłoszony ponownie."
            };
            var observation = new ObservationProposal(draft.Id, symptom.Name, kind, text, clock.UtcNow);
            foreach (var match in matches) observation.Evidence.Add(new(observation.Id, match.Visit.Id, match.Version.Id,
                match.Visit.ScheduledAt, $"{match.Symptom.Name}; nasilenie {match.Symptom.Severity?.ToString() ?? "nieznane"}"));
            store.Add(observation);
        }
    }

    private static JsonElement Unwrap(JsonElement collection)
    {
        if (!collection.TryGetProperty("interview_json", out var value)) return collection;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var nested)) value = nested;
        if (value.ValueKind == JsonValueKind.String)
        {
            using var document = JsonDocument.Parse(value.GetString() ?? "");
            return document.RootElement.Clone();
        }
        return value;
    }

    private static bool TryProviderString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Object && property.TryGetProperty("value", out var nested)) property = nested;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString(); return !string.IsNullOrWhiteSpace(value);
    }
    private static IReadOnlyList<T> MergeBy<T>(IEnumerable<T> first, IEnumerable<T> second, Func<T, string> key) =>
        first.Concat(second).GroupBy(x => key(x).Trim(), StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();
    private static NormalizedInterviewData MergeWithDraft(InterviewDraft draft, NormalizedInterviewData data) => data with
    {
        ConsultationReason = data.ConsultationReason ?? draft.ConsultationReason,
        Symptoms = MergeBy(draft.Symptoms.Select(x => new NormalizedSymptom(x.Name, x.StartedOn, x.StartedOnState,
            null, x.Frequency, x.Severity, null, x.DailyImpact, x.Description,
            x.Timeline.Select(t => new TimelineData(t.OccurredOn, t.Period, t.Description)).ToList())), data.Symptoms, x => x.Name),
        Medications = MergeBy(draft.Medications.Select(x => new NormalizedMedication(x.Name, x.Dose, x.DoseState,
            x.Schedule, x.Reason)), data.Medications, x => x.Name),
        Allergies = MergeBy(draft.Allergies.Select(x => new NormalizedAllergy(x.Substance, x.Reaction)), data.Allergies, x => x.Substance),
        ChronicConditions = MergeBy(draft.ChronicConditions.Select(x => new NormalizedCondition(x.Name, x.Description)), data.ChronicConditions, x => x.Name),
        PatientQuestions = draft.PatientQuestions.Select(x => x.Text).Concat(data.PatientQuestions).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        AdditionalNotes = data.AdditionalNotes ?? draft.AdditionalNotes
    };
    private static FieldState Later(FieldState first, FieldState second) => second == FieldState.NotAsked ? first : second;
    private static bool Required(string? value, string path, ICollection<ExtractionIssue> issues)
    { if (!string.IsNullOrWhiteSpace(value)) return true; issues.Add(new(path, "missing", "Brakuje wymaganej wartości.")); return false; }
    private static string? Cut(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Join(params string?[] values) { var text = string.Join(" ", values.Where(x => !string.IsNullOrWhiteSpace(x))); return string.IsNullOrWhiteSpace(text) ? null : text; }
    private static ExtractionOutcome Failed(string path, string kind, string message)
    { var issue = new ExtractionIssue(path, kind, message); return new(ExtractionStatus.Failed, null, [issue]); }
    private static void AddStateIssue(ICollection<ExtractionIssue> issues, string path, FieldState state, string label)
    {
        if (state == FieldState.Provided) return;
        var kind = state switch { FieldState.Unknown => "unknown", FieldState.Contradictory => "contradiction", _ => "not_asked" };
        issues.Add(new(path, kind, $"Należy sprawdzić {label}."));
    }

    private sealed class Input
    {
        public int? SchemaVersion { get; set; }
        public string? ConsultationReason { get; set; }
        public List<SymptomInput>? Symptoms { get; set; }
        public List<MedicationInput>? Medications { get; set; }
        public List<AllergyInput>? Allergies { get; set; }
        public List<ConditionInput>? ChronicConditions { get; set; }
        public List<string>? PatientQuestions { get; set; }
        public string? AdditionalNotes { get; set; }
        public FieldState? MedicationsState { get; set; }
        public FieldState? AllergiesState { get; set; }
        public FieldState? ChronicConditionsState { get; set; }
    }
    private sealed class SymptomInput
    {
        public string? Name { get; set; } public DateOnly? StartedOn { get; set; } public FieldState? StartedOnState { get; set; }
        public string? StartedOnText { get; set; } public string? Frequency { get; set; } public int? Severity { get; set; }
        public string? Course { get; set; } public string? DailyImpact { get; set; } public string? Description { get; set; }
        public List<TimelineData>? Timeline { get; set; }
    }
    private sealed class MedicationInput { public string? Name { get; set; } public string? Dose { get; set; } public FieldState? DoseState { get; set; } public string? Schedule { get; set; } public string? Reason { get; set; } }
    private sealed class AllergyInput { public string? Substance { get; set; } public string? Reaction { get; set; } }
    private sealed class ConditionInput { public string? Name { get; set; } public string? Description { get; set; } }
}

public sealed record ExtractionOutcome(ExtractionStatus Status, NormalizedInterviewData? Data, IReadOnlyList<ExtractionIssue> Issues);
