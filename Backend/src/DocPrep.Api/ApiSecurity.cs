using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Domain.Tenancy;

namespace DocPrep.Api;

public sealed class IntegrationOptions { public const string Section = "Integration"; public List<IntegrationClient> Clients { get; init; } = []; }
public sealed record IntegrationClient(Guid FacilityId, string Name, FacilityRole Role, string? ClinicianId, string ApiKeySha256);
public sealed record FacilityRequestContext(Guid FacilityId, FacilityRole Role, string? ClinicianId);

public sealed class FacilityApiKeyFilter(IOptions<IntegrationOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var value)) return Results.Unauthorized();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()));
        var client = options.Value.Clients.FirstOrDefault(x => Matches(x.ApiKeySha256, hash)); if (client is null) return Results.Unauthorized();
        context.HttpContext.Items["Facility"] = new FacilityRequestContext(client.FacilityId, client.Role, client.ClinicianId); return await next(context);
    }
    private static bool Matches(string configured, byte[] supplied) { try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(configured), supplied); } catch { return false; } }
}

public sealed class PatientSessionFilter(IPatientSessionStore sessions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
        var visitId = await sessions.Resolve(authorization[7..].Trim(), context.HttpContext.RequestAborted); if (visitId is null) return Results.Unauthorized();
        context.HttpContext.Items["PatientVisitId"] = visitId.Value; return await next(context);
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var result = ex switch
            {
                NotFoundError => (404, ((ApplicationError)ex).Code),
                ForbiddenError => (403, ((ApplicationError)ex).Code),
                ConflictError => (409, ((ApplicationError)ex).Code),
                Domain.Common.DomainException d => (409, d.Code),
                ArgumentException => (400, "validation.invalid"),
                _ => (500, "server.error")
            };
            if (result.Item1 == 500) logger.LogError(ex, "Unhandled request failure");
            await Results.Problem(statusCode: result.Item1, title: result.Item2, detail: result.Item1 == 500 ? "An unexpected error occurred." : ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = result.Item2, ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
