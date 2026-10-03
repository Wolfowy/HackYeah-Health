using HealthPrep.Application.Abstractions;
using HealthPrep.Application.Appointments;

namespace HealthPrep.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapHealthPrepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow })).ExcludeFromDescription();
        var patient = app.MapGroup("/api/v1/patient").WithTags("Patient");
        patient.MapGet("/interviews/by-token/{token}", async (string token, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.GetByLinkToken(token, PatientId(ctx), ct)));
        patient.MapGet("/appointments", async (HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.PatientHistory(PatientId(ctx), ct)));
        patient.MapGet("/appointments/{id:guid}", async (Guid id, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.GetForPatient(id, PatientId(ctx), ct)));
        patient.MapPost("/appointments/{id:guid}/answers", async (Guid id, SubmitAnswer command, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(new { nextQuestion = await service.SubmitAnswer(id, PatientId(ctx), command, ct) }));
        patient.MapPut("/appointments/{id:guid}/summary", async (Guid id, UpdateSummary command, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.Update(id, PatientId(ctx), command, ct)));
        patient.MapPost("/appointments/{id:guid}/approve", async (Guid id, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.Approve(id, PatientId(ctx), ct)));
        patient.MapPut("/appointments/{id:guid}/consent", async (Guid id, ConsentRequest request, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.SetConsent(id, PatientId(ctx), request.Granted, ct)));
        patient.MapGet("/appointments/{id:guid}/report.pdf", async (Guid id, HttpContext ctx, InterviewService service, IReportPdfRenderer pdf, CancellationToken ct) =>
        {
            var report = await service.GetForPatient(id, PatientId(ctx), ct);
            return Results.File(pdf.Render(report), "application/pdf", $"visit-{id}.pdf");
        });

        var integration = app.MapGroup("/api/v1/integration").WithTags("Facility integration").AddEndpointFilter<FacilityApiKeyFilter>();
        integration.MapPost("/appointments", async (CreateAppointmentRequest request, HttpContext ctx, InterviewService service, CancellationToken ct) =>
        {
            var result = await service.Start(new(TenantId(ctx), request.ExternalAppointmentId, request.ExternalPatientId, request.ScheduledAt, request.ConsultationReason), ct);
            return Results.Created($"/api/v1/integration/appointments/{result.Id}", new { result.Id, result.InterviewLinkToken, interviewUrl = $"/interview/{result.InterviewLinkToken}" });
        });
        integration.MapGet("/appointments", async (HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.FacilityDashboard(TenantId(ctx), ct)));
        integration.MapGet("/appointments/{id:guid}/summary", async (Guid id, HttpContext ctx, InterviewService service, CancellationToken ct) => Results.Ok(await service.GetShared(id, TenantId(ctx), ct)));
        integration.MapGet("/appointments/{id:guid}/report.pdf", async (Guid id, HttpContext ctx, InterviewService service, IReportPdfRenderer pdf, CancellationToken ct) =>
        {
            var report = await service.GetShared(id, TenantId(ctx), ct);
            return Results.File(pdf.Render(report), "application/pdf", $"visit-{id}.pdf");
        });
        integration.MapPost("/appointments/{id:guid}/questions", async (Guid id, ClinicianQuestionRequest request, HttpContext ctx, InterviewService service, CancellationToken ct) =>
        {
            await service.AddClinicianQuestion(id, TenantId(ctx), request.Question, ct);
            return Results.Accepted();
        });
        return app;
    }

    private static string PatientId(HttpContext context) => context.Request.Headers.TryGetValue("X-Patient-Id", out var id) && !string.IsNullOrWhiteSpace(id)
        ? id.ToString() : throw new ForbiddenException("X-Patient-Id is required.");
    private static Guid TenantId(HttpContext context) => (Guid)context.Items["TenantId"]!;
}

public sealed record CreateAppointmentRequest(string ExternalAppointmentId, string ExternalPatientId, DateTimeOffset ScheduledAt, string ConsultationReason);
public sealed record ConsentRequest(bool Granted);
public sealed record ClinicianQuestionRequest(string Question);
