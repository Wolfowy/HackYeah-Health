using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using DocPrep.Api;
using DocPrep.Application.Access;
using DocPrep.Application.Interviews;
using DocPrep.Application.Reports;
using DocPrep.Application.Visits;
using DocPrep.Infrastructure;
using DocPrep.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDocPrepInfrastructure(builder.Configuration);
builder.Services.AddScoped<PatientAccessService>(); builder.Services.AddScoped<PatientInterviewService>();
builder.Services.AddScoped<IntegrationService>(); builder.Services.AddScoped<FacilityReportService>();
builder.Services.AddScoped<FacilityApiKeyFilter>(); builder.Services.AddScoped<PatientSessionFilter>();
builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection(IntegrationOptions.Section));
builder.Services.ConfigureHttpJsonOptions(x => x.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(); builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRateLimiter(x => x.AddPolicy("access", context => RateLimitPartition.GetFixedWindowLimiter(
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddCors(x => x.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSwaggerGen(x =>
{
    x.SwaggerDoc("v1", new OpenApiInfo { Title = "DocPrep API", Version = "v1", Description = "Przygotowanie pacjenta do wizyty. System nie diagnozuje, nie zaleca leczenia i nie ocenia pilności." });
    x.AddSecurityDefinition("FacilityApiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Api-Key" });
    x.AddSecurityDefinition("PatientSession", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "opaque" });
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>(); app.UseRateLimiter(); app.UseCors();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (DocPrepDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).ExcludeFromDescription();
app.MapDocPrepEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DocPrepDbContext>().Database.MigrateAsync();
}
await app.RunAsync();
public partial class Program;
