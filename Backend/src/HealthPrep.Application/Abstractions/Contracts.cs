using HealthPrep.Domain.Appointments;

namespace HealthPrep.Application.Abstractions;

public interface IAppointmentRepository
{
    Task Add(Appointment appointment, CancellationToken ct);
    Task<Appointment?> Get(Guid id, CancellationToken ct);
    Task<Appointment?> GetByLinkToken(string token, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetForPatient(string externalPatientId, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetForTenant(Guid tenantId, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface IInterviewQuestionProvider
{
    Task<string?> GetNextQuestion(Appointment appointment, CancellationToken ct);
}

public interface IReportPdfRenderer
{
    byte[] Render(AppointmentSummary summary);
}

public interface IClock { DateTimeOffset UtcNow { get; } }

public sealed record AppointmentSummary(
    Guid AppointmentId, string ExternalAppointmentId, string ExternalPatientId, DateTimeOffset ScheduledAt,
    string ConsultationReason, InterviewStatus Status, IReadOnlyList<SymptomView> Symptoms,
    IReadOnlyList<MedicationView> Medications, IReadOnlyList<QuestionView> Questions,
    IReadOnlyList<ClarificationView> Clarifications, IReadOnlyList<TrendView> Trends, int Version,
    DateTimeOffset? ApprovedAt, bool SharingConsentGranted);

public sealed record SymptomView(Guid Id, string Name, DateOnly? StartedOn, string? Frequency, int? Severity, string? DailyImpact, string? Description);
public sealed record MedicationView(Guid Id, string Name, string? Dose, string? Schedule, bool IsAllergy);
public sealed record QuestionView(Guid Id, string Text, InformationSource Source);
public sealed record ClarificationView(Guid Id, string Field, string Message);
public sealed record TrendView(Guid Id, string SymptomName, string Kind, string Description, string EvidenceJson, bool PatientApproved);

public sealed record StartAppointment(Guid TenantId, string ExternalAppointmentId, string ExternalPatientId, DateTimeOffset ScheduledAt, string ConsultationReason);
public sealed record SubmitAnswer(string Question, string Answer, AnswerMode Mode);
public sealed record UpdateSummary(IReadOnlyList<SymptomInput> Symptoms, IReadOnlyList<MedicationInput> Medications, IReadOnlyList<string> Questions);
