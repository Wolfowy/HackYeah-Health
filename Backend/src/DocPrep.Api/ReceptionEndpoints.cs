using DocPrep.Application.Contracts;
using DocPrep.Application.Visits;
using DocPrep.Domain.Tenancy;
using DocPrep.Domain.Visits;
using DocPrep.Infrastructure.Services;

namespace DocPrep.Api;

public static partial class Endpoints
{
    private static void MapReception(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin").WithTags("Reception")
            .RequireAuthorization(AuthenticationSchemes.FacilityPolicy);
        admin.AddEndpointFilter(async (context, next) =>
        {
            _ = ReceptionFacility(context.HttpContext);
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        admin.MapGet("/facility", async (HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Facility(ReceptionFacility(ctx), ct))).Produces<VisitFacility>();
        admin.MapGet("/doctors", async (bool? includeInactive, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Doctors(ReceptionFacility(ctx), includeInactive ?? false, ct))).Produces<IReadOnlyList<ClinicianView>>();
        admin.MapPost("/doctors", async (CreateClinicianCommand request, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
        {
            var doctor = await service.CreateDoctor(ReceptionFacility(ctx), request, ct);
            return Results.Created($"/api/v1/admin/doctors/{Uri.EscapeDataString(doctor.Id)}", doctor);
        }).Produces<ClinicianView>(201).ProducesProblem(400).ProducesProblem(409);
        admin.MapPut("/doctors/{id}", async (string id, UpdateClinicianCommand request, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateDoctor(ReceptionFacility(ctx), id, request, ct))).Produces<ClinicianView>().ProducesProblem(404);
        admin.MapGet("/appointments", async (DateTimeOffset? from, DateTimeOffset? to, string? doctorId, string? room,
            VisitStatus? status, string? q, int? page, int? pageSize, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Search(ReceptionFacility(ctx), from, to, doctorId, room, status, q,
                page ?? 1, pageSize ?? 25, ct))).Produces<PagedResult<AppointmentView>>();
        admin.MapGet("/calendar", async (DateTimeOffset from, DateTimeOffset to, string? doctorId, string? room,
            HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Calendar(ReceptionFacility(ctx), from, to, doctorId, room, ct))).Produces<IReadOnlyList<AppointmentView>>().ProducesProblem(400);
        admin.MapPost("/appointments", async (CreateAppointmentCommand request, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
        {
            var result = await service.Create(ReceptionFacility(ctx), request, ct);
            return Results.Created($"/api/v1/admin/appointments/{result.Appointment.VisitId}", result);
        }).Produces<CreatedAppointmentView>(201).ProducesProblem(400).ProducesProblem(409);
        admin.MapGet("/appointments/{id:guid}", async (Guid id, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Get(ReceptionFacility(ctx), id, ct))).Produces<AppointmentView>().ProducesProblem(404);
        admin.MapPut("/appointments/{id:guid}", async (Guid id, UpdateAppointmentCommand request, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Update(ReceptionFacility(ctx), id, request, ct))).Produces<AppointmentView>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        admin.MapPost("/appointments/{id:guid}/cancel", async (Guid id, HttpContext ctx, IntegrationService service, CancellationToken ct) =>
        {
            await service.Cancel(ReceptionFacility(ctx), id, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(404);
        admin.MapGet("/appointments/{id:guid}/invitation", async (Guid id, HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.Invitation(ReceptionFacility(ctx), id, ct))).Produces<InvitationLinkView>().ProducesProblem(404).ProducesProblem(409);
        admin.MapPost("/appointments/{id:guid}/invitation/regenerate", async (Guid id, SendAppointmentInvitationCommand request,
            HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.RegenerateInvitation(ReceptionFacility(ctx), id, request.Channel, ct))).Produces<InvitationLinkView>().ProducesProblem(404).ProducesProblem(409);
        admin.MapPost("/appointments/{id:guid}/invitation/send", async (Guid id, SendAppointmentInvitationCommand request,
            HttpContext ctx, ReceptionService service, CancellationToken ct) =>
            Results.Ok(await service.SendInvitation(ReceptionFacility(ctx), id, request.Channel, ct))).Produces<AppointmentView>().ProducesProblem(404).ProducesProblem(409);
    }

    private static Guid ReceptionFacility(HttpContext ctx) => Facility(ctx, FacilityRole.Administrative, FacilityRole.System).FacilityId;
}
