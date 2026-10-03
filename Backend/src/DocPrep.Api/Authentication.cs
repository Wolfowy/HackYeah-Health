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
using DocPrep.Application.Contracts;
using DocPrep.Infrastructure.Persistence;

namespace DocPrep.Api;

public static class AuthenticationSchemes
{
    public const string StaffJwt = "StaffJwt";
    public const string FacilityApiKey = "FacilityApiKey";
    public const string PatientSession = "PatientSession";
    public const string PatientAccountJwt = "PatientAccountJwt";
    public const string AnonymousInterviewJwt = "AnonymousInterviewJwt";
    public const string InterviewAccess = "InterviewAccess";
    public const string FacilityPolicy = "FacilityAccess";
    public const string PatientPolicy = "PatientAccess";
    public const string StaffPolicy = "StaffAccess";
    public const string PatientAccountPolicy = "PatientAccountAccess";
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
    public const string PatientIdentityId = "patient_identity_id";
}

public sealed class JwtOptions
{
    public const string Section = "Authentication:Jwt";
    public string Issuer { get; init; } = "DocPrep";
    public string Audience { get; init; } = "DocPrep.Frontend";
    public string AnonymousAudience { get; init; } = "DocPrep.AnonymousInterview";
    public string PatientAudience { get; init; } = "DocPrep.PatientAccount";
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

public sealed record PatientRegisterRequest(string Email, string Password, string DisplayName);
public sealed record PatientLoginRequest(string Email, string Password);
public sealed record PatientProfileUpdateRequest(string DisplayName, string? AvatarUrl);
public sealed record PatientAccountView(Guid Id, string Email, string DisplayName, string? AvatarUrl);
public sealed record PatientAccountTokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, PatientAccountView User);
public sealed record PatientAccountVisitView(VisitDetails Visit);

