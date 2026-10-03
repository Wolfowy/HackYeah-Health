using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HealthPrep.Api.Agents;

public sealed class ElevenLabsOptions
{
    public string ApiKey { get; init; } = "";
    public string AgentId { get; init; } = "";
    public string WebhookSecret { get; init; } = "";
    public string Environment { get; init; } = "production";
}

public sealed record ProviderCredential(string Value, string? ConversationId);
public sealed class AgentApiException(int status, string message) : Exception(message)
{ public int Status { get; } = status; }

public sealed class ElevenLabsProvider(HttpClient http, IOptions<ElevenLabsOptions> options)
{
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey) || string.IsNullOrWhiteSpace(options.Value.AgentId))
            throw new AgentApiException(503, "Agent is not configured on the server.");
    }

    public async Task<ProviderCredential> CreateCredential(string mode, CancellationToken ct)
    {
        EnsureConfigured();
        var agent = Uri.EscapeDataString(options.Value.AgentId);
        var path = mode == "voice"
            ? $"convai/conversation/token?agent_id={agent}&environment={Uri.EscapeDataString(options.Value.Environment)}"
            : $"convai/conversation/get-signed-url?agent_id={agent}&include_conversation_id=true";
        var json = await Get(path, ct);
        var key = mode == "voice" ? "token" : "signed_url";
        if (!json.TryGetProperty(key, out var credential) || string.IsNullOrWhiteSpace(credential.GetString()))
            throw new AgentApiException(502, "Agent credential response is invalid.");
        return new(credential.GetString()!, json.TryGetProperty("conversation_id", out var id) ? id.GetString() : null);
    }

    public async Task<bool> OwnsConversation(string conversationId, string technicalUserId, CancellationToken ct)
    {
        var data = await Get($"convai/conversations/{Uri.EscapeDataString(conversationId)}", ct);
        return data.TryGetProperty("agent_id", out var agent) && agent.GetString() == options.Value.AgentId
            && data.TryGetProperty("user_id", out var user) && user.GetString() == technicalUserId;
    }

    private async Task<JsonElement> Get(string path, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("xi-api-key", options.Value.ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new AgentApiException(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? 429 : 502,
                    "Agent provider is temporarily unavailable.");
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (HttpRequestException) { throw new AgentApiException(503, "Agent provider is unavailable."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AgentApiException(503, "Agent provider timed out."); }
        catch (JsonException) { throw new AgentApiException(502, "Agent provider returned an invalid response."); }
    }
}
