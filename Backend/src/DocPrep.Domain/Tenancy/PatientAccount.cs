using DocPrep.Domain.Common;

namespace DocPrep.Domain.Tenancy;

public sealed class PatientAccount
{
    private PatientAccount() { }
    public PatientAccount(Guid patientIdentityId, string email, string displayName, string passwordHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); PatientIdentityId = patientIdentityId; Email = NormalizeEmail(email);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        PasswordHash = Guard.Required(passwordHash, nameof(passwordHash), 2000);
        CreatedAt = now; VerifiedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid PatientIdentityId { get; private set; }
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string? AvatarUrl { get; private set; }
    public string PasswordHash { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public void Update(string displayName, string? avatarUrl)
    {
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : Guard.Required(avatarUrl, nameof(avatarUrl), 2000);
    }
    public void ReplacePasswordHash(string value) => PasswordHash = Guard.Required(value, nameof(value), 2000);
    public void Deactivate() => IsActive = false;
    public static string NormalizeEmail(string email) => Guard.Required(email, nameof(email), 320).ToUpperInvariant();
}

public sealed class PatientRefreshToken
{
    private PatientRefreshToken() { }
    public PatientRefreshToken(Guid accountId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    { Id = Guid.NewGuid(); AccountId = accountId; TokenHash = tokenHash; ExpiresAt = expiresAt; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public uint ConcurrencyVersion { get; private set; }
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
    public void Revoke(DateTimeOffset now, string? replacement = null)
    { RevokedAt ??= now; ReplacedByTokenHash = replacement; ConcurrencyVersion++; }
}