public sealed class PatientAccountService(DocPrepDbContext db, IPasswordHasher<PatientAccount> hasher,
    IPatientSessionStore sessions, IClock clock, IOptions<JwtOptions> options)
{
    private readonly JwtOptions jwt = options.Value;

    public async Task<PatientAccountTokenResponse> Register(Guid verifiedVisitId, PatientRegisterRequest request, CancellationToken ct)
    {
        ValidatePassword(request.Password);
        var visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == verifiedVisitId, ct) ?? throw new NotFoundError();
        var email = PatientAccount.NormalizeEmail(request.Email);
        if (await db.PatientAccounts.AnyAsync(x => x.Email == email || x.PatientIdentityId == visit.PatientIdentityId, ct))
            throw new ConflictError("patient_account.exists", "An account already exists for this patient or e-mail address.");
        var account = new PatientAccount(visit.PatientIdentityId, request.Email, request.DisplayName, "temporary", clock.UtcNow);
        account.ReplacePasswordHash(hasher.HashPassword(account, request.Password));
        db.PatientAccounts.Add(account);
        var response = Issue(account);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<PatientAccountTokenResponse> Login(PatientLoginRequest request, CancellationToken ct)
    {
        var email = PatientAccount.NormalizeEmail(request.Email);
        var account = await db.PatientAccounts.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
        if (account is null || hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            throw new UnauthorizedError("patient_account.invalid_credentials", "Invalid credentials.");
        return await SaveIssued(account, ct);
    }

    public async Task<PatientAccountTokenResponse> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        var stored = await db.PatientRefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || !stored.IsUsable(clock.UtcNow)) throw new UnauthorizedError("patient_account.invalid_token", "Invalid refresh token.");
        var account = await db.PatientAccounts.SingleOrDefaultAsync(x => x.Id == stored.AccountId && x.IsActive, ct)
            ?? throw new UnauthorizedError("patient_account.invalid_token", "Invalid refresh token.");
        var raw = GenerateRefreshToken(); stored.Revoke(clock.UtcNow, Hash(raw));
        var access = CreateAccessToken(account);
        db.PatientRefreshTokens.Add(new(account.Id, Hash(raw), clock.UtcNow.AddDays(jwt.RefreshTokenDays), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return new(access.Token, raw, access.ExpiresAt, Map(account));
    }

    public async Task Logout(Guid accountId, LogoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;
        var token = await db.PatientRefreshTokens.SingleOrDefaultAsync(x => x.AccountId == accountId && x.TokenHash == Hash(request.RefreshToken), ct);
        if (token is null) return; token.Revoke(clock.UtcNow); await db.SaveChangesAsync(ct);
    }

    public async Task<PatientAccountView> Me(Guid accountId, CancellationToken ct) => Map(await Account(accountId, ct));
    public async Task<PatientAccountView> Update(Guid accountId, PatientProfileUpdateRequest request, CancellationToken ct)
    { var account = await Account(accountId, ct); account.Update(request.DisplayName, request.AvatarUrl); await db.SaveChangesAsync(ct); return Map(account); }

    public async Task<IReadOnlyList<PatientAccountVisitView>> Visits(Guid accountId, CancellationToken ct)
    {
        var account = await Account(accountId, ct);
        var visits = await db.Visits.Where(x => x.PatientIdentityId == account.PatientIdentityId).OrderByDescending(x => x.ScheduledAt).ToListAsync(ct);
        var result = new List<PatientAccountVisitView>();
        foreach (var visit in visits)
        {
            var interview = await db.AgentInterviews.Where(x => x.VisitProcessId == visit.Id).OrderByDescending(x => x.Generation).FirstOrDefaultAsync(ct);
            if (interview is null) continue;
            result.Add(new(new(visit.Id, visit.ExternalVisitId, visit.ScheduledAt, visit.TimeZone, visit.ServiceExpiresAt,
                new(visit.AssignedClinicianId, visit.DoctorName, visit.DoctorSpecialty),
                new(visit.FacilityId, visit.FacilityName, visit.FacilityAddress), visit.Room, visit.VisitType,
                visit.LocationInstructions, VisitStatusName(visit.Status), interview.Id,
                interview.Status == Domain.Interviews.AgentInterviewStatus.InProgress ? "in_progress" : interview.Status.ToString().ToLowerInvariant())));
        }
        return result;
    }

    public async Task<ExchangeAccessResult> CreateVisitSession(Guid accountId, Guid visitId, CancellationToken ct)
    {
        var account = await Account(accountId, ct);
        var visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == visitId && x.PatientIdentityId == account.PatientIdentityId, ct) ?? throw new NotFoundError();
        visit.Expire(clock.UtcNow);
        if (visit.Status is Domain.Visits.VisitStatus.Cancelled or Domain.Visits.VisitStatus.Expired) throw new ConflictError("visit.inactive", "The visit is inactive.");
        var token = await sessions.Create(visit.Id, visit.ServiceExpiresAt, ct);
        return new(token, visit.Id, visit.ServiceExpiresAt);
    }

    private async Task<PatientAccountTokenResponse> SaveIssued(PatientAccount account, CancellationToken ct)
    { var response = Issue(account); await db.SaveChangesAsync(ct); return response; }
    private PatientAccountTokenResponse Issue(PatientAccount account)
    {
        var access = CreateAccessToken(account); var refresh = GenerateRefreshToken();
        db.PatientRefreshTokens.Add(new(account.Id, Hash(refresh), clock.UtcNow.AddDays(jwt.RefreshTokenDays), clock.UtcNow));
        return new(access.Token, refresh, access.ExpiresAt, Map(account));
    }
    private (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(PatientAccount account)
    {
        var now = clock.UtcNow; var expires = now.AddMinutes(jwt.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[] { new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), new(JwtRegisteredClaimNames.Email, account.Email),
                new("name", account.DisplayName), new(DocPrepClaims.PatientIdentityId, account.PatientIdentityId.ToString()),
                new(DocPrepClaims.Scope, "patient:account") }),
            Issuer = jwt.Issuer, Audience = jwt.PatientAudience, IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime, SigningCredentials = new(new SymmetricSecurityKey(jwt.GetSigningKey()), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler(); return (handler.WriteToken(handler.CreateToken(descriptor)), expires);
    }
    private async Task<PatientAccount> Account(Guid id, CancellationToken ct) =>
        await db.PatientAccounts.SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct) ?? throw new NotFoundError();
    private static void ValidatePassword(string value)
    { if (string.IsNullOrWhiteSpace(value) || value.Length < 12 || !value.Any(char.IsUpper) || !value.Any(char.IsLower) || !value.Any(char.IsDigit)) throw new ArgumentException("Password must contain at least 12 characters, upper/lower case letters and a digit."); }
    private static string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static PatientAccountView Map(PatientAccount account) => new(account.Id, account.Email.ToLowerInvariant(), account.DisplayName, account.AvatarUrl);
    private static string VisitStatusName(Domain.Visits.VisitStatus status) => status switch
    {
        Domain.Visits.VisitStatus.NotStarted => "not_started", Domain.Visits.VisitStatus.InProgress => "in_progress",
        Domain.Visits.VisitStatus.AwaitingApproval => "awaiting_approval", Domain.Visits.VisitStatus.RequiresSupplementation => "requires_supplementation",
        _ => status.ToString().ToLowerInvariant()
    };
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AuthenticatedUserView(Guid Id, Guid FacilityId, string Email, string DisplayName, FacilityRole Role, string? ClinicianId);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, AuthenticatedUserView User);
public sealed record CreateStaffRequest(string Email, string DisplayName, FacilityRole Role, string? ClinicianId, string Password);
public sealed record UpdateStaffRequest(string DisplayName, FacilityRole Role, string? ClinicianId, bool IsActive = true);
public sealed record AnonymousInterviewTokenResponse(string AccessToken, int ExpiresIn, Guid InterviewId);

