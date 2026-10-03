using DocPrep.Domain.Common;

namespace DocPrep.Domain.Interviews;

public enum InformationSource { Patient, Clinician, AiObservation }
public enum AnswerMode { Text, Voice }
public enum FieldState { Provided, Unknown, NotAsked, Contradictory }
public enum ClarificationKind { Missing, Unknown, Contradiction }

public sealed class InterviewDraft
{
    private InterviewDraft() { }
    public InterviewDraft(Guid visitId, DateTimeOffset now) { Id = Guid.NewGuid(); VisitProcessId = visitId; UpdatedAt = now; }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public string? ConsultationReason { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<InterviewAnswer> Answers { get; } = [];
    public List<Symptom> Symptoms { get; } = [];
    public List<Medication> Medications { get; } = [];
    public List<Allergy> Allergies { get; } = [];
    public List<ChronicCondition> ChronicConditions { get; } = [];
    public List<PatientQuestion> PatientQuestions { get; } = [];
    public List<Clarification> Clarifications { get; } = [];

    public void AddAnswer(string question, string answer, AnswerMode mode, DateTimeOffset now)
    {
        Answers.Add(new(Id, Guard.Required(question, nameof(question), 1000), Guard.Required(answer, nameof(answer), 8000), mode, now)); Touch(now);
    }

    public void Replace(string consultationReason, IEnumerable<SymptomData> symptoms, IEnumerable<MedicationData> medications,
        IEnumerable<AllergyData> allergies, IEnumerable<ConditionData> conditions, IEnumerable<string> questions, DateTimeOffset now)
    {
        ConsultationReason = Guard.Required(consultationReason, nameof(consultationReason), 2000);
        Symptoms.Clear(); Medications.Clear(); Allergies.Clear(); ChronicConditions.Clear(); PatientQuestions.Clear(); Clarifications.Clear();
        Symptoms.AddRange(symptoms.Select(x => new Symptom(Id, x)));
        Medications.AddRange(medications.Select(x => new Medication(Id, x)));
        Allergies.AddRange(allergies.Select(x => new Allergy(Id, x)));
        ChronicConditions.AddRange(conditions.Select(x => new ChronicCondition(Id, x)));
        PatientQuestions.AddRange(questions.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new PatientQuestion(Id, x)));
        foreach (var medication in Medications.Where(x => x.DoseState != FieldState.Provided))
            Clarifications.Add(new(Id, $"medications.{medication.Id}.dose", medication.DoseState == FieldState.Contradictory ? ClarificationKind.Contradiction : ClarificationKind.Missing, $"Confirm the dose of {medication.Name}."));
        foreach (var symptom in Symptoms.Where(x => x.StartedOnState != FieldState.Provided))
            Clarifications.Add(new(Id, $"symptoms.{symptom.Id}.startedOn", symptom.StartedOnState == FieldState.Contradictory ? ClarificationKind.Contradiction : ClarificationKind.Missing, $"Confirm when {symptom.Name} began."));
        Touch(now);
    }
    private void Touch(DateTimeOffset now) { Revision++; UpdatedAt = now; }
}

public sealed class InterviewAnswer(Guid interviewDraftId, string question, string answer, AnswerMode mode, DateTimeOffset answeredAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid InterviewDraftId { get; private set; } = interviewDraftId;
    public string Question { get; private set; } = question; public string Answer { get; private set; } = answer;
    public AnswerMode Mode { get; private set; } = mode; public DateTimeOffset AnsweredAt { get; private set; } = answeredAt;
}

