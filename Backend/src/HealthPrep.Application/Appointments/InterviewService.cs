using System.Text.Json;
using HealthPrep.Application.Abstractions;
using HealthPrep.Domain.Appointments;

namespace HealthPrep.Application.Appointments;

public sealed class InterviewService(IAppointmentRepository repository, IInterviewQuestionProvider questionProvider, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Appointment> Start(StartAppointment command, CancellationToken ct)
    {
        var appointment = new Appointment(command.TenantId, command.ExternalAppointmentId, command.ExternalPatientId,
            command.ScheduledAt, command.ConsultationReason, clock.UtcNow);
        await repository.Add(appointment, ct);
        await repository.Save(ct);
        return appointment;
    }

    public async Task<string?> SubmitAnswer(Guid appointmentId, string patientId, SubmitAnswer command, CancellationToken ct)
    {
        var appointment = await PatientAppointment(appointmentId, patientId, ct);
        appointment.AddAnswer(command.Question, command.Answer, command.Mode, clock.UtcNow);
        await repository.Save(ct);
        return await questionProvider.GetNextQuestion(appointment, ct);
    }

    public async Task<AppointmentSummary> Update(Guid appointmentId, string patientId, UpdateSummary command, CancellationToken ct)
    {
        var appointment = await PatientAppointment(appointmentId, patientId, ct);
        appointment.ReplaceClinicalData(command.Symptoms, command.Medications, command.Questions, clock.UtcNow);
        BuildClarifications(appointment);
        await BuildTrends(appointment, ct);
        await repository.Save(ct);
        return Map(appointment);
    }

    public async Task<AppointmentSummary> Approve(Guid appointmentId, string patientId, CancellationToken ct)
    {
        var appointment = await PatientAppointment(appointmentId, patientId, ct);
        var beforeApproval = Map(appointment);
        var approvedSnapshot = beforeApproval with { Trends = beforeApproval.Trends.Select(x => x with { PatientApproved = true }).ToList() };
        appointment.Approve(JsonSerializer.Serialize(approvedSnapshot, JsonOptions), clock.UtcNow);
        await repository.Save(ct);
        return Map(appointment);
    }

    public async Task<AppointmentSummary> SetConsent(Guid appointmentId, string patientId, bool granted, CancellationToken ct)
    {
        var appointment = await PatientAppointment(appointmentId, patientId, ct);
        appointment.SetSharingConsent(granted, clock.UtcNow);
        await repository.Save(ct);
        return Map(appointment);
    }

    public async Task<AppointmentSummary> GetForPatient(Guid id, string patientId, CancellationToken ct) => Map(await PatientAppointment(id, patientId, ct));

    public async Task<AppointmentSummary> GetByLinkToken(string token, string patientId, CancellationToken ct)
    {
        var appointment = await repository.GetByLinkToken(token, ct) ?? throw new NotFoundException("Interview link not found.");
        if (!string.Equals(appointment.ExternalPatientId, patientId, StringComparison.Ordinal))
            throw new ForbiddenException("The interview link belongs to another patient.");
        return Map(appointment);
    }

    public async Task<AppointmentSummary> GetShared(Guid id, Guid tenantId, CancellationToken ct)
    {
        var appointment = await repository.Get(id, ct) ?? throw new NotFoundException("Appointment not found.");
        if (appointment.TenantId != tenantId) throw new ForbiddenException("The appointment belongs to another facility.");
        if (!appointment.SharingConsentGranted || appointment.Status != InterviewStatus.Shared)
            throw new ForbiddenException("The patient has not shared an approved report.");
        return Map(appointment);
    }

    public async Task AddClinicianQuestion(Guid id, Guid tenantId, string question, CancellationToken ct)
    {
        var appointment = await repository.Get(id, ct) ?? throw new NotFoundException("Appointment not found.");
        if (appointment.TenantId != tenantId) throw new ForbiddenException("The appointment belongs to another facility.");
        appointment.AddClinicianQuestion(question, clock.UtcNow);
        await repository.Save(ct);
    }

    public async Task<IReadOnlyList<AppointmentSummary>> PatientHistory(string patientId, CancellationToken ct) =>
        (await repository.GetForPatient(patientId, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<AppointmentSummary>> FacilityDashboard(Guid tenantId, CancellationToken ct) =>
        (await repository.GetForTenant(tenantId, ct)).Select(Map).ToList();

    private async Task<Appointment> PatientAppointment(Guid id, string patientId, CancellationToken ct)
    {
        var appointment = await repository.Get(id, ct) ?? throw new NotFoundException("Appointment not found.");
        if (!string.Equals(appointment.ExternalPatientId, patientId, StringComparison.Ordinal))
            throw new ForbiddenException("The interview belongs to another patient.");
        return appointment;
    }

    private static void BuildClarifications(Appointment appointment)
    {
        appointment.Clarifications.Clear();
        foreach (var medication in appointment.Medications.Where(x => !x.IsAllergy && string.IsNullOrWhiteSpace(x.Dose)))
            appointment.Clarifications.Add(new(appointment.Id, $"medications.{medication.Id}.dose", $"Confirm the dose of {medication.Name}."));
        foreach (var symptom in appointment.Symptoms.Where(x => x.StartedOn is null))
            appointment.Clarifications.Add(new(appointment.Id, $"symptoms.{symptom.Id}.startedOn", $"Confirm when {symptom.Name} began."));
    }

    private async Task BuildTrends(Appointment appointment, CancellationToken ct)
    {
        appointment.Trends.Clear();
        var previous = (await repository.GetForPatient(appointment.ExternalPatientId, ct))
            .Where(x => x.Id != appointment.Id && x.Status is InterviewStatus.Approved or InterviewStatus.Shared)
            .OrderByDescending(x => x.ScheduledAt).ToList();
        foreach (var symptom in appointment.Symptoms)
        {
            var earlier = previous.SelectMany(x => x.Symptoms.Select(s => (Visit: x, Symptom: s)))
                .FirstOrDefault(x => x.Symptom.Name.Equals(symptom.Name, StringComparison.OrdinalIgnoreCase));
            var kind = earlier == default ? "new" : earlier.Symptom.Severity != symptom.Severity ? "changed" : "recurring";
            var description = kind switch { "new" => "First reported occurrence.", "changed" => $"Severity changed from {earlier.Symptom.Severity?.ToString() ?? "unknown"} to {symptom.Severity?.ToString() ?? "unknown"}.", _ => "Reported again; absence in other interviews does not imply resolution." };
            var evidence = earlier == default ? new[] { appointment.Id } : new[] { earlier.Visit.Id, appointment.Id };
            appointment.Trends.Add(new(appointment.Id, symptom.Name, kind, description, JsonSerializer.Serialize(evidence), false));
        }
    }

    public static AppointmentSummary Map(Appointment x)
    {
        var latest = x.SummaryVersions.OrderByDescending(v => v.Version).FirstOrDefault();
        return new(x.Id, x.ExternalAppointmentId, x.ExternalPatientId, x.ScheduledAt, x.ConsultationReason, x.Status,
            x.Symptoms.Select(s => new SymptomView(s.Id, s.Name, s.StartedOn, s.Frequency, s.Severity, s.DailyImpact, s.Description)).ToList(),
            x.Medications.Select(m => new MedicationView(m.Id, m.Name, m.Dose, m.Schedule, m.IsAllergy)).ToList(),
            x.Questions.Select(q => new QuestionView(q.Id, q.Text, q.Source)).ToList(),
            x.Clarifications.Select(c => new ClarificationView(c.Id, c.Field, c.Message)).ToList(),
            x.Trends.Select(t => new TrendView(t.Id, t.SymptomName, t.Kind, t.Description, t.EvidenceJson, t.PatientApproved)).ToList(),
            latest?.Version ?? 0, latest?.ApprovedAt, x.SharingConsentGranted);
    }
}

public sealed class NotFoundException(string message) : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);
