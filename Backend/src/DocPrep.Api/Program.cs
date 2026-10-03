using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DocPrep.Api;
using DocPrep.Application.Access;
using DocPrep.Application.Interviews;
using DocPrep.Application.Reports;
using DocPrep.Application.Visits;
using DocPrep.Domain.Tenancy;
using DocPrep.Infrastructure;
using DocPrep.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDocPrepInfrastructure(builder.Configuration);
builder.Services.AddScoped<PatientAccessService>();
builder.Services.AddScoped<PatientInterviewService>();
builder.Services.AddScoped<ElevenLabsInterviewService>();
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddScoped<FacilityReportService>();
builder.Services.AddScoped<StaffAuthenticationService>();
builder.Services.AddSingleton<AnonymousInterviewTokenService>();
builder.Services.AddSingleton<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();
builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection(IntegrationOptions.Section));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<StaffSeedOptions>(builder.Configuration.GetSection(StaffSeedOptions.Section));
builder.Services.ConfigureHttpJsonOptions(x => x.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = jwt.GetSigningKey();
builder.Services.AddAuthentication()
    .AddJwtBearer(AuthenticationSchemes.StaffJwt, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    })
    .AddJwtBearer(AuthenticationSchemes.AnonymousInterviewJwt, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.AnonymousAudience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FacilityApiKeyAuthenticationHandler>(AuthenticationSchemes.FacilityApiKey, _ => { })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PatientSessionAuthenticationHandler>(AuthenticationSchemes.PatientSession, _ => { })
    .AddPolicyScheme(AuthenticationSchemes.InterviewAccess, null, options => options.ForwardDefaultSelector = context =>
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticationSchemes.PatientSession;
        return value[7..].Count(x => x == '.') == 2 ? AuthenticationSchemes.AnonymousInterviewJwt : AuthenticationSchemes.PatientSession;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthenticationSchemes.FacilityPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthenticationSchemes.StaffJwt, AuthenticationSchemes.FacilityApiKey);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy(AuthenticationSchemes.PatientPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthenticationSchemes.PatientSession);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy(AuthenticationSchemes.StaffPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthenticationSchemes.StaffJwt);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy(AuthenticationSchemes.InterviewPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthenticationSchemes.InterviewAccess);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("access", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("voice", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(DocPrepClaims.VisitId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("public-interview", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DocPrep API",
        Version = "v1",
        Description = "Przygotowanie pacjenta do wizyty. System nie diagnozuje, nie zaleca leczenia i nie ocenia pilności."
    });
    options.AddSecurityDefinition("FacilityApiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Api-Key" });
    options.AddSecurityDefinition("StaffJwt", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Token użytkownika panelu zwrócony przez /api/v1/auth/login." });
    options.AddSecurityDefinition("PatientSession", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "opaque", Description = "Sesja pacjenta zwrócona po wymianie linku lub kodu." });
    options.AddSecurityDefinition("AnonymousInterviewJwt", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "JWT ograniczony do pojedynczego wywiadu." });
    options.OperationFilter<SecurityRequirementsOperationFilter>();
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (DocPrepDbContext db, IDistributedCache cache, CancellationToken ct) =>
{
    if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
    const string key = "health:ready";
    await cache.SetStringAsync(key, "ok", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) }, ct);
    var redisReady = await cache.GetStringAsync(key, ct) == "ok";
    await cache.RemoveAsync(key, ct);
    return redisReady ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
}).ExcludeFromDescription();
app.MapDocPrepEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DocPrepDbContext>().Database.MigrateAsync();
}
await StaffIdentitySeeder.Seed(app);
await app.RunAsync();

public partial class Program;
