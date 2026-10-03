using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using DocPrep.Domain.Interviews;
using DocPrep.Infrastructure.Services;

namespace DocPrep.UnitTests;

public sealed class ElevenLabsIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Webhook_verifier_accepts_valid_signature_and_rejects_stale_signature()
    {
        var options = Options.Create(new ElevenLabsOptions { WebhookSecret = "webhook-secret", WebhookToleranceMinutes = 30 });
        var verifier = new ElevenLabsWebhookVerifier(options);
        var payload = Encoding.UTF8.GetBytes("{\"type\":\"post_call_transcription\"}");
        var timestamp = Now.ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-secret"), Encoding.UTF8.GetBytes(timestamp + ".").Concat(payload).ToArray())).ToLowerInvariant();

        Assert.True(verifier.IsValid(payload, $"t={timestamp},v0={signature}", Now));
        Assert.False(verifier.IsValid(payload, $"t={timestamp},v0={signature}", Now.AddMinutes(31)));
        Assert.False(verifier.IsValid(payload, $"t={timestamp},v0=00", Now));
    }

    [Fact]
    public void Invitation_enforces_session_limit_and_revocation()
    {
        var invitation = new InterviewInvitation(Guid.NewGuid(), new string('a', 64), Now.AddDays(1), 2, Now);

        invitation.UseSession(Now);
        invitation.UseSession(Now);

        Assert.False(invitation.IsUsable(Now));
        Assert.Throws<DocPrep.Domain.Common.DomainException>(() => invitation.UseSession(Now));
    }

    [Fact]
    public async Task Client_requests_webrtc_token_without_exposing_api_key_in_url()
    {
        var handler = new RecordingHandler("{\"token\":\"temporary\",\"conversation_id\":\"conv_123\"}");
        var client = new ElevenLabsClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.io") },
            Options.Create(new ElevenLabsOptions { ApiKey = "secret-key", AgentId = "agent_123", Environment = "staging" }));

        var result = await client.CreateVoiceCredential("anon_123", CancellationToken.None);

        Assert.Equal("temporary", result.ConversationToken);
        Assert.Equal("conv_123", result.ConversationId);
        Assert.Contains("agent_id=agent_123", handler.Uri!.Query);
        Assert.DoesNotContain("secret-key", handler.Uri.ToString());
        Assert.Equal("secret-key", handler.ApiKey);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? ApiKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            ApiKey = request.Headers.GetValues("xi-api-key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
