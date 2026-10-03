using System.Text.Json;
using System.Threading.RateLimiting;
using HealthPrep.Domain.Agents;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace HealthPrep.Api.Agents;

public static class AgentEndpoints
{
    public static IServiceCollection AddAgentInterviews(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ElevenLabsOptions>(configuration.GetSection("ElevenLabs"));
        var protection = services.AddDataProtection().SetApplicationName("HealthPrep");
        var keyDirectory = configuration["InterviewAccess:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.AddScoped<InterviewAccess>();
        services.AddScoped<AgentInterviewService>();
        services.AddHttpClient<ElevenLabsProvider>(http =>
        {
            http.BaseAddress = new Uri("https://api.elevenlabs.io/v1/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("interview-links", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapAgentInterviews(this IEndpointRouteBuilder app)
    {
        var facility = app.MapGroup("/api/v1/integration/appointments").WithTags("Agent invitations").AddEndpointFilter<FacilityApiKeyFilter>();
        facility.MapPost("/{visitId:guid}/invitation", async (Guid visitId, HttpContext context, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.CreateInvitation(visitId, (Guid)context.Items["TenantId"]!, ct)));
        facility.MapDelete("/{visitId:guid}/invitation", async (Guid visitId, HttpContext context, AgentInterviewService service, CancellationToken ct) =>
        { await service.RevokeInvitation(visitId, (Guid)context.Items["TenantId"]!, ct); return Results.NoContent(); });

        var publicLinks = app.MapGroup("/api/public/interviews").WithTags("Anonymous interview").RequireRateLimiting("interview-links");
        publicLinks.MapGet("/{token}", async (string token, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(new { interview = await service.GetInfo(await access.FromInvitation(token, ct), ct) }));
        publicLinks.MapPost("/{token}/authorize", async (string token, InterviewAccess access, AgentInterviewService service, HttpContext context, CancellationToken ct) =>
        {
            var scope = await access.FromInvitation(token, ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { accessToken = access.Issue(scope.Invitation!), expiresIn = 3600, interview = await service.GetInfo(scope, ct) });
        });

        app.MapGet("/api/visits/{visitId:guid}/interview", async (Guid visitId, HttpContext context, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.GetVisit(visitId, context, ct))).WithTags("Patient agent interview");
        app.MapGet("/api/interviews/{id:guid}", async (Guid id, HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.GetInfo(await access.Authorize(context, id, ct), ct)));
        app.MapGet("/api/interview", async (HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.GetInfo(await access.Authorize(context, null, ct), ct)));

        app.MapPost("/api/interviews/{id:guid}/sessions", async (Guid id, StartAgentSession request, HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.Start(await access.Authorize(context, id, ct), request.Mode, ct));
        }).RequireRateLimiting("interview-links");
        app.MapPost("/api/interview/sessions", async (StartAgentSession request, HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.Start(await access.Authorize(context, null, ct), request.Mode, ct));
        }).RequireRateLimiting("interview-links");
        app.MapGet("/api/interviews/{id:guid}/result", async (Guid id, HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Result(await access.Authorize(context, id, ct), ct)));
        app.MapGet("/api/interview/result", async (HttpContext context, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
            Results.Ok(await service.Result(await access.Authorize(context, null, ct), ct)));
        app.MapPut("/api/interview-sessions/{id:guid}/provider-conversation", async (Guid id, BindAgentConversation request, HttpContext context, HealthPrepDbContext db, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
        {
            var session = await AuthorizedSession(id, context, db, access, ct);
            await service.Bind(session, request.ConversationId, ct);
            return Results.NoContent();
        });
        app.MapPost("/api/interview-sessions/{id:guid}/end", async (Guid id, EndAgentSession request, HttpContext context, HealthPrepDbContext db, InterviewAccess access, AgentInterviewService service, CancellationToken ct) =>
        {
            var session = await AuthorizedSession(id, context, db, access, ct);
            await service.End(session, request.ContinuesInterview, ct);
            return Results.NoContent();
        });
        app.MapPost("/api/webhooks/elevenlabs", Webhook);
        return app;
    }

    private static async Task<AgentSession> AuthorizedSession(Guid id, HttpContext context, HealthPrepDbContext db, InterviewAccess access, CancellationToken ct)
    {
        var session = await db.AgentSessions.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AgentApiException(404, "Session was not found.");
        var scope = await access.Authorize(context, session.InterviewId, ct);
        if (scope.Invitation is not null && session.InvitationId != scope.Invitation.Id)
            throw new AgentApiException(403, "Session access denied.");
        return session;
    }

    private static async Task<IResult> Webhook(HttpContext context, IOptions<ElevenLabsOptions> options, AgentInterviewService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret)) return Results.StatusCode(503);
        const int maxBytes = 2 * 1024 * 1024;
        if (context.Request.ContentLength > maxBytes) return Results.StatusCode(413);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) return Results.StatusCode(413);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var raw = buffer.ToArray();
        if (!ElevenWebhookSignature.Verify(raw, context.Request.Headers["ElevenLabs-Signature"].ToString(), options.Value.WebhookSecret, DateTimeOffset.UtcNow))
            return Results.Unauthorized();
        try
        {
            using var document = JsonDocument.Parse(raw);
            await service.ProcessWebhook(document.RootElement, ct);
            return Results.Ok();
        }
        catch (JsonException) { return Results.BadRequest(); }
    }
}

public sealed record StartAgentSession(string Mode);
public sealed record BindAgentConversation(string ConversationId);
public sealed record EndAgentSession(bool ContinuesInterview = false);
