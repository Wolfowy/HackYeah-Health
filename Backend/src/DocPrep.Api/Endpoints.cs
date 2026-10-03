using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Access;
using DocPrep.Application.Contracts;
using DocPrep.Application.Interviews;
using DocPrep.Application.Reports;
using DocPrep.Application.Visits;
using DocPrep.Domain.Tenancy;

namespace DocPrep.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapDocPrepEndpoints(this IEndpointRouteBuilder app)
    {
        var access = app.MapGroup("/api/v1/patient-access").WithTags("Patient access").RequireRateLimiting("access");
        access.MapPost("/link/exchange", async (LinkExchangeRequest x, PatientAccessService service, CancellationToken ct) => Results.Ok(await service.ExchangeLink(x.Token, ct)));
        access.MapPost("/code/exchange", async (CodeExchangeRequest x, PatientAccessService service, CancellationToken ct) => Results.Ok(await service.ExchangeCode(x.Code, ct)));

        var patient = app.MapGroup("/api/v1/interview").WithTags("Patient interview").AddEndpointFilter<PatientSessionFilter>();
        patient.MapGet("/", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) => Results.Ok(await service.Get(PatientVisit(ctx), ct)));
        patient.MapPost("/answers", async (SubmitAnswerCommand x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => Results.Ok(new { nextQuestion = await service.SubmitAnswer(PatientVisit(ctx), x, ct) }));
        patient.MapPost("/voice/transcribe", async (IFormFile audio, ITranscriptionService transcription, CancellationToken ct) =>
        {
            if (audio.Length == 0 || audio.Length > 15_000_000) return Results.BadRequest(new { code = "audio.invalid_size" });
            await using var stream = audio.OpenReadStream(); return Results.Ok(new { text = await transcription.Transcribe(stream, audio.ContentType, ct) });
        }).DisableAntiforgery();
        patient.MapPut("/draft", async (ReplaceDraftCommand x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => Results.Ok(await service.ReplaceDraft(PatientVisit(ctx), x, ct)));
        patient.MapPut("/observations/{id:guid}/decision", async (Guid id, ObservationDecisionCommand x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => Results.Ok(await service.DecideObservation(PatientVisit(ctx), id, x, ct)));
        patient.MapPost("/complete", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { await service.Complete(PatientVisit(ctx), ct); return Results.NoContent(); });
        patient.MapPost("/approve", async (ApproveReportCommand x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => Results.Ok(await service.Approve(PatientVisit(ctx), x, ct)));
        patient.MapPut("/consent", async (ConsentCommand x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { await service.SetConsent(PatientVisit(ctx), x, ct); return Results.NoContent(); });
        patient.MapGet("/report", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { var report = await service.GetLatestReport(PatientVisit(ctx), ct); return Results.Content(report.Json, "application/json"); });
        patient.MapGet("/report.pdf", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { var report = await service.GetLatestReport(PatientVisit(ctx), ct); return Results.File(report.Pdf, "application/pdf", $"DocPrep-v{report.Version}.pdf"); });
        patient.MapPost("/report/regenerate-pdf", async (HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { await service.RegeneratePdf(PatientVisit(ctx), ct); return Results.NoContent(); });
        patient.MapPost("/supplementation-round/answers", async (IReadOnlyList<SupplementationAnswerCommand> x, HttpContext ctx, PatientInterviewService service, CancellationToken ct) => { await service.AnswerSupplementation(PatientVisit(ctx), x, ct); return Results.NoContent(); });

        var integration = app.MapGroup("/api/v1/integration").WithTags("Facility integration").AddEndpointFilter<FacilityApiKeyFilter>();
        integration.MapPost("/visits", async (CreateVisitRequest x, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            var facility = Facility(ctx, FacilityRole.Administrative, FacilityRole.System);
            var result = await service.CreateVisit(new(facility.FacilityId, x.ExternalVisitId, x.Pesel, x.ScheduledAt, x.ServiceExpiresAt, x.Contact, x.Channel, x.AssignedClinicianId), ct);
            return Results.Created($"/api/v1/integration/visits/{result.VisitId}/status", result);
        });
        integration.MapPost("/visits/{id:guid}/invitations", async (Guid id, InvitationRequest x, HttpContext ctx, IntegrationService service, CancellationToken ct) => Results.Ok(await service.RegenerateInvitation(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, x.Contact, ct)));
        integration.MapGet("/visits", async (HttpContext ctx, IntegrationService service, CancellationToken ct) => Results.Ok(await service.Dashboard(Facility(ctx, FacilityRole.Administrative, FacilityRole.System, FacilityRole.Clinician).FacilityId, ct)));
        integration.MapGet("/visits/{id:guid}/status", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) => Results.Ok((await service.Dashboard(Facility(ctx, FacilityRole.Administrative, FacilityRole.System, FacilityRole.Clinician).FacilityId, ct)).SingleOrDefault(x => x.VisitId == id) ?? throw new Application.Common.NotFoundError()));
        integration.MapPost("/visits/{id:guid}/cancel", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) => { await service.Cancel(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, ct); return Results.NoContent(); });
        integration.MapPost("/patients/deletion-requests", async (DeletionRequestContract x, HttpContext ctx, IntegrationService service, CancellationToken ct) => Results.Accepted(value: new { requestId = await service.DeleteByPesel(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, x.Pesel, x.VerificationReference, ct) }));
        integration.MapGet("/deletion-requests/{id:guid}", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) => Results.Ok(await service.DeletionStatus(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, ct)));
        integration.MapPost("/visits/{id:guid}/consent-revocations", async (Guid id, ConsentRevocationRequest x, HttpContext ctx, IntegrationService service, CancellationToken ct) => { await service.RevokeConsent(Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId, id, x.VerificationReference, ct); return Results.NoContent(); });
        integration.MapGet("/visits/{id:guid}/report-versions", async (Guid id, HttpContext ctx, FacilityReportService service, CancellationToken ct) => Results.Ok(await service.Versions(Facility(ctx, FacilityRole.Clinician, FacilityRole.System).FacilityId, id, ct)));
        integration.MapGet("/visits/{id:guid}/report-versions/{versionId:guid}", async (Guid id, Guid versionId, HttpContext ctx, FacilityReportService service, CancellationToken ct) => Results.Ok(await service.Get(Facility(ctx, FacilityRole.Clinician, FacilityRole.System).FacilityId, id, versionId, ct)));
        integration.MapGet("/visits/{id:guid}/report-versions/{versionId:guid}/pdf", async (Guid id, Guid versionId, HttpContext ctx, FacilityReportService service, CancellationToken ct) => Results.File(await service.Pdf(Facility(ctx, FacilityRole.Clinician, FacilityRole.System).FacilityId, id, versionId, ct), "application/pdf", $"DocPrep-{versionId}.pdf"));
        integration.MapPost("/visits/{id:guid}/supplementation-round/questions", async (Guid id, QuestionsRequest x, HttpContext ctx, FacilityReportService service, CancellationToken ct) =>
        { var facility = Facility(ctx, FacilityRole.Clinician, FacilityRole.System); await service.AddQuestions(facility.FacilityId, id, new(facility.ClinicianId ?? x.ClinicianId ?? "system", x.Questions), ct); return Results.Accepted(); });
        return app;
    }

    private static Guid PatientVisit(HttpContext ctx) => (Guid)ctx.Items["PatientVisitId"]!;
    private static FacilityRequestContext Facility(HttpContext ctx, params FacilityRole[] roles)
    { var facility = (FacilityRequestContext)ctx.Items["Facility"]!; if (!roles.Contains(facility.Role)) throw new Application.Common.ForbiddenError(); return facility; }
}

public sealed record LinkExchangeRequest(string Token);
public sealed record CodeExchangeRequest(string Code);
public sealed record CreateVisitRequest(string ExternalVisitId, string Pesel, DateTimeOffset ScheduledAt, DateTimeOffset ServiceExpiresAt, string Contact, Domain.Visits.ContactChannel Channel, string? AssignedClinicianId);
public sealed record InvitationRequest(string Contact);
public sealed record DeletionRequestContract(string Pesel, string VerificationReference);
public sealed record ConsentRevocationRequest(string VerificationReference);
public sealed record QuestionsRequest(string? ClinicianId, IReadOnlyList<string> Questions);
