using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Supplementation;
using DocPrep.Domain.Visits;

namespace DocPrep.Application.Visits;

public sealed class IntegrationService(IDocPrepStore store, IPatientDataProtector protector, ICredentialService credentials,
    IPatientSessionStore sessions, INotificationSender notifications, IClock clock)
{
    public async Task<InvitationResult> CreateVisit(CreateVisitCommand command, CancellationToken ct)
    {
        if (await store.FindVisit(command.FacilityId, command.ExternalVisitId, ct) is not null)
            throw new ConflictError("visit.external_id_conflict", "A visit with this external identifier already exists.");
        var correlation = protector.CorrelationKey(command.Pesel);
        var patient = await store.FindPatient(correlation, ct);
        if (patient is null) { patient = new(correlation, protector.Protect(command.Pesel), clock.UtcNow); store.Add(patient); }
        var visit = new VisitProcess(command.FacilityId, patient.Id, command.ExternalVisitId, command.ScheduledAt,
            command.ServiceExpiresAt, command.AssignedClinicianId, command.Channel, clock.UtcNow, command.TimeZone,
            command.DoctorName, command.DoctorSpecialty, command.FacilityName, command.FacilityAddress,
            command.Room, command.VisitType, command.LocationInstructions);
        store.Add(visit); store.Add(new InterviewDraft(visit.Id, clock.UtcNow));
        var agentInterview = new AgentInterview(visit.Id, "pre-visit", clock.UtcNow); store.Add(agentInterview);
        var agentInvitationToken = credentials.GenerateLinkToken();
        store.Add(new InterviewInvitation(agentInterview.Id, credentials.Hash(agentInvitationToken), visit.ServiceExpiresAt, 3, clock.UtcNow));
        var access = await CreateInvitationCore(visit, command.Contact, agentInvitationToken, ct);
        var result = new InvitationResult(visit.Id, access.LinkToken, access.VisitCode, access.DeliveryStatus, agentInterview.Id, agentInvitationToken);
        store.Add(new AuditEvent(command.FacilityId, visit.Id, "facility-api", "visit.created", clock.UtcNow));
        await store.Save(ct);
        return result;
    }

    public async Task<InvitationResult> RegenerateInvitation(Guid facilityId, Guid visitId, string contact, CancellationToken ct)
    {
        var visit = await FacilityVisit(facilityId, visitId, ct);
        if (visit.Status is VisitStatus.Cancelled or VisitStatus.Expired) throw new ConflictError("visit.inactive", "Cannot generate access for an inactive visit.");
        foreach (var grant in await store.GetAccessGrants(visitId, ct)) grant.Revoke(clock.UtcNow);
        await sessions.RevokeForVisit(visitId, ct);
        var interview = await store.GetAgentInterviewByVisit(visitId, ct);
        if (interview is null) { interview = new AgentInterview(visit.Id, "pre-visit", clock.UtcNow); store.Add(interview); }
        foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct)) invitation.Revoke(clock.UtcNow);
        var agentInvitationToken = credentials.GenerateLinkToken();
        store.Add(new InterviewInvitation(interview.Id, credentials.Hash(agentInvitationToken), visit.ServiceExpiresAt, 3, clock.UtcNow));
        var access = await CreateInvitationCore(visit, contact, agentInvitationToken, ct);
        var result = new InvitationResult(visit.Id, access.LinkToken, access.VisitCode, access.DeliveryStatus, interview.Id, agentInvitationToken);
        store.Add(new AuditEvent(facilityId, visitId, "facility-api", "invitation.regenerated", clock.UtcNow));
        await store.Save(ct);
        return result;
    }

    public async Task<IReadOnlyList<AdminVisitView>> Dashboard(Guid facilityId, CancellationToken ct)
    {
        var visits = await store.GetFacilityVisits(facilityId, ct); var result = new List<AdminVisitView>();
        foreach (var visit in visits)
        {
            visit.Expire(clock.UtcNow);
            var delivery = await store.GetLatestDelivery(visit.Id, ct); var round = await store.GetOpenRound(visit.Id, ct);
            var interview = await store.GetAgentInterviewByVisit(visit.Id, ct);
            var details = interview is null ? null : Details(visit, interview);
            result.Add(new(visit.Id, visit.ExternalVisitId, visit.ScheduledAt, visit.Status,
                delivery?.Status.ToString() ?? "NotSent", round is not null, details, interview?.Id,
                interview?.Status.ToString(), interview?.ExtractionStatus.ToString(), interview?.ImportStatus.ToString()));
        }
        await store.Save(ct);
        return result;
    }

    public async Task<PagedResult<AdminVisitView>> Search(Guid facilityId, DateTimeOffset? from, DateTimeOffset? to,
        VisitStatus? status, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var values = (await Dashboard(facilityId, ct)).Where(x => (from is null || x.ScheduledAt >= from) &&
            (to is null || x.ScheduledAt <= to) && (status is null || x.Status == status)).ToList();
        return new(values.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, values.Count);
    }

    public async Task<AdminVisitView> Update(Guid facilityId, Guid visitId, UpdateVisitCommand command, CancellationToken ct)
    {
        var visit = await FacilityVisit(facilityId, visitId, ct);
        visit.Reschedule(command.ScheduledAt, command.ServiceExpiresAt, command.TimeZone, command.AssignedClinicianId,
            command.DoctorName, command.DoctorSpecialty, command.FacilityName, command.FacilityAddress, command.Room,
            command.VisitType, command.LocationInstructions, clock.UtcNow);
        foreach (var grant in await store.GetAccessGrants(visitId, ct)) grant.ChangeValidity(command.ServiceExpiresAt, clock.UtcNow);
        foreach (var interview in await store.GetAgentInterviewsByVisit(visitId, ct))
            foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct))
                invitation.ChangeExpiry(command.ServiceExpiresAt, clock.UtcNow);
        store.Add(new AuditEvent(facilityId, visitId, "facility-api", "visit.updated", clock.UtcNow));
        await store.Save(ct);
        return (await Dashboard(facilityId, ct)).Single(x => x.VisitId == visitId);
    }

    public async Task Cancel(Guid facilityId, Guid visitId, CancellationToken ct)
    {
        var visit = await FacilityVisit(facilityId, visitId, ct); visit.Cancel(clock.UtcNow);
        foreach (var grant in await store.GetAccessGrants(visitId, ct)) grant.Revoke(clock.UtcNow);
        foreach (var interview in await store.GetAgentInterviewsByVisit(visitId, ct))
        {
            interview.Cancel(clock.UtcNow);
            foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct)) invitation.Revoke(clock.UtcNow);
        }
        await sessions.RevokeForVisit(visitId, ct); store.Add(new AuditEvent(facilityId, visitId, "facility-api", "visit.cancelled", clock.UtcNow));
        await store.Save(ct);
    }

    public async Task<Guid> DeleteByPesel(Guid facilityId, string pesel, string verificationReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verificationReference)) throw new ConflictError("deletion.verification_required", "Verified request reference is required.");
        var correlation = protector.CorrelationKey(pesel); var patient = await store.FindPatient(correlation, ct) ?? throw new NotFoundError();
        var request = new DeletionRequest(correlation, facilityId, verificationReference, clock.UtcNow); request.Processing(); store.Add(request);
        foreach (var visit in await store.GetPatientVisits(patient.Id, ct))
        {
            foreach (var grant in await store.GetAccessGrants(visit.Id, ct)) grant.Revoke(clock.UtcNow);
            foreach (var interview in await store.GetAgentInterviewsByVisit(visit.Id, ct))
            {
                interview.Cancel(clock.UtcNow);
                foreach (var invitation in await store.GetInterviewInvitations(interview.Id, ct)) invitation.Revoke(clock.UtcNow);
            }
            if (visit.Status != VisitStatus.Cancelled) visit.Cancel(clock.UtcNow);
            await sessions.RevokeForVisit(visit.Id, ct);
        }
        await store.Save(ct);
        try { await store.DeletePatientData(patient.Id, ct); request.Complete(clock.UtcNow); await store.Save(ct); return request.Id; }
        catch (Exception ex) { request.Fail(ex.GetType().Name); await store.Save(ct); throw new ConflictError("deletion.failed", "Deletion was blocked and scheduled for operational review."); }
    }

    public async Task<DeletionRequestView> DeletionStatus(Guid facilityId, Guid requestId, CancellationToken ct)
    {
        var request = await store.GetDeletionRequest(requestId, ct) ?? throw new NotFoundError();
        if (request.RequestedByFacilityId != facilityId) throw new NotFoundError();
        return new(request.Id, request.Status.ToString(), request.RequestedAt, request.CompletedAt, request.LastError);
    }

    public async Task RetryDeletion(Guid facilityId, Guid requestId, CancellationToken ct)
    {
        var request = await store.GetDeletionRequest(requestId, ct) ?? throw new NotFoundError();
        if (request.RequestedByFacilityId != facilityId) throw new NotFoundError();
        if (request.Status == Domain.Privacy.DeletionStatus.Completed) return;
        var patient = await store.FindPatient(request.PatientCorrelationKey, ct);
        if (patient is null) { request.Complete(clock.UtcNow); await store.Save(ct); return; }
        request.Processing(); await store.Save(ct);
        try { await store.DeletePatientData(patient.Id, ct); request.Complete(clock.UtcNow); await store.Save(ct); }
        catch (Exception ex) { request.Fail(ex.GetType().Name); await store.Save(ct); throw new ConflictError("deletion.failed", "Deletion failed and remains available for retry."); }
    }

    public async Task RevokeConsent(Guid facilityId, Guid visitId, string verificationReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verificationReference)) throw new ConflictError("consent.verification_required", "Verified patient decision reference is required.");
        var visit = await FacilityVisit(facilityId, visitId, ct); var consent = await store.GetActiveConsent(visitId, ct);
        consent?.Revoke(clock.UtcNow); visit.RevokeConsent(clock.UtcNow);
        store.Add(new AuditEvent(facilityId, visitId, "facility-api", $"consent.revoked:{verificationReference}", clock.UtcNow)); await store.Save(ct);
    }

    private async Task<(string LinkToken, string VisitCode, string DeliveryStatus)> CreateInvitationCore(VisitProcess visit, string contact, string interviewInvitationToken, CancellationToken ct)
    {
        var token = credentials.GenerateLinkToken(); var code = credentials.GenerateVisitCode();
        var generation = (await store.GetAccessGrants(visit.Id, ct)).Count + 1;
        var grant = new PatientAccessGrant(visit.Id, credentials.Hash(token), credentials.Hash(code), generation, visit.ServiceExpiresAt, clock.UtcNow); store.Add(grant);
        var attempt = new DeliveryAttempt(visit.Id, grant.Id, visit.ContactChannel.ToString(), protector.Protect(contact), clock.UtcNow); store.Add(attempt);
        // Najpierw utrwalamy wizytę i tokeny; dostawca powiadomień nie może dostać linku do nieistniejącej transakcji.
        await store.Save(ct);
        var sent = await notifications.Send(visit.ContactChannel.ToString(), contact, token, code, interviewInvitationToken, ct);
        if (sent.Delivered) attempt.Delivered(sent.ProviderId); else attempt.Failed(sent.Error ?? "Delivery failed.");
        await store.Save(ct);
        return new(token, code, attempt.Status.ToString());
    }

    private async Task<VisitProcess> FacilityVisit(Guid facilityId, Guid visitId, CancellationToken ct)
    {
        var visit = await store.GetVisit(visitId, ct) ?? throw new NotFoundError();
        if (visit.FacilityId != facilityId) throw new NotFoundError();
        return visit;
    }

    private static VisitDetails Details(VisitProcess visit, AgentInterview interview) => new(visit.Id,
        visit.ExternalVisitId, visit.ScheduledAt, visit.TimeZone, visit.ServiceExpiresAt,
        new(visit.AssignedClinicianId, visit.DoctorName, visit.DoctorSpecialty),
        new(visit.FacilityId, visit.FacilityName, visit.FacilityAddress), visit.Room, visit.VisitType,
        visit.LocationInstructions, VisitStatusName(visit.Status), interview.Id,
        interview.Status == AgentInterviewStatus.InProgress ? "in_progress" : interview.Status.ToString().ToLowerInvariant());
    private static string VisitStatusName(VisitStatus status) => status switch
    {
        VisitStatus.NotStarted => "not_started",
        VisitStatus.InProgress => "in_progress",
        VisitStatus.AwaitingApproval => "awaiting_approval",
        VisitStatus.RequiresSupplementation => "requires_supplementation",
        _ => status.ToString().ToLowerInvariant()
    };
}
