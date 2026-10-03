using DocPrep.Domain.Visits;

namespace DocPrep.Application.Contracts;

public sealed record ReceptionPatient(string Name, string? Phone, string? Email);
public sealed record ClinicianView(string Id, string Name, string Specialty, string? DefaultRoom, bool IsActive);
public sealed record CreateClinicianCommand(string Name, string Specialty, string? DefaultRoom, string? Id = null);
public sealed record UpdateClinicianCommand(string Name, string Specialty, string? DefaultRoom, bool IsActive = true);
public sealed record CreateAppointmentCommand(string PatientName, string? Phone, string? Email, string DoctorId,
    DateTimeOffset ScheduledAt, int DurationMinutes, string? Room, string VisitType = "InPerson",
    string TimeZone = "Europe/Warsaw", string? ExternalVisitId = null, string? Pesel = null,
    DateTimeOffset? ServiceExpiresAt = null, string? LocationInstructions = null, bool SendInvitation = false);
public sealed record UpdateAppointmentCommand(DateTimeOffset ScheduledAt, int DurationMinutes, string DoctorId,
    string? Room, string VisitType = "InPerson", string TimeZone = "Europe/Warsaw",
    DateTimeOffset? ServiceExpiresAt = null, string? LocationInstructions = null, uint? ExpectedVersion = null);
public sealed record AppointmentView(Guid VisitId, string ExternalVisitId, DateTimeOffset ScheduledAt,
    DateTimeOffset EndsAt, DateTimeOffset ServiceExpiresAt, string TimeZone, int DurationMinutes,
    ClinicianView? Doctor, VisitFacility Facility, string? Room, string VisitType, VisitStatus Status,
    ReceptionPatient? Patient, string DeliveryStatus, string DeliveryMode, DateTimeOffset? LastSentAt,
    bool HasOpenSupplementationRound, Guid? InterviewId, string? InterviewStatus, uint Version,
    ContactChannel ContactChannel, string? DeliveryError);
public sealed record CreatedAppointmentView(AppointmentView Appointment, InvitationLinkView Invitation);
public sealed record InvitationLinkView(Guid VisitId, Guid InterviewId, string Url, DateTimeOffset ExpiresAt,
    bool CanStart, int RemainingSessions);
public sealed record SendAppointmentInvitationCommand(ContactChannel Channel);
