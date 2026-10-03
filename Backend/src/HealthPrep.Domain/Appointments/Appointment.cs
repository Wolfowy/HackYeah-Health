namespace HealthPrep.Domain.Appointments;

public enum InterviewStatus { NotStarted, InProgress, AwaitingApproval, Approved, Shared }
public enum AnswerMode { Text, Voice }
public enum InformationSource { Patient, ApplicationObservation, Clinician }

public sealed class Appointment
{
    private Appointment() { }

    public Appointment(Guid tenantId, string externalAppointmentId, string externalPatientId,
        DateTimeOffset scheduledAt, string consultationReason, DateTimeOffset now)
    {
        if (scheduledAt <= now) throw new DomainException("Appointment must be in the future.");
        Id = Guid.NewGuid();
        TenantId = tenantId;
        ExternalAppointmentId = Required(externalAppointmentId, 100);
        ExternalPatientId = Required(externalPatientId, 100);
        ScheduledAt = scheduledAt;
        ConsultationReason = Required(consultationReason, 1000);
        EditDeadline = scheduledAt.AddHours(-24);
        InterviewLinkToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        CreatedAt = UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ExternalAppointmentId { get; private set; } = "";
    public string ExternalPatientId { get; private set; } = "";
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset EditDeadline { get; private set; }
    public string ConsultationReason { get; private set; } = "";
    public string InterviewLinkToken { get; private set; } = "";
    public InterviewStatus Status { get; private set; }
    public bool SharingConsentGranted { get; private set; }
    public DateTimeOffset? ConsentGrantedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<InterviewAnswer> Answers { get; } = [];
    public List<Symptom> Symptoms { get; } = [];
    public List<Medication> Medications { get; } = [];
    public List<PatientQuestion> Questions { get; } = [];
    public List<Clarification> Clarifications { get; } = [];
    public List<SummaryVersion> SummaryVersions { get; } = [];
    public List<TrendObservation> Trends { get; } = [];

    public void AddAnswer(string question, string answer, AnswerMode mode, DateTimeOffset now)
    {
        EnsureEditable(now);
        Answers.Add(new(Id, Required(question, 1000), Required(answer, 4000), mode, now));
        Status = InterviewStatus.InProgress;
        UpdatedAt = now;
    }

    public void ReplaceClinicalData(IEnumerable<SymptomInput> symptoms, IEnumerable<MedicationInput> medications,
        IEnumerable<string> questions, DateTimeOffset now)
    {
        EnsureEditable(now);
        Symptoms.Clear(); Medications.Clear(); Questions.RemoveAll(x => x.Source == InformationSource.Patient);
        Symptoms.AddRange(symptoms.Select(x => new Symptom(Id, x.Name, x.StartedOn, x.Frequency, x.Severity, x.DailyImpact, x.Description)));
        Medications.AddRange(medications.Select(x => new Medication(Id, x.Name, x.Dose, x.Schedule, x.IsAllergy)));
        Questions.AddRange(questions.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new PatientQuestion(Id, x, InformationSource.Patient)));
        Status = InterviewStatus.AwaitingApproval;
        UpdatedAt = now;
    }

    public void AddClinicianQuestion(string text, DateTimeOffset now)
    {
        Questions.Add(new(Id, Required(text, 1000), InformationSource.Clinician));
        if (Status is InterviewStatus.Approved or InterviewStatus.Shared) Status = InterviewStatus.AwaitingApproval;
        UpdatedAt = now;
    }

    public SummaryVersion Approve(string snapshotJson, DateTimeOffset now)
    {
        EnsureEditable(now);
        foreach (var trend in Trends) trend.Approve();
        var version = new SummaryVersion(Id, SummaryVersions.Count + 1, snapshotJson, now);
        SummaryVersions.Add(version);
        Status = InterviewStatus.Approved;
        UpdatedAt = now;
        return version;
    }

    public void SetSharingConsent(bool granted, DateTimeOffset now)
    {
        SharingConsentGranted = granted;
        ConsentGrantedAt = granted ? now : null;
        if (granted && Status == InterviewStatus.Approved) Status = InterviewStatus.Shared;
        if (!granted && Status == InterviewStatus.Shared) Status = InterviewStatus.Approved;
        UpdatedAt = now;
    }

    private void EnsureEditable(DateTimeOffset now)
    {
        if (now >= EditDeadline) throw new DomainException("The interview edit deadline has passed.");
    }

    private static string Required(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException("A required value is missing.")
        : value.Trim().Length > max ? throw new DomainException($"Value exceeds {max} characters.") : value.Trim();
}

public sealed class InterviewAnswer(Guid appointmentId, string question, string answer, AnswerMode mode, DateTimeOffset answeredAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AppointmentId { get; private set; } = appointmentId;
    public string Question { get; private set; } = question;
    public string Answer { get; private set; } = answer;
    public AnswerMode Mode { get; private set; } = mode;
    public DateTimeOffset AnsweredAt { get; private set; } = answeredAt;
}

public sealed class Symptom(Guid appointmentId, string name, DateOnly? startedOn, string? frequency, int? severity, string? dailyImpact, string? description)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public string Name { get; private set; } = name.Trim(); public DateOnly? StartedOn { get; private set; } = startedOn;
    public string? Frequency { get; private set; } = frequency; public int? Severity { get; private set; } = severity is >= 0 and <= 10 ? severity : null;
    public string? DailyImpact { get; private set; } = dailyImpact; public string? Description { get; private set; } = description;
}

public sealed class Medication(Guid appointmentId, string name, string? dose, string? schedule, bool isAllergy)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public string Name { get; private set; } = name.Trim(); public string? Dose { get; private set; } = dose;
    public string? Schedule { get; private set; } = schedule; public bool IsAllergy { get; private set; } = isAllergy;
}

public sealed class PatientQuestion(Guid appointmentId, string text, InformationSource source)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public string Text { get; private set; } = text.Trim(); public InformationSource Source { get; private set; } = source;
}

public sealed class Clarification(Guid appointmentId, string field, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public string Field { get; private set; } = field; public string Message { get; private set; } = message;
}

public sealed class SummaryVersion(Guid appointmentId, int version, string snapshotJson, DateTimeOffset approvedAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public int Version { get; private set; } = version; public string SnapshotJson { get; private set; } = snapshotJson;
    public DateTimeOffset ApprovedAt { get; private set; } = approvedAt;
}

public sealed class TrendObservation(Guid appointmentId, string symptomName, string kind, string description, string evidenceJson, bool patientApproved)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid AppointmentId { get; private set; } = appointmentId;
    public string SymptomName { get; private set; } = symptomName; public string Kind { get; private set; } = kind;
    public string Description { get; private set; } = description; public string EvidenceJson { get; private set; } = evidenceJson;
    public bool PatientApproved { get; private set; } = patientApproved;
    public void Approve() => PatientApproved = true;
}

public sealed record SymptomInput(string Name, DateOnly? StartedOn, string? Frequency, int? Severity, string? DailyImpact, string? Description);
public sealed record MedicationInput(string Name, string? Dose, string? Schedule, bool IsAllergy);
public sealed class DomainException(string message) : Exception(message);