public sealed class Symptom
{
    private Symptom() { }
    internal Symptom(Guid draftId, SymptomData x)
    {
        Id = Guid.NewGuid(); InterviewDraftId = draftId; Name = Guard.Required(x.Name, nameof(x.Name), 200); StartedOn = x.StartedOn;
        StartedOnState = x.StartedOnState; Frequency = x.Frequency; Severity = x.Severity is >= 0 and <= 10 ? x.Severity : null;
        DailyImpact = x.DailyImpact; Description = x.Description;
        Timeline.AddRange(x.Timeline.Select(t => new SymptomTimelineEntry(Id, t.OccurredOn, t.Period, t.Description)));
    }
    public Guid Id { get; private set; }
    public Guid InterviewDraftId { get; private set; }
    public string Name { get; private set; } = "";
    public DateOnly? StartedOn { get; private set; }
    public FieldState StartedOnState { get; private set; }
    public string? Frequency { get; private set; }
    public int? Severity { get; private set; }
    public string? DailyImpact { get; private set; }
    public string? Description { get; private set; }
    public InformationSource Source { get; private set; } = InformationSource.Patient;
    public List<SymptomTimelineEntry> Timeline { get; } = [];
}
public sealed class SymptomTimelineEntry(Guid symptomId, DateOnly? occurredOn, string? period, string description)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid SymptomId { get; private set; } = symptomId;
    public DateOnly? OccurredOn { get; private set; } = occurredOn; public string? Period { get; private set; } = period; public string Description { get; private set; } = description;
}
public sealed class Medication
{
    private Medication() { }
    internal Medication(Guid draftId, MedicationData x) { Id = Guid.NewGuid(); InterviewDraftId = draftId; Name = Guard.Required(x.Name, nameof(x.Name), 200); Dose = x.Dose; DoseState = x.DoseState; Schedule = x.Schedule; }
    public Guid Id { get; private set; }
    public Guid InterviewDraftId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Dose { get; private set; }
    public FieldState DoseState { get; private set; }
    public string? Schedule { get; private set; }
    public InformationSource Source { get; private set; } = InformationSource.Patient;
}
public sealed class Allergy
{
    private Allergy() { }
    internal Allergy(Guid draftId, AllergyData x) { Id = Guid.NewGuid(); InterviewDraftId = draftId; Substance = Guard.Required(x.Substance, nameof(x.Substance), 200); Reaction = x.Reaction; }
    public Guid Id { get; private set; }
    public Guid InterviewDraftId { get; private set; }
    public string Substance { get; private set; } = ""; public string? Reaction { get; private set; }
    public InformationSource Source { get; private set; } = InformationSource.Patient;
}
public sealed class ChronicCondition
{
    private ChronicCondition() { }
    internal ChronicCondition(Guid draftId, ConditionData x) { Id = Guid.NewGuid(); InterviewDraftId = draftId; Name = Guard.Required(x.Name, nameof(x.Name), 200); Description = x.Description; }
    public Guid Id { get; private set; }
    public Guid InterviewDraftId { get; private set; }
    public string Name { get; private set; } = ""; public string? Description { get; private set; }
    public InformationSource Source { get; private set; } = InformationSource.Patient;
}
public sealed class PatientQuestion(Guid interviewDraftId, string text)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid InterviewDraftId { get; private set; } = interviewDraftId; public string Text { get; private set; } = text.Trim();
    public InformationSource Source { get; private set; } = InformationSource.Patient;
}
public sealed class Clarification(Guid interviewDraftId, string fieldPath, ClarificationKind kind, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid InterviewDraftId { get; private set; } = interviewDraftId; public string FieldPath { get; private set; } = fieldPath;
    public ClarificationKind Kind { get; private set; } = kind; public string Message { get; private set; } = message; public string? Resolution { get; private set; }
}

public sealed record SymptomData(string Name, DateOnly? StartedOn, FieldState StartedOnState, string? Frequency, int? Severity, string? DailyImpact, string? Description, IReadOnlyList<TimelineData> Timeline);
public sealed record TimelineData(DateOnly? OccurredOn, string? Period, string Description);
public sealed record MedicationData(string Name, string? Dose, FieldState DoseState, string? Schedule);
public sealed record AllergyData(string Substance, string? Reaction);
public sealed record ConditionData(string Name, string? Description);
