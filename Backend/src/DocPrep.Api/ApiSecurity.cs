using Microsoft.EntityFrameworkCore;
using Npgsql;
using DocPrep.Application.Common;
using DocPrep.Domain.Tenancy;

namespace DocPrep.Api;

public sealed class IntegrationOptions
{
    public const string Section = "Integration";
    public List<IntegrationClient> Clients { get; init; } = [];
}

public sealed record IntegrationClient(Guid FacilityId, string Name, FacilityRole Role, string? ClinicianId, string ApiKeySha256);
public sealed record FacilityRequestContext(Guid FacilityId, FacilityRole Role, string? ClinicianId);

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
                UnauthorizedError => (401, ((ApplicationError)ex).Code),
                ForbiddenError => (403, ((ApplicationError)ex).Code),
                ConflictError => (409, ((ApplicationError)ex).Code),
                TooManyRequestsError => (429, ((ApplicationError)ex).Code),
                ServiceUnavailableError => (503, ((ApplicationError)ex).Code),
                DbUpdateConcurrencyException => (409, "persistence.concurrency_conflict"),
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => (409, "persistence.duplicate"),
                Domain.Common.DomainException d => (409, d.Code),
                ArgumentException => (400, "validation.invalid"),
                _ => (500, "server.error")
            };
            if (result.Item1 == 500) logger.LogError(ex, "Unhandled request failure");
            await Results.Problem(statusCode: result.Item1, title: result.Item2,
                detail: result.Item1 == 500 ? "An unexpected error occurred." : ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = result.Item2, ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
