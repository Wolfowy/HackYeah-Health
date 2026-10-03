using DocPrep.Domain.Tenancy;

namespace DocPrep.UnitTests;

public sealed class StaffIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Staff_user_is_locked_after_five_failed_logins()
    {
        var user = new StaffUser(Guid.NewGuid(), "Doctor@Example.org", "Doctor", FacilityRole.Clinician, "doctor-1", "hash", Now);

        for (var i = 0; i < 5; i++) user.LoginFailed(Now);

        Assert.True(user.IsLocked(Now.AddMinutes(14)));
        Assert.False(user.IsLocked(Now.AddMinutes(16)));
        Assert.Equal("DOCTOR@EXAMPLE.ORG", user.Email);
    }

    [Fact]
    public void Refresh_token_can_only_be_used_before_expiry_and_revocation()
    {
        var token = new StaffRefreshToken(Guid.NewGuid(), new string('a', 64), Now.AddDays(1), Now);

        Assert.True(token.IsUsable(Now));
        token.Revoke(Now.AddMinutes(1), new string('b', 64));

        Assert.False(token.IsUsable(Now.AddMinutes(2)));
        Assert.Equal(new string('b', 64), token.ReplacedByTokenHash);
    }
}
