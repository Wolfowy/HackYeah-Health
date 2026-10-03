using System.Security.Cryptography;
using System.Text;
using HealthPrep.Application.Appointments;
using HealthPrep.Domain.Appointments;
using Microsoft.Extensions.Options;

namespace HealthPrep.Api;

public sealed class IntegrationOptions { public const string Section = "Integration"; public List<IntegrationClient> Clients { get; init; } = []; }
public sealed record IntegrationClient(Guid TenantId, string Name, string ApiKeySha256);

public sealed class FacilityApiKeyFilter(IOptions<IntegrationOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var supplied)) return Results.Unauthorized();
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        var client = options.Value.Clients.FirstOrDefault(x => TryMatches(x.ApiKeySha256, suppliedHash));
        if (client is null) return Results.Unauthorized();
        context.HttpContext.Items["TenantId"] = client.TenantId;
        return await next(context);
    }
    private static bool TryMatches(string configuredHex, byte[] suppliedHash)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(configuredHex), suppliedHash); }
        catch (FormatException) { return false; }
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ForbiddenException)
        {
            var status = ex switch { NotFoundException => 404, ForbiddenException => 403, _ => 400 };
            await Results.Problem(statusCode: status, title: ex.GetType().Name.Replace("Exception", ""), detail: ex.Message).ExecuteAsync(context);
        }
    }
}
