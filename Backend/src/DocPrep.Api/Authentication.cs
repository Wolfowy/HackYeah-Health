using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;
using DocPrep.Domain.Tenancy;
using DocPrep.Infrastructure.Persistence;

namespace DocPrep.Api;

public static class AuthenticationSchemes
{
    public const string StaffJwt = "StaffJwt";
    public const string FacilityApiKey = "FacilityApiKey";
    public const string PatientSession = "PatientSession";
    public const string AnonymousInterviewJwt = "AnonymousInterviewJwt";
    public const string InterviewAccess = "InterviewAccess";
    public const string FacilityPolicy = "FacilityAccess";
    public const string PatientPolicy = "PatientAccess";
    public const string StaffPolicy = "StaffAccess";
    public const string InterviewPolicy = "InterviewExecute";
}

public static class DocPrepClaims
{
    public const string FacilityId = "facility_id";
    public const string ClinicianId = "clinician_id";
    public const string VisitId = "visit_id";
    public const string InterviewId = "interview_id";
    public const string InvitationId = "invitation_id";
    public const string Scope = "scope";
}

public sealed class JwtOptions
{
    public const string Section = "Authentication:Jwt";
    public string Issuer { get; init; } = "DocPrep";
    public string Audience { get; init; } = "DocPrep.Frontend";
    public string AnonymousAudience { get; init; } = "DocPrep.AnonymousInterview";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;
    public int AnonymousTokenMinutes { get; init; } = 60;

    public byte[] GetSigningKey()
    {
        try
        {
            var key = Convert.FromBase64String(SigningKey);
            if (key.Length < 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be a base64-encoded key of at least 256 bits.");
        }
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AuthenticatedUserView(Guid Id, Guid FacilityId, string Email, string DisplayName, FacilityRole Role, string? ClinicianId);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, AuthenticatedUserView User);
public sealed record AnonymousInterviewTokenResponse(string AccessToken, int ExpiresIn, Guid InterviewId);

public sealed class AnonymousInterviewTokenService(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions jwt = options.Value;

    public AnonymousInterviewTokenResponse Issue(Guid interviewId, Guid invitationId, DateTimeOffset invitationExpiresAt)
    {
        var now = clock.UtcNow;
        var expiresAt = new[] { now.AddMinutes(jwt.AnonymousTokenMinutes), invitationExpiresAt }.Min();
        if (expiresAt <= now) throw new ConflictError("agent_invitation.expired", "The invitation expired.");
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, "anonymous-interview"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(DocPrepClaims.InterviewId, interviewId.ToString()),
                new Claim(DocPrepClaims.InvitationId, invitationId.ToString()),
                new Claim(DocPrepClaims.Scope, "interview:read interview:execute")]),
            Issuer = jwt.Issuer,
            Audience = jwt.AnonymousAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(jwt.GetSigningKey()), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return new(handler.WriteToken(handler.CreateToken(descriptor)), (int)(expiresAt - now).TotalSeconds, interviewId);
    }
}

