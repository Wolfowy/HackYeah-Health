using DocPrep.Domain.Common;

namespace DocPrep.Domain.Tenancy;

public sealed class StaffUser
{
    private StaffUser() { }

    public StaffUser(Guid facilityId, string email, string displayName, FacilityRole role, string? clinicianId, string passwordHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        FacilityId = facilityId;
        Email = NormalizeEmail(email);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        Role = role;
        ClinicianId = clinicianId?.Trim();
        PasswordHash = Guard.Required(passwordHash, nameof(passwordHash), 2000);
        IsActive = true;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid FacilityId { get; private set; }
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public FacilityRole Role { get; private set; }
    public string? ClinicianId { get; private set; }
    public string PasswordHash { get; private set; } = "";
    public bool IsActive { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public void LoginFailed(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= 5)
        {
            LockedUntil = now.AddMinutes(15);
            FailedLoginCount = 0;
        }
    }

    public void LoginSucceeded(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    public void ReplacePasswordHash(string passwordHash) => PasswordHash = Guard.Required(passwordHash, nameof(passwordHash), 2000);
    public void Update(string displayName, FacilityRole role, string? clinicianId)
    { DisplayName = Guard.Required(displayName, nameof(displayName), 200); Role = role; ClinicianId = clinicianId?.Trim(); }
    public void Deactivate() => IsActive = false;

    public static string NormalizeEmail(string email) => Guard.Required(email, nameof(email), 320).ToUpperInvariant();
}

public sealed class StaffRefreshToken
{
    private StaffRefreshToken() { }

    public StaffRefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = Guard.Required(tokenHash, nameof(tokenHash), 128);
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public uint ConcurrencyVersion { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
    public void Revoke(DateTimeOffset now, string? replacedByTokenHash = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
        ConcurrencyVersion++;
    }
}
