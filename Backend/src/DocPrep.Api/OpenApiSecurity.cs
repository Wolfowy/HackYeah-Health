using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocPrep.Api;

public sealed class SecurityRequirementsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var policies = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Select(x => x.Policy)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        if (policies.Contains(AuthenticationSchemes.PatientPolicy))
        {
            operation.Security = [Requirement("PatientSession")];
        }
        else if (policies.Contains(AuthenticationSchemes.StaffPolicy))
        {
            operation.Security = [Requirement("StaffJwt")];
        }
        else if (policies.Contains(AuthenticationSchemes.InterviewPolicy))
        {
            operation.Security = [Requirement("PatientSession"), Requirement("AnonymousInterviewJwt")];
        }
        else if (policies.Contains(AuthenticationSchemes.FacilityPolicy))
        {
            operation.Security = [Requirement("StaffJwt"), Requirement("FacilityApiKey")];
        }
    }

    private static OpenApiSecurityRequirement Requirement(string id) => new()
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id } }] = []
    };
}
