using System.Security.Cryptography;
using System.Text;
using HealthPrep.Domain.Agents;

namespace HealthPrep.UnitTests;

public sealed class AgentInterviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Invitation_is_random_and_only_a_hash_is_stored()
    {
        var token = InterviewInvitation.CreateToken();
        Assert.Equal(43, token.Length);
        Assert.NotEqual(token, InterviewInvitation.CreateToken());
        var invitation = new InterviewInvitation(Guid.NewGuid(), InterviewInvitation.Hash(token), Now.AddDays(1));
        Assert.Equal(64, invitation.TokenHash.Length);
        Assert.DoesNotContain(token, invitation.TokenHash);
        Assert.True(invitation.CanAccess(Now));
        Assert.False(invitation.CanStart(Now.AddDays(1)));
        invitation.Revoke(Now);
        Assert.False(invitation.CanAccess(Now));
    }

    [Fact]
    public void Raw_webhook_signature_rejects_modified_payloads_and_stale_or_future_timestamps()
    {
        var body = Encoding.UTF8.GetBytes("{\"type\":\"post_call_transcription\",\"message\":\"ból\"}");
        var timestamp = Now.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes($"{timestamp}." + Encoding.UTF8.GetString(body))));
        var header = $"t={timestamp},v0={signature}";
        Assert.True(ElevenWebhookSignature.Verify(body, header, "secret", Now));
        Assert.False(ElevenWebhookSignature.Verify(Encoding.UTF8.GetBytes("{}"), header, "secret", Now));
        Assert.False(ElevenWebhookSignature.Verify(body, header, "wrong", Now));
        Assert.False(ElevenWebhookSignature.Verify(body, header, "secret", Now.AddMinutes(31)));
        Assert.False(ElevenWebhookSignature.Verify(body, header, "secret", Now.AddMinutes(-31)));
        Assert.False(ElevenWebhookSignature.Verify(body, $"t={timestamp},v0=wrong", "secret", Now));
        Assert.False(ElevenWebhookSignature.Verify(body, header + $",t={timestamp}", "secret", Now));
    }

    [Fact]
    public void Session_cannot_bind_another_conversation_and_webhook_retries_do_not_change_result()
    {
        var interview = new AgentInterview(Guid.NewGuid());
        var session = new AgentSession(interview.Id, null, "voice", Now);
        session.Bind("conv_original");
        Assert.Throws<InvalidOperationException>(() => session.Bind("conv_other"));
        session.Complete("[]", "{}", "{}", "{\"reason\":\"ból\"}", "Pierwszy wynik", Now);
        session.Complete("[]", "{}", "{}", "{}", "Niepożądana zmiana", Now.AddMinutes(1));
        Assert.Equal("Pierwszy wynik", session.Summary);
        session.End();
        Assert.Equal("completed", session.Status);
        interview.Complete(session, Now);
        Assert.Equal("completed", interview.Status);
    }

    [Fact]
    public void Late_webhook_cannot_overwrite_result_of_a_newer_session()
    {
        var interview = new AgentInterview(Guid.NewGuid());
        var earlier = new AgentSession(interview.Id, null, "voice", Now);
        var later = new AgentSession(interview.Id, null, "text", Now.AddMinutes(1));
        later.Complete("[]", "{}", "{}", "{}", "Nowszy wynik", Now.AddMinutes(2));
        earlier.Complete("[]", "{}", "{}", "{}", "Stary wynik", Now.AddMinutes(3));
        interview.Complete(later, Now.AddMinutes(2));
        interview.Complete(earlier, Now.AddMinutes(3));
        Assert.Equal("Nowszy wynik", interview.Summary);
    }
}
