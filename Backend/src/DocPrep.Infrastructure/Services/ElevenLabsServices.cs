using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Common;

namespace DocPrep.Infrastructure.Services;

public sealed class ElevenLabsOptions
{
    public const string Section = "ElevenLabs";
    public string BaseUrl { get; init; } = "https://api.elevenlabs.io";
    public string ApiKey { get; init; } = "";
    public string AgentId { get; init; } = "";
    public string Environment { get; init; } = "production";
    public string WebhookSecret { get; init; } = "";
    public int WebhookToleranceMinutes { get; init; } = 30;
}

public sealed class ElevenLabsClient(HttpClient http, IOptions<ElevenLabsOptions> options) : IElevenLabsClient
{
    private readonly ElevenLabsOptions settings = options.Value;

    public async Task<ElevenLabsCredential> CreateVoiceCredential(string participantName, CancellationToken ct)
    {
        EnsureConfigured();
        var path = $"/v1/convai/conversation/token?agent_id={Uri.EscapeDataString(settings.AgentId)}&environment={Uri.EscapeDataString(settings.Environment)}&participant_name={Uri.EscapeDataString(participantName)}";
        using var response = await Send(path, ct);
        var body = await response.Content.ReadFromJsonAsync<VoiceTokenResponse>(cancellationToken: ct)
            ?? throw new ServiceUnavailableError("elevenlabs.invalid_response", "ElevenLabs returned an invalid token response.");
        if (string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.ConversationId))
            throw new ServiceUnavailableError("elevenlabs.invalid_response", "ElevenLabs did not return the required conversation credential.");
        return new(body.Token, null, body.ConversationId);
    }

    public async Task<ElevenLabsCredential> CreateTextCredential(string participantName, CancellationToken ct)
    {
        EnsureConfigured();
        var path = $"/v1/convai/conversation/get-signed-url?agent_id={Uri.EscapeDataString(settings.AgentId)}&environment={Uri.EscapeDataString(settings.Environment)}&include_conversation_id=true";
        using var response = await Send(path, ct);
        var body = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(cancellationToken: ct)
            ?? throw new ServiceUnavailableError("elevenlabs.invalid_response", "ElevenLabs returned an invalid signed URL response.");
        if (string.IsNullOrWhiteSpace(body.SignedUrl))
            throw new ServiceUnavailableError("elevenlabs.invalid_response", "ElevenLabs did not return a signed URL.");
        return new(null, body.SignedUrl, ConversationIdFromUrl(body.SignedUrl));
    }

    private async Task<HttpResponseMessage> Send(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("xi-api-key", settings.ApiKey);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode) return response;
        response.Dispose();
        throw new ServiceUnavailableError("elevenlabs.request_failed", "ElevenLabs could not issue a conversation credential.");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.AgentId))
            throw new ServiceUnavailableError("elevenlabs.not_configured", "ElevenLabs is not configured.");
    }

    private static string? ConversationIdFromUrl(string signedUrl)
    {
        if (!Uri.TryCreate(signedUrl, UriKind.Absolute, out var uri)) return null;
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue("conversation_id", out var value) ? value.ToString() : null;
    }

    private sealed record VoiceTokenResponse([property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("conversation_id")] string ConversationId);
    private sealed record SignedUrlResponse([property: JsonPropertyName("signed_url")] string SignedUrl);
}

public sealed class ElevenLabsWebhookVerifier(IOptions<ElevenLabsOptions> options) : IElevenLabsWebhookVerifier
{
    private readonly ElevenLabsOptions settings = options.Value;

    public bool IsValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(settings.WebhookSecret) || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !parts.TryAdd(pair[0], pair[1])) return false;
        }
        if (!parts.TryGetValue("t", out var timestampText) || !long.TryParse(timestampText, out var timestamp) || !parts.TryGetValue("v0", out var suppliedHex)) return false;
        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - signedAt).Duration() > TimeSpan.FromMinutes(settings.WebhookToleranceMinutes)) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(suppliedHex); }
        catch (FormatException) { return false; }
        var prefix = Encoding.UTF8.GetBytes(timestampText + ".");
        var message = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(message, 0); rawBody.CopyTo(message.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookSecret), message);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
