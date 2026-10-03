using System.Net.Mail;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Application.Visits;
using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Tenancy;
using DocPrep.Domain.Visits;
using DocPrep.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocPrep.Infrastructure.Services;

public sealed class ReceptionService(DocPrepDbContext db, IntegrationService integration,
    IPatientDataProtector protector, INotificationSender notifications, IClock clock,
    IOptions<NotificationOptions> options)
{
    public async Task<VisitFacility> Facility(Guid facilityId, CancellationToken ct)
    {
        var facility = await db.Facilities.FindAsync([facilityId], ct) ?? throw new NotFoundError();
        return new(facility.Id, facility.Name, null);
    }

    public async Task<IReadOnlyList<ClinicianView>> Doctors(Guid facilityId, bool includeInactive, CancellationToken ct) =>
        await db.Clinicians.AsNoTracking().Where(x => x.FacilityId == facilityId && (includeInactive || x.IsActive))
            .OrderBy(x => x.Name).Select(x => new ClinicianView(x.Id, x.Name, x.Specialty, x.DefaultRoom, x.IsActive)).ToListAsync(ct);

    public async Task<ClinicianView> CreateDoctor(Guid facilityId, CreateClinicianCommand command, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N") : command.Id.Trim();
        if (await db.Clinicians.AnyAsync(x => x.FacilityId == facilityId && x.Id == id, ct))
            throw new ConflictError("clinician.id_exists", "This clinician identifier already exists in the facility.");
        var doctor = new Clinician(facilityId, id, command.Name, command.Specialty, command.DefaultRoom, clock.UtcNow);
        db.Clinicians.Add(doctor);
        db.AuditEvents.Add(new(facilityId, null, "reception", "clinician.created", clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Map(doctor);
    }

    public async Task<ClinicianView> UpdateDoctor(Guid facilityId, string id, UpdateClinicianCommand command, CancellationToken ct)
    {
        var doctor = await db.Clinicians.SingleOrDefaultAsync(x => x.FacilityId == facilityId && x.Id == id, ct) ?? throw new NotFoundError();
        doctor.Update(command.Name, command.Specialty, command.DefaultRoom, command.IsActive);
        db.AuditEvents.Add(new(facilityId, null, "reception", "clinician.updated", clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return Map(doctor);
    }

    public async Task<CreatedAppointmentView> Create(Guid facilityId, CreateAppointmentCommand command, CancellationToken ct)
    {
        var patient = ValidatePatient(new(command.PatientName, command.Phone, command.Email));
        var doctor = await ActiveDoctor(facilityId, command.DoctorId, ct);
        var facility = await Facility(facilityId, ct);
        ValidateSchedule(command.ScheduledAt, command.DurationMinutes, command.TimeZone, command.VisitType);
        var room = command.VisitType == "Remote" ? command.Room : command.Room ?? doctor.DefaultRoom;
        if (command.VisitType == "InPerson" && string.IsNullOrWhiteSpace(room)) throw new ArgumentException("A room is required for an in-person visit.");
        var channel = string.IsNullOrWhiteSpace(patient.Phone) ? ContactChannel.Email : ContactChannel.Sms;
        var contact = channel == ContactChannel.Sms ? patient.Phone! : patient.Email!;
        var created = await integration.CreateVisit(new(facilityId,
            string.IsNullOrWhiteSpace(command.ExternalVisitId) ? $"WIZ-{Guid.NewGuid():N}" : command.ExternalVisitId.Trim(),
            command.Pesel, command.ScheduledAt, command.ServiceExpiresAt ?? EndOfDay(command.ScheduledAt, command.TimeZone),
            contact, channel, doctor.Id, command.TimeZone, doctor.Name, doctor.Specialty, facility.Name, facility.Address,
            room, command.VisitType, command.LocationInstructions, command.DurationMinutes, patient, command.SendInvitation), ct);
        return new(await Get(facilityId, created.VisitId, ct), await Invitation(facilityId, created.VisitId, ct));
    }

    public async Task<AppointmentView> Get(Guid facilityId, Guid visitId, CancellationToken ct)
    {
        var visit = await Visit(facilityId, visitId, ct);
        return await Map(visit, ct);
    }

    public async Task<PagedResult<AppointmentView>> Search(Guid facilityId, DateTimeOffset? from, DateTimeOffset? to,
        string? doctorId, string? room, VisitStatus? status, string? query, int page, int pageSize, CancellationToken ct)
    {
        if (from is not null && to is not null && from >= to) throw new ArgumentException("from must precede to.");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var visits = db.Visits.Where(x => x.FacilityId == facilityId);
        if (from is not null) visits = visits.Where(x => x.ScheduledAt >= from);
        if (to is not null) visits = visits.Where(x => x.ScheduledAt < to);
        if (!string.IsNullOrWhiteSpace(doctorId)) visits = visits.Where(x => x.AssignedClinicianId == doctorId);
        if (!string.IsNullOrWhiteSpace(room)) visits = visits.Where(x => x.Room == room);
        // Expiry is a preparation status, not a cancellation of the calendar booking.
        if (status == VisitStatus.Expired) visits = visits.Where(x => x.Status != VisitStatus.Cancelled && x.ServiceExpiresAt <= clock.UtcNow);
        else if (status is not null) visits = visits.Where(x => x.Status == status && (status == VisitStatus.Cancelled || x.ServiceExpiresAt > clock.UtcNow));
        visits = visits.OrderBy(x => x.ScheduledAt).ThenBy(x => x.Id);
        if (string.IsNullOrWhiteSpace(query))
        {
            var total = await visits.CountAsync(ct);
            var selected = await visits.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var items = new List<AppointmentView>();
            foreach (var visit in selected) items.Add(await Map(visit, ct));
            return new(items, page, pageSize, total);
        }
        // Patient names are encrypted; filter after decrypting, before pagination.
        if (query.Length > 200) throw new ArgumentException("Search text exceeds 200 characters.");
        var matching = new List<AppointmentView>();
        foreach (var visit in await visits.ToListAsync(ct))
        {
            var view = await Map(visit, ct);
            if (view.Patient?.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true ||
                view.ExternalVisitId.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)) matching.Add(view);
        }
        return new(matching.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, matching.Count);
    }

    public async Task<IReadOnlyList<AppointmentView>> Calendar(Guid facilityId, DateTimeOffset from, DateTimeOffset to,
        string? doctorId, string? room, CancellationToken ct)
    {
        if (from == default || to == default || to <= from || to - from > TimeSpan.FromDays(93))
            throw new ArgumentException("Provide a calendar range between 1 instant and 93 days (from inclusive, to exclusive).");
        var visits = db.Visits.Where(x => x.FacilityId == facilityId && x.Status != VisitStatus.Cancelled &&
            x.ScheduledAt < to && x.ScheduledAt.AddMinutes(x.DurationMinutes) > from);
        if (!string.IsNullOrWhiteSpace(doctorId)) visits = visits.Where(x => x.AssignedClinicianId == doctorId);
        if (!string.IsNullOrWhiteSpace(room)) visits = visits.Where(x => x.Room == room);
        var result = new List<AppointmentView>();
        foreach (var visit in await visits.OrderBy(x => x.ScheduledAt).ToListAsync(ct)) result.Add(await Map(visit, ct));
        return result;
    }

    public async Task<AppointmentView> Update(Guid facilityId, Guid visitId, UpdateAppointmentCommand command, CancellationToken ct)
    {
        var visit = await Visit(facilityId, visitId, ct);
        if (command.ExpectedVersion is not null && visit.ConcurrencyVersion != command.ExpectedVersion)
            throw new ConflictError("visit.version_conflict", "The appointment changed. Reload it before saving.");
        ValidateSchedule(command.ScheduledAt, command.DurationMinutes, command.TimeZone, command.VisitType);
        var doctor = await ActiveDoctor(facilityId, command.DoctorId, ct);
        var room = command.Room ?? doctor.DefaultRoom;
        if (command.VisitType == "InPerson" && string.IsNullOrWhiteSpace(room)) throw new ArgumentException("A room is required for an in-person visit.");
        await integration.Update(facilityId, visitId, new(command.ScheduledAt,
            command.ServiceExpiresAt ?? EndOfDay(command.ScheduledAt, command.TimeZone), command.TimeZone,
            doctor.Id, doctor.Name, doctor.Specialty, visit.FacilityName, visit.FacilityAddress,
            room, command.VisitType, command.LocationInstructions, command.DurationMinutes), ct);
        return await Get(facilityId, visitId, ct);
    }

    public async Task<InvitationLinkView> Invitation(Guid facilityId, Guid visitId, CancellationToken ct)
    {
        var visit = await Visit(facilityId, visitId, ct);
        if (visit.Status == VisitStatus.Cancelled || visit.ServiceExpiresAt <= clock.UtcNow)
            throw new ConflictError("visit.inactive", "The visit is inactive.");
        var interview = await db.AgentInterviews.Where(x => x.VisitProcessId == visitId).OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundError();
        var invitation = await db.InterviewInvitations.Where(x => x.InterviewId == interview.Id && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundError();
        if (!invitation.CanReview(clock.UtcNow)) throw new ConflictError("agent_invitation.inactive", "The invitation is inactive.");
        if (invitation.EncryptedToken is null)
            throw new ConflictError("invitation.regeneration_required", "This older invitation cannot be recovered; regenerate it once.");
        var token = protector.Unprotect(invitation.EncryptedToken);
        var baseUrl = options.Value.FrontendBaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ServiceUnavailableError("invitation.invalid_base_url", "The frontend base URL is invalid.");
        var canStart = invitation.IsUsable(clock.UtcNow) && interview.Status is not (AgentInterviewStatus.Completed or AgentInterviewStatus.Cancelled or AgentInterviewStatus.Expired);
        return new(visitId, interview.Id, $"{baseUrl}/i/{Uri.EscapeDataString(token)}", invitation.ExpiresAt,
            canStart, Math.Max(0, invitation.MaxSessionCount - invitation.SessionCount));
    }

    public async Task<InvitationLinkView> RegenerateInvitation(Guid facilityId, Guid visitId, ContactChannel channel, CancellationToken ct)
    {
        await Visit(facilityId, visitId, ct);
        var patient = await Patient(visitId, ct);
        var contact = Contact(patient, channel);
        await integration.RegenerateInvitation(facilityId, visitId, contact, ct, channel, false);
        return await Invitation(facilityId, visitId, ct);
    }

    public async Task<AppointmentView> SendInvitation(Guid facilityId, Guid visitId, ContactChannel channel, CancellationToken ct)
    {
        var visit = await Visit(facilityId, visitId, ct);
        var contact = Contact(await Patient(visitId, ct), channel);
        var link = await Invitation(facilityId, visitId, ct);
        var invitation = await db.InterviewInvitations.Where(x => x.InterviewId == link.InterviewId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstAsync(ct);
        var grant = await db.AccessGrants.Where(x => x.VisitProcessId == visitId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundError();
        var attempt = new DeliveryAttempt(visitId, grant.Id, channel.ToString(), protector.Protect(contact), clock.UtcNow);
        visit.ChangeContactChannel(channel, clock.UtcNow);
        db.DeliveryAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        var result = await notifications.SendInterviewLink(channel.ToString(), contact, protector.Unprotect(invitation.EncryptedToken!), ct);
        if (result.Delivered) attempt.Delivered(result.ProviderId, result.Simulated);
        else attempt.Failed(result.Error ?? "Delivery failed.");
        db.AuditEvents.Add(new(facilityId, visitId, "reception", "invitation.sent", clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return await Get(facilityId, visitId, ct);
    }

    private async Task<VisitProcess> Visit(Guid facilityId, Guid visitId, CancellationToken ct) =>
        await db.Visits.SingleOrDefaultAsync(x => x.Id == visitId && x.FacilityId == facilityId, ct) ?? throw new NotFoundError();
    private async Task<Clinician> ActiveDoctor(Guid facilityId, string id, CancellationToken ct) =>
        await db.Clinicians.SingleOrDefaultAsync(x => x.FacilityId == facilityId && x.Id == id && x.IsActive, ct)
            ?? throw new ConflictError("clinician.unavailable", "Choose an active clinician from this facility.");
    private async Task<ReceptionPatient?> Patient(Guid visitId, CancellationToken ct)
    {
        var details = await db.ReceptionDetails.FindAsync([visitId], ct);
        return details is null ? null : JsonSerializer.Deserialize<ReceptionPatient>(protector.Unprotect(details.EncryptedPatient));
    }
    private async Task<AppointmentView> Map(VisitProcess visit, CancellationToken ct)
    {
        var delivery = await db.DeliveryAttempts.Where(x => x.VisitProcessId == visit.Id).OrderByDescending(x => x.AttemptedAt).FirstOrDefaultAsync(ct);
        var interview = await db.AgentInterviews.Where(x => x.VisitProcessId == visit.Id).OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct);
        var hasRound = await db.SupplementationRounds.AnyAsync(x => x.VisitProcessId == visit.Id &&
            (x.Status == Domain.Supplementation.SupplementationRoundStatus.Open || x.Status == Domain.Supplementation.SupplementationRoundStatus.AwaitingApproval), ct);
        var status = visit.Status != VisitStatus.Cancelled && visit.ServiceExpiresAt <= clock.UtcNow ? VisitStatus.Expired : visit.Status;
        var catalogDoctor = string.IsNullOrWhiteSpace(visit.AssignedClinicianId) ? null :
            await db.Clinicians.FindAsync([visit.FacilityId, visit.AssignedClinicianId], ct);
        var doctor = string.IsNullOrWhiteSpace(visit.AssignedClinicianId) ? null : new ClinicianView(visit.AssignedClinicianId,
            visit.DoctorName ?? visit.AssignedClinicianId, visit.DoctorSpecialty ?? "", catalogDoctor?.DefaultRoom, catalogDoctor?.IsActive ?? true);
        return new(visit.Id, visit.ExternalVisitId, visit.ScheduledAt, visit.ScheduledAt.AddMinutes(visit.DurationMinutes),
            visit.ServiceExpiresAt, visit.TimeZone, visit.DurationMinutes, doctor,
            new(visit.FacilityId, visit.FacilityName, visit.FacilityAddress), visit.Room, visit.VisitType, status,
            await Patient(visit.Id, ct), delivery?.Status.ToString() ?? "NotSent",
            delivery is null || delivery.Status == DeliveryStatus.Pending ? "not_sent" : delivery.IsSimulated ? "demo" : "provider",
            delivery?.Status == DeliveryStatus.Delivered ? delivery.AttemptedAt : null, hasRound,
            interview?.Id, interview?.Status.ToString(), visit.ConcurrencyVersion, visit.ContactChannel, delivery?.FailureReason);
    }
    private static ClinicianView Map(Clinician doctor) => new(doctor.Id, doctor.Name, doctor.Specialty, doctor.DefaultRoom, doctor.IsActive);
    private static string Contact(ReceptionPatient? patient, ContactChannel channel)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentException("Unknown contact channel.");
        var contact = channel == ContactChannel.Sms ? patient?.Phone : patient?.Email;
        return string.IsNullOrWhiteSpace(contact) ? throw new ConflictError("invitation.contact_missing", "No contact is available for this channel.") : contact;
    }
    private static ReceptionPatient ValidatePatient(ReceptionPatient patient)
    {
        if (string.IsNullOrWhiteSpace(patient.Name) || patient.Name.Trim().Length > 200) throw new ArgumentException("Provide a patient name of at most 200 characters.");
        var phone = string.IsNullOrWhiteSpace(patient.Phone) ? null : patient.Phone.Trim();
        var email = string.IsNullOrWhiteSpace(patient.Email) ? null : patient.Email.Trim();
        if (phone is null && email is null) throw new ArgumentException("Provide a phone number or e-mail address.");
        if (phone is not null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[\d\s()-]{9,20}$")) throw new ArgumentException("Invalid phone number.");
        if (email is not null && (email.Length > 320 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)) throw new ArgumentException("Invalid e-mail address.");
        return new(patient.Name.Trim(), phone, email);
    }
    private static void ValidateSchedule(DateTimeOffset at, int minutes, string timeZone, string visitType)
    {
        if (at == default || minutes is < 5 or > 240) throw new ArgumentException("Provide a date and a duration between 5 and 240 minutes.");
        if (visitType is not ("InPerson" or "Remote")) throw new ArgumentException("visitType must be InPerson or Remote.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Unknown time zone."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("Invalid time zone."); }
    }
    private static DateTimeOffset EndOfDay(DateTimeOffset at, string timeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTime(at, zone).Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