public sealed class StaffAuthenticationService(
    DocPrepDbContext db,
    IPasswordHasher<StaffUser> passwordHasher,
    IOptions<JwtOptions> options,
    IClock clock)
{
    private readonly JwtOptions jwt = options.Value;

    public async Task<TokenResponse> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("Email and password are required.");

        var normalizedEmail = StaffUser.NormalizeEmail(request.Email);
        var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        if (user is null || !user.IsActive) throw InvalidCredentials();
        if (user.IsLocked(clock.UtcNow)) throw new TooManyRequestsError("authentication.locked", "The account is temporarily locked.");

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.LoginFailed(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            throw InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.ReplacePasswordHash(passwordHasher.HashPassword(user, request.Password));
        user.LoginSucceeded(clock.UtcNow);
        var response = IssueTokens(user);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<TokenResponse> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw InvalidCredentials();
        var tokenHash = Hash(request.RefreshToken);
        var stored = await db.StaffRefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
        if (stored is null || !stored.IsUsable(clock.UtcNow)) throw InvalidCredentials();
        var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Id == stored.UserId, ct);
        if (user is null || !user.IsActive || user.IsLocked(clock.UtcNow)) throw InvalidCredentials();

        var rawRefreshToken = GenerateRefreshToken();
        var replacementHash = Hash(rawRefreshToken);
        stored.Revoke(clock.UtcNow, replacementHash);
        var access = CreateAccessToken(user);
        db.StaffRefreshTokens.Add(new StaffRefreshToken(user.Id, replacementHash, clock.UtcNow.AddDays(jwt.RefreshTokenDays), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return new(access.Token, rawRefreshToken, access.ExpiresAt, Map(user));
    }

    public async Task Logout(Guid userId, LogoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;
        var stored = await db.StaffRefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == Hash(request.RefreshToken) && x.UserId == userId, ct);
        if (stored is null) return;
        stored.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthenticatedUserView> Me(Guid userId, CancellationToken ct)
    {
        var user = await db.StaffUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct) ?? throw new NotFoundError();
        return Map(user);
    }

    private TokenResponse IssueTokens(StaffUser user)
    {
        var access = CreateAccessToken(user);
        var rawRefreshToken = GenerateRefreshToken();
        db.StaffRefreshTokens.Add(new StaffRefreshToken(user.Id, Hash(rawRefreshToken), clock.UtcNow.AddDays(jwt.RefreshTokenDays), clock.UtcNow));
        return new(access.Token, rawRefreshToken, access.ExpiresAt, Map(user));
    }

    private (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(StaffUser user)
    {
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.DisplayName),
            new("role", user.Role.ToString()),
            new(DocPrepClaims.FacilityId, user.FacilityId.ToString())
        };
        if (!string.IsNullOrWhiteSpace(user.ClinicianId)) claims.Add(new(DocPrepClaims.ClinicianId, user.ClinicianId));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(jwt.GetSigningKey()), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(handler.CreateToken(descriptor)), expiresAt);
    }

    private static string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static UnauthorizedError InvalidCredentials() => new("authentication.invalid_credentials", "Invalid credentials.");
    private static AuthenticatedUserView Map(StaffUser user) => new(user.Id, user.FacilityId, user.Email.ToLowerInvariant(), user.DisplayName, user.Role, user.ClinicianId);
}

public sealed class FacilityApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<IntegrationOptions> clients)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var value)) return Task.FromResult(AuthenticateResult.NoResult());
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()));
        var client = clients.Value.Clients.FirstOrDefault(x => Matches(x.ApiKeySha256, supplied));
        if (client is null) return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        var claims = new List<Claim>
        {
            new("name", client.Name),
            new("role", client.Role.ToString()),
            new(DocPrepClaims.FacilityId, client.FacilityId.ToString())
        };
        if (!string.IsNullOrWhiteSpace(client.ClinicianId)) claims.Add(new(DocPrepClaims.ClinicianId, client.ClinicianId));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }

    private static bool Matches(string configured, byte[] supplied)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(configured), supplied); }
        catch { return false; }
    }
}

public sealed class PatientSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPatientSessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var visitId = await sessions.Resolve(authorization[7..].Trim(), Context.RequestAborted);
        if (visitId is null) return AuthenticateResult.Fail("Invalid or expired patient session.");
        var identity = new ClaimsIdentity([new Claim(DocPrepClaims.VisitId, visitId.Value.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

public sealed class StaffSeedOptions
{
    public const string Section = "Authentication:SeedUsers";
    public bool Enabled { get; init; }
    public List<StaffSeedUser> Users { get; init; } = [];
}

public sealed record StaffSeedUser(Guid FacilityId, string Email, string DisplayName, FacilityRole Role, string? ClinicianId, string Password);

public static class StaffIdentitySeeder
{
    public static async Task Seed(WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<StaffSeedOptions>>().Value;
        if (!options.Enabled || options.Users.Count == 0) return;
        var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<StaffUser>>();
        foreach (var seed in options.Users)
        {
            var email = StaffUser.NormalizeEmail(seed.Email);
            if (await db.StaffUsers.AnyAsync(x => x.Email == email)) continue;
            var user = new StaffUser(seed.FacilityId, seed.Email, seed.DisplayName, seed.Role, seed.ClinicianId, "temporary", DateTimeOffset.UtcNow);
            user.ReplacePasswordHash(hasher.HashPassword(user, seed.Password));
            db.StaffUsers.Add(user);
        }
        await db.SaveChangesAsync();
    }
}
