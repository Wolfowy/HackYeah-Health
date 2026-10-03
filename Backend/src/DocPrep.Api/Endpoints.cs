using System.Security.Claims;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Access;
using DocPrep.Application.Common;
using DocPrep.Application.Contracts;
using DocPrep.Application.Interviews;
using DocPrep.Application.Reports;
using DocPrep.Application.Visits;
using DocPrep.Domain.Tenancy;
using DocPrep.Domain.Interviews;
using DocPrep.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocPrep.Api;

public static class Endpoints
{
    private static readonly HashSet<string> AllowedAudioTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/mp4", "audio/wav", "audio/x-wav", "audio/webm", "audio/ogg"
    };

    public static IEndpointRouteBuilder MapDocPrepEndpoints(this IEndpointRouteBuilder app)
    {
        MapAuthentication(app);
        MapElevenLabsInterviews(app);

        var access = app.MapGroup("/api/v1/patient-access")
            .WithTags("Patient access")
            .RequireRateLimiting("access");
        access.MapPost("/link/exchange", async (LinkExchangeRequest request, PatientAccessService service, CancellationToken ct) =>
        {
            Required(request.Token, nameof(request.Token), 500);
            return Results.Ok(await service.ExchangeLink(request.Token, ct));
        }).Produces<ExchangeAccessResult>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(429);
        access.MapPost("/code/exchange", async (CodeExchangeRequest request, PatientAccessService service, CancellationToken ct) =>
        {
            Required(request.Code, nameof(request.Code), 100);
            return Results.Ok(await service.ExchangeCode(request.Code, ct));
        }).Produces<ExchangeAccessResult>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(429);

        var patient = app.MapGroup("/api/v1/interview")
            .WithTags("Patient interview")
            .RequireAuthorization(AuthenticationSchemes.PatientPolicy);
        patient.MapGet("", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Get(PatientVisit(ctx), ct))).Produces<PatientInterviewView>().ProducesProblem(401).ProducesProblem(404);
        patient.MapPost("/answers", async (SubmitAnswerCommand request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            Required(request.Question, nameof(request.Question), 1000);
            Required(request.Answer, nameof(request.Answer), 8000);
            return Results.Ok(new NextQuestionResponse(await service.SubmitAnswer(PatientVisit(ctx), request, ct)));
        }).Produces<NextQuestionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);
        patient.MapPost("/voice/transcribe", async (IFormFile audio, ITranscriptionService transcription, CancellationToken ct) =>
        {
            if (audio.Length == 0 || audio.Length > 15_000_000) throw new ArgumentException("Audio must contain between 1 byte and 15 MB.");
            if (!AllowedAudioTypes.Contains(audio.ContentType)) throw new ArgumentException("Unsupported audio content type.");
            await using var stream = audio.OpenReadStream();
            return Results.Ok(new TranscriptionResponse(await transcription.Transcribe(stream, audio.ContentType, ct)));
        }).DisableAntiforgery().RequireRateLimiting("voice").Accepts<IFormFile>("multipart/form-data").Produces<TranscriptionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        patient.MapPut("/draft", async (ReplaceDraftCommand request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            ValidateDraft(request);
            return Results.Ok(await service.ReplaceDraft(PatientVisit(ctx), request, ct));
        }).Produces<PatientInterviewView>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);
        patient.MapPut("/observations/{id:guid}/decision", async (Guid id, ObservationDecisionCommand request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.DecideObservation(PatientVisit(ctx), id, request, ct))).Produces<PatientInterviewView>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        patient.MapPost("/complete", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            await service.Complete(PatientVisit(ctx), ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(409);
        patient.MapPost("/approve", async (ApproveReportCommand request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Approve(PatientVisit(ctx), request, ct))).Produces<ReportVersionView>().ProducesProblem(401).ProducesProblem(409);
        patient.MapPut("/consent", async (ConsentCommand request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            await service.SetConsent(PatientVisit(ctx), request, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(409);
        patient.MapGet("/report", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            var report = await service.GetLatestReport(PatientVisit(ctx), ct);
            return Results.Content(report.Json, "application/json");
        }).Produces(200, contentType: "application/json").ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        patient.MapGet("/report.pdf", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            var report = await service.GetLatestReport(PatientVisit(ctx), ct);
            return Results.File(report.Pdf, "application/pdf", $"DocPrep-v{report.Version}.pdf");
        }).Produces(200, contentType: "application/pdf").ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        patient.MapPost("/report/regenerate-pdf", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            await service.RegeneratePdf(PatientVisit(ctx), ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        patient.MapPost("/supplementation-round/answers", async (IReadOnlyList<SupplementationAnswerCommand> request, HttpContext ctx, PatientInterviewService service, CancellationToken ct) =>
        {
            if (request is null || request.Count == 0) throw new ArgumentException("At least one answer is required.");
            foreach (var answer in request) Required(answer.Answer, nameof(answer.Answer), 8000);
            await service.AnswerSupplementation(PatientVisit(ctx), request, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);

        var integration = app.MapGroup("/api/v1/integration")
            .WithTags("Facility integration")
            .RequireAuthorization(AuthenticationSchemes.FacilityPolicy);
        integration.MapPost("/visits", async (CreateVisitRequest request, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            ValidateVisit(request);
            var facility = Facility(ctx, FacilityRole.Administrative, FacilityRole.System);
            var result = await service.CreateVisit(new(facility.FacilityId, request.ExternalVisitId, request.Pesel, request.ScheduledAt, request.ServiceExpiresAt, request.Contact, request.Channel, request.AssignedClinicianId), ct);
            return Results.Created($"/api/v1/integration/visits/{result.VisitId}/status", result);
        }).Produces<InvitationResult>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        integration.MapPost("/visits/{id:guid}/invitations", async (Guid id, InvitationRequest request, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            Required(request.Contact, nameof(request.Contact), 320);
            return Results.Ok(await service.RegenerateInvitation(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, request.Contact, ct));
        }).Produces<InvitationResult>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        integration.MapGet("/visits", async (HttpContext ctx, IntegrationService service, CancellationToken ct) =>
            Results.Ok(await service.Dashboard(Facility(ctx, FacilityRole.Administrative, FacilityRole.System, FacilityRole.Clinician).FacilityId, ct))).Produces<IReadOnlyList<AdminVisitView>>().ProducesProblem(401).ProducesProblem(403);
        integration.MapGet("/visits/{id:guid}/status", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
            Results.Ok((await service.Dashboard(Facility(ctx, FacilityRole.Administrative, FacilityRole.System, FacilityRole.Clinician).FacilityId, ct)).SingleOrDefault(x => x.VisitId == id) ?? throw new NotFoundError())).Produces<AdminVisitView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        integration.MapPost("/visits/{id:guid}/cancel", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            await service.Cancel(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        integration.MapPost("/patients/deletion-requests", async (DeletionRequestContract request, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            Required(request.Pesel, nameof(request.Pesel), 20);
            Required(request.VerificationReference, nameof(request.VerificationReference), 500);
            return Results.Accepted(value: new DeletionRequestResponse(await service.DeleteByPesel(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, request.Pesel, request.VerificationReference, ct)));
        }).Produces<DeletionRequestResponse>(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        integration.MapGet("/deletion-requests/{id:guid}", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
            Results.Ok(await service.DeletionStatus(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, ct))).Produces<DeletionRequestView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        integration.MapPost("/visits/{id:guid}/consent-revocations", async (Guid id, ConsentRevocationRequest request, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            Required(request.VerificationReference, nameof(request.VerificationReference), 500);
            await service.RevokeConsent(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, request.VerificationReference, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        integration.MapGet("/visits/{id:guid}/report-versions", async (Guid id, HttpContext ctx, FacilityReportService service, CancellationToken ct) =>
        {
            var facility = Facility(ctx, FacilityRole.Clinician, FacilityRole.System);
            return Results.Ok(await service.Versions(facility.FacilityId, id, facility.ClinicianId, ct));
        }).Produces<IReadOnlyList<ReportVersionView>>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        integration.MapGet("/visits/{id:guid}/report-versions/{versionId:guid}", async (Guid id, Guid versionId, HttpContext ctx, FacilityReportService service, CancellationToken ct) =>
        {
            var facility = Facility(ctx, FacilityRole.Clinician, FacilityRole.System);
            return Results.Ok(await service.Get(facility.FacilityId, id, versionId, facility.ClinicianId, ct));
        }).Produces<ClinicianReportView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        integration.MapGet("/visits/{id:guid}/report-versions/{versionId:guid}/pdf", async (Guid id, Guid versionId, HttpContext ctx, FacilityReportService service, CancellationToken ct) =>
        {
            var facility = Facility(ctx, FacilityRole.Clinician, FacilityRole.System);
            return Results.File(await service.Pdf(facility.FacilityId, id, versionId, facility.ClinicianId, ct), "application/pdf", $"DocPrep-{versionId}.pdf");
        }).Produces(200, contentType: "application/pdf").ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        integration.MapPost("/visits/{id:guid}/supplementation-round/questions", async (Guid id, QuestionsRequest request, HttpContext ctx, FacilityReportService service, CancellationToken ct) =>
        {
            ValidateQuestions(request);
            var facility = Facility(ctx, FacilityRole.Clinician, FacilityRole.System);
            var clinicianId = facility.ClinicianId ?? request.ClinicianId ?? throw new ForbiddenError("A clinician identity is required.");
            await service.AddQuestions(facility.FacilityId, id, new(clinicianId, request.Questions), ct);
            return Results.Accepted();
        }).Produces(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        return app;
    }

    private static void MapElevenLabsInterviews(IEndpointRouteBuilder app)
    {
        var visits = app.MapGroup("/api/visits").WithTags("ElevenLabs interviews");
        visits.MapGet("/{visitId:guid}/interview", async (Guid visitId, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
        {
            if (PatientVisit(ctx) != visitId) throw new ForbiddenError();
            return Results.Ok(await service.GetByVisit(visitId, ct));
        }).RequireAuthorization(AuthenticationSchemes.PatientPolicy).Produces<AgentInterviewView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        var interviews = app.MapGroup("/api/interviews").WithTags("ElevenLabs interviews").RequireAuthorization(AuthenticationSchemes.InterviewPolicy);
        interviews.MapPost("/{interviewId:guid}/sessions", async (Guid interviewId, CreateAgentSessionRequest request, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.CreateSession(interviewId, request.Mode, InterviewAccess(ctx), ct)))
            .RequireRateLimiting("public-interview").Produces<AgentSessionResult>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        interviews.MapGet("/{interviewId:guid}", async (Guid interviewId, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Get(interviewId, InterviewAccess(ctx), ct))).Produces<AgentInterviewView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        interviews.MapGet("/{interviewId:guid}/result", async (Guid interviewId, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Result(interviewId, InterviewAccess(ctx), ct))).Produces<AgentInterviewResultView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        app.MapPut("/api/interview-sessions/{sessionId:guid}/provider-conversation", async (Guid sessionId, ProviderConversationRequest request, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
        {
            Required(request.ConversationId, nameof(request.ConversationId), 200);
            await service.SetProviderConversation(sessionId, request.ConversationId, InterviewAccess(ctx), ct);
            return Results.NoContent();
        }).WithTags("ElevenLabs interviews").RequireAuthorization(AuthenticationSchemes.InterviewPolicy)
            .Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        app.MapPost("/api/interview-sessions/{sessionId:guid}/end", async (Guid sessionId, EndAgentSessionRequest request, HttpContext ctx, ElevenLabsInterviewService service, CancellationToken ct) =>
        {
            await service.EndSession(sessionId, request.ContinuesInterview, InterviewAccess(ctx), ct);
            return Results.NoContent();
        }).WithTags("ElevenLabs interviews").RequireAuthorization(AuthenticationSchemes.InterviewPolicy)
            .Produces(204).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        var publicInterviews = app.MapGroup("/api/public/interviews").WithTags("Public ElevenLabs interviews").RequireRateLimiting("public-interview");
        publicInterviews.MapGet("/{invitationToken}", async (string invitationToken, ElevenLabsInterviewService service, CancellationToken ct) =>
            Results.Ok(new PublicInterviewResponse(await service.PublicView(invitationToken, ct))))
            .Produces<PublicInterviewResponse>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        publicInterviews.MapPost("/{invitationToken}/authorize", async (string invitationToken, ElevenLabsInterviewService service, AnonymousInterviewTokenService tokens, CancellationToken ct) =>
        {
            var access = await service.AuthorizeInvitation(invitationToken, ct);
            return Results.Ok(tokens.Issue(access.InterviewId, access.InvitationId, access.ExpiresAt));
        }).Produces<AnonymousInterviewTokenResponse>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        publicInterviews.MapPost("/{invitationToken}/sessions", async (string invitationToken, CreateAgentSessionRequest request, ElevenLabsInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.CreatePublicSession(invitationToken, request.Mode, ct)))
            .Produces<AgentSessionResult>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(429).ProducesProblem(503);

        app.MapPost("/api/webhooks/elevenlabs", async (HttpRequest request, IElevenLabsWebhookVerifier verifier,
            ElevenLabsInterviewService service, IOptions<ElevenLabsOptions> options, IClock clock, CancellationToken ct) =>
        {
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            var rawBody = buffer.ToArray();
            if (!verifier.IsValid(rawBody, request.Headers["ElevenLabs-Signature"].ToString(), clock.UtcNow))
                return Results.Problem(statusCode: 401, title: "webhook.invalid_signature", extensions: new Dictionary<string, object?> { ["code"] = "webhook.invalid_signature" });
            using var payload = JsonDocument.Parse(rawBody);
            await service.ProcessWebhook(payload.RootElement, ElevenLabsInterviewService.PayloadHash(rawBody), options.Value.AgentId, ct);
            return Results.Ok(new { status = "received" });
        }).WithTags("ElevenLabs webhook").WithMetadata(new RequestSizeLimitAttribute(1_048_576))
            .Produces(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }

    private static void MapAuthentication(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Staff authentication");
        auth.MapPost("/login", async (LoginRequest request, StaffAuthenticationService service, CancellationToken ct) =>
            Results.Ok(await service.Login(request, ct))).RequireRateLimiting("authentication").Produces<TokenResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        auth.MapPost("/refresh", async (RefreshTokenRequest request, StaffAuthenticationService service, CancellationToken ct) =>
            Results.Ok(await service.Refresh(request, ct))).RequireRateLimiting("authentication").Produces<TokenResponse>().ProducesProblem(401).ProducesProblem(429);
        auth.MapPost("/logout", async (LogoutRequest request, HttpContext ctx, StaffAuthenticationService service, CancellationToken ct) =>
        {
            await service.Logout(StaffUserId(ctx), request, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthenticationSchemes.StaffPolicy).Produces(204).ProducesProblem(401);
        auth.MapGet("/me", async (HttpContext ctx, StaffAuthenticationService service, CancellationToken ct) =>
            Results.Ok(await service.Me(StaffUserId(ctx), ct))).RequireAuthorization(AuthenticationSchemes.StaffPolicy).Produces<AuthenticatedUserView>().ProducesProblem(401);
    }

    private static Guid PatientVisit(HttpContext ctx) => Guid.Parse(ctx.User.FindFirstValue(DocPrepClaims.VisitId) ?? throw new UnauthorizedError());
    private static Guid StaffUserId(HttpContext ctx) => Guid.Parse(ctx.User.FindFirstValue("sub") ?? throw new UnauthorizedError());
    private static AgentInterviewAccess InterviewAccess(HttpContext ctx)
    {
        if (Guid.TryParse(ctx.User.FindFirstValue(DocPrepClaims.VisitId), out var visitId)) return AgentInterviewAccess.ForVisit(visitId);
        if (!string.Equals(ctx.User.FindFirstValue(DocPrepClaims.Scope), "interview:read interview:execute", StringComparison.Ordinal)) throw new ForbiddenError();
        if (!Guid.TryParse(ctx.User.FindFirstValue(DocPrepClaims.InterviewId), out var interviewId) ||
            !Guid.TryParse(ctx.User.FindFirstValue(DocPrepClaims.InvitationId), out var invitationId)) throw new ForbiddenError();
        return AgentInterviewAccess.ForInvitation(interviewId, invitationId);
    }
    private static FacilityRequestContext Facility(HttpContext ctx, params FacilityRole[] roles)
    {
        if (!Guid.TryParse(ctx.User.FindFirstValue(DocPrepClaims.FacilityId), out var facilityId)) throw new UnauthorizedError();
        if (!Enum.TryParse<FacilityRole>(ctx.User.FindFirstValue("role"), out var role) || !roles.Contains(role)) throw new ForbiddenError();
        return new(facilityId, role, ctx.User.FindFirstValue(DocPrepClaims.ClinicianId));
    }

    private static void ValidateVisit(CreateVisitRequest request)
    {
        Required(request.ExternalVisitId, nameof(request.ExternalVisitId), 100);
        Required(request.Pesel, nameof(request.Pesel), 20);
        Required(request.Contact, nameof(request.Contact), 320);
        if (request.ScheduledAt == default || request.ServiceExpiresAt == default) throw new ArgumentException("Visit dates are required.");
    }

    private static void ValidateDraft(ReplaceDraftCommand request)
    {
        Required(request.ConsultationReason, nameof(request.ConsultationReason), 2000);
        if (request.Symptoms is null || request.Medications is null || request.Allergies is null || request.ChronicConditions is null || request.Questions is null)
            throw new ArgumentException("Draft collections cannot be null.");
        foreach (var symptom in request.Symptoms)
        {
            Required(symptom.Name, "symptoms.name", 200);
            if (symptom.Severity is < 0 or > 10) throw new ArgumentException("Symptom severity must be between 0 and 10.");
            if (symptom.Timeline is null) throw new ArgumentException("Symptom timeline cannot be null.");
        }
        foreach (var medication in request.Medications) Required(medication.Name, "medications.name", 200);
        foreach (var allergy in request.Allergies) Required(allergy.Substance, "allergies.substance", 200);
        foreach (var condition in request.ChronicConditions) Required(condition.Name, "chronicConditions.name", 200);
        foreach (var question in request.Questions) Required(question, "questions", 1000);
    }

    private static void ValidateQuestions(QuestionsRequest request)
    {
        if (request.Questions is null || request.Questions.Count == 0 || request.Questions.Count > 20)
            throw new ArgumentException("Between 1 and 20 questions are required.");
        foreach (var question in request.Questions) Required(question, nameof(request.Questions), 1000);
    }

    private static string Required(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{field} is required.");
        if (value.Trim().Length > max) throw new ArgumentException($"{field} exceeds {max} characters.");
        return value.Trim();
    }
}

public sealed record LinkExchangeRequest(string Token);
public sealed record CodeExchangeRequest(string Code);
public sealed record NextQuestionResponse(string? NextQuestion);
public sealed record TranscriptionResponse(string Text);
public sealed record CreateVisitRequest(string ExternalVisitId, string Pesel, DateTimeOffset ScheduledAt, DateTimeOffset ServiceExpiresAt, string Contact, Domain.Visits.ContactChannel Channel, string? AssignedClinicianId);
public sealed record InvitationRequest(string Contact);
public sealed record DeletionRequestContract(string Pesel, string VerificationReference);
public sealed record DeletionRequestResponse(Guid RequestId);
public sealed record ConsentRevocationRequest(string VerificationReference);
public sealed record QuestionsRequest(string? ClinicianId, IReadOnlyList<string> Questions);
public sealed record CreateAgentSessionRequest(InterviewSessionMode Mode);
public sealed record EndAgentSessionRequest(bool ContinuesInterview = false);
public sealed record ProviderConversationRequest(string ConversationId);
public sealed record PublicInterviewResponse(AgentInterviewView Interview);