public sealed class AnonymousInterviewTokenService(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions jwt = options.Value;

    public AnonymousInterviewTokenResponse Issue(Guid interviewId, Guid invitationId, Guid visitId, DateTimeOffset invitationExpiresAt)
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
                new Claim(DocPrepClaims.VisitId, visitId.ToString()),
                new Claim(DocPrepClaims.Scope, "interview:read interview:execute report:review")]),
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

public sealed class StaffAdministrationService(DocPrepDbContext db, IPasswordHasher<StaffUser> hasher, IClock clock)
{
    public async Task<IReadOnlyList<AuthenticatedUserView>> List(Guid facilityId, CancellationToken ct) =>
        await db.StaffUsers.AsNoTracking().Where(x => x.FacilityId == facilityId).OrderBy(x => x.DisplayName)
            .Select(x => new AuthenticatedUserView(x.Id, x.FacilityId, x.Email.ToLower(), x.DisplayName, x.Role, x.ClinicianId)).ToListAsync(ct);
    public async Task<AuthenticatedUserView> Create(Guid facilityId, CreateStaffRequest request, CancellationToken ct)
    {
        if (request.Password.Length < 12) throw new ArgumentException("Password must have at least 12 characters.");
        var email = StaffUser.NormalizeEmail(request.Email);
        if (await db.StaffUsers.AnyAsync(x => x.Email == email, ct)) throw new ConflictError("staff.email_exists", "The e-mail address is already used.");
        if (request.Role == FacilityRole.Clinician && string.IsNullOrWhiteSpace(request.ClinicianId)) throw new ArgumentException("ClinicianId is required for a clinician.");
        var user = new StaffUser(facilityId, request.Email, request.DisplayName, request.Role, request.ClinicianId, "temporary", clock.UtcNow);
        user.ReplacePasswordHash(hasher.HashPassword(user, request.Password)); db.StaffUsers.Add(user);
        if (user.Role == FacilityRole.Clinician && !await db.Clinicians.AnyAsync(x => x.FacilityId == facilityId && x.Id == user.ClinicianId, ct))
        {
            db.Clinicians.Add(new(facilityId, user.ClinicianId!, user.DisplayName, "Medycyna ogólna", null, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
        return new(user.Id, user.FacilityId, user.Email.ToLowerInvariant(), user.DisplayName, user.Role, user.ClinicianId);
    }
    public async Task<AuthenticatedUserView> Update(Guid facilityId, Guid id, UpdateStaffRequest request, CancellationToken ct)
    {
        var user = await db.StaffUsers.SingleOrDefaultAsync(x => x.Id == id && x.FacilityId == facilityId, ct) ?? throw new NotFoundError();
        user.Update(request.DisplayName, request.Role, request.ClinicianId); if (!request.IsActive) user.Deactivate(); await db.SaveChangesAsync(ct);
        return new(user.Id, user.FacilityId, user.Email.ToLowerInvariant(), user.DisplayName, user.Role, user.ClinicianId);
    }
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
        foreach (var user in (await db.StaffUsers.Where(x => x.Role == FacilityRole.Clinician && x.IsActive && x.ClinicianId != null).ToListAsync())
            .DistinctBy(x => (x.FacilityId, x.ClinicianId)))
            if (!await db.Clinicians.AnyAsync(x => x.FacilityId == user.FacilityId && x.Id == user.ClinicianId))
                db.Clinicians.Add(new(user.FacilityId, user.ClinicianId!, user.DisplayName, "Medycyna ogólna", null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }
}
