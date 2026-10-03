using System.Security.Cryptography;
using System.Text.Json;
using HealthPrep.Domain.Agents;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HealthPrep.Api.Agents;

public sealed record AnonymousInterviewAccess(Guid InterviewId, Guid InvitationId, DateTimeOffset ExpiresAt);
public sealed record InterviewScope(AgentInterview Interview, InterviewInvitation? Invitation);

// A protected, opaque bearer token. It is scoped to one invitation and never stored by the browser.
public sealed class InterviewAccess(IDataProtectionProvider protection, HealthPrepDbContext db, IWebHostEnvironment environment)
{
    private readonly IDataProtector protector = protection.CreateProtector("HealthPrep.AnonymousInterview.v1");

    public string Issue(InterviewInvitation invitation)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        if (invitation.ExpiresAt < expires) expires = invitation.ExpiresAt;
        return protector.Protect(JsonSerializer.Serialize(new AnonymousInterviewAccess(invitation.InterviewId, invitation.Id, expires)));
    }

    public async Task<InterviewScope> FromInvitation(string token, CancellationToken ct)
    {
        if (token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new AgentApiException(404, "Invitation was not found.");
        var hash = InterviewInvitation.Hash(token);
        var invitation = await db.InterviewInvitations.SingleOrDefaultAsync(x => x.TokenHash == hash, ct)
            ?? throw new AgentApiException(404, "Invitation was not found.");
        if (!invitation.CanAccess(DateTimeOffset.UtcNow)) throw new AgentApiException(410, "Invitation has expired or was revoked.");
        var interview = await db.AgentInterviews.SingleAsync(x => x.Id == invitation.InterviewId, ct);
        if (interview.Status == "completed") throw new AgentApiException(410, "Interview is already completed.");
        if (!invitation.CanStart(DateTimeOffset.UtcNow)) throw new AgentApiException(429, "Invitation session limit reached.");
        return new(interview, invitation);
    }

    public async Task<InterviewScope> Authorize(HttpContext context, Guid? expectedInterview, CancellationToken ct)
    {
        if (context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
        {
            AnonymousInterviewAccess token;
            try
            {
                token = JsonSerializer.Deserialize<AnonymousInterviewAccess>(
                    protector.Unprotect(context.Request.Headers.Authorization.ToString()[7..]))
                    ?? throw new CryptographicException();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
            { throw new AgentApiException(401, "Interview session is invalid."); }
            if (token.ExpiresAt <= DateTimeOffset.UtcNow || (expectedInterview.HasValue && token.InterviewId != expectedInterview))
                throw new AgentApiException(403, "Interview access denied.");
            var invitation = await db.InterviewInvitations.SingleOrDefaultAsync(x => x.Id == token.InvitationId && x.InterviewId == token.InterviewId, ct);
            if (invitation is null || !invitation.CanAccess(DateTimeOffset.UtcNow))
                throw new AgentApiException(410, "Invitation is no longer active.");
            return new(await db.AgentInterviews.SingleAsync(x => x.Id == token.InterviewId, ct), invitation);
        }
        if (!expectedInterview.HasValue) throw new AgentApiException(401, "An interview session is required.");
        var interview = await db.AgentInterviews.SingleOrDefaultAsync(x => x.Id == expectedInterview, ct)
            ?? throw new AgentApiException(404, "Interview was not found.");
        var patient = PatientId(context);
        if (!await db.Appointments.AnyAsync(x => x.Id == interview.AppointmentId && x.ExternalPatientId == patient, ct))
            throw new AgentApiException(403, "Interview access denied.");
        return new(interview, null);
    }

    public string PatientId(HttpContext context)
    {
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        var id = authenticated ? context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
        // This matches the existing MVP identity bridge, restricted to Development for new endpoints.
        if (id is null && environment.IsDevelopment()) id = context.Request.Headers["X-Patient-Id"].ToString();
        return !string.IsNullOrWhiteSpace(id) ? id : throw new AgentApiException(401, "Patient authentication is required.");
    }
}
