using System.Text.Json.Serialization;
using HealthPrep.Api;
using HealthPrep.Application.Appointments;
using HealthPrep.Infrastructure;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<InterviewService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(x => x.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection(IntegrationOptions.Section));
builder.Services.AddScoped<FacilityApiKeyFilter>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "HealthPrep API", Version = "v1", Description = "Visit-preparation API. It does not diagnose, triage, or recommend treatment." });
    options.AddSecurityDefinition("FacilityApiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Api-Key" });
    options.AddSecurityDefinition("PatientId", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Patient-Id", Description = "MVP identity bridge; replace with OIDC subject in production." });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapHealthPrepEndpoints();

if (app.Configuration.GetValue("Database:Initialize", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.RunAsync();
public partial class Program;
