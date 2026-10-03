using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Domain.Interviews;
using DocPrep.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DocPrep.UnitTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCPREP_TEST_POSTGRES"))) Skip = "Requires an isolated PostgreSQL database (DOCPREP_TEST_POSTGRES)."; }
}

[Collection("Postgres")]
public sealed class AgentEndpointsTests
{
    [PostgresFact]
    public async Task Real_api_authorizes_invitation_preserves_mode_switch_and_accepts_signed_result()
    {
        var provider = new FakeProvider();
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("DOCPREP_TEST_POSTGRES"),
                ["Authentication:SeedUsers:Enabled"] = "false",
                ["ElevenLabs:AgentId"] = "agent-test",
                ["ElevenLabs:WebhookSecret"] = "webhook-test-secret"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IElevenLabsClient>();
                services.AddSingleton<IElevenLabsClient>(provider);
                services.RemoveAll<IPatientSessionStore>();
                services.AddSingleton<IPatientSessionStore, MemorySessions>();
            });
        });
        using var facility = factory.CreateClient();
        facility.DefaultRequestHeaders.Add("X-Api-Key", "demo-system-key");
        using var client = factory.CreateClient();
        var visits = new List<Guid>();
        async Task<JsonElement> CreateVisit()
        {
            var response = await facility.PostAsJsonAsync("/api/v1/integration/visits", new {
                externalVisitId = Guid.NewGuid().ToString(), pesel = "00000000000", scheduledAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(visits.Count),
                serviceExpiresAt = DateTimeOffset.UtcNow.AddDays(7), contact = "patient@example.invalid", channel = "Email", assignedClinicianId = "doctor-demo"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            visits.Add(created.GetProperty("visitId").GetGuid());
            return created;
        }
        async Task<string> Authorize(string token)
        {
            var response = await client.PostAsJsonAsync($"/api/public/interviews/{token}/authorize", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.TryGetProperty("interviewId", out _));
            return result.GetProperty("accessToken").GetString()!;
        }
        async Task<HttpResponseMessage> Webhook(string conversationId, string summary)
        {
            var body = JsonSerializer.Serialize(new { type = "post_call_transcription", event_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), data = new {
                agent_id = "agent-test", conversation_id = conversationId, status = "done",
                transcript = new[] { new { role = "user", message = "Ból głowy" } },
                analysis = new { transcript_summary = summary, data_collection_results = new { interview_json = new { value = JsonSerializer.Serialize(new {
                    schemaVersion = 1, consultationReason = "Ból głowy", symptoms = new[] { new { name = "Ból głowy", severity = 6, startedOnState = "unknown", timeline = Array.Empty<object>() } },
                    medications = new[] { new { name = "Ibuprofen", doseState = "unknown", reason = "ból" } },
                    allergies = new[] { new { substance = "penicylina", reaction = "wysypka" } }, chronicConditions = Array.Empty<object>(),
                    patientQuestions = Array.Empty<string>(), medicationsState = "provided", allergiesState = "provided", chronicConditionsState = "provided"
                }) } } }, metadata = new { }
            } });
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-test-secret"), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/elevenlabs") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("ElevenLabs-Signature", $"t={timestamp},v0={hash}");
            return await client.SendAsync(request);
        }
        try
        {
            var created = await CreateVisit();
            var interviewId = created.GetProperty("interviewId").GetGuid();
            var token = created.GetProperty("interviewInvitationToken").GetString()!;
            var metadata = await client.GetFromJsonAsync<JsonElement>($"/api/public/interviews/{token}");
            Assert.Equal(interviewId, metadata.GetProperty("interview").GetProperty("id").GetGuid());
            Assert.False(metadata.GetProperty("interview").TryGetProperty("patientId", out _));
            var accessToken = await Authorize(token);
            client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
            var secondVisit = await CreateVisit();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/interviews/{secondVisit.GetProperty("interviewId").GetGuid()}/result")).StatusCode);
            var voiceResponse = await client.PostAsJsonAsync($"/api/interviews/{interviewId}/sessions", new { mode = "voice" });
            Assert.Equal(HttpStatusCode.OK, voiceResponse.StatusCode);
            var voice = await voiceResponse.Content.ReadFromJsonAsync<JsonElement>();
            var voiceSession = voice.GetProperty("sessionId").GetGuid();
            Assert.Equal("temporary-voice", voice.GetProperty("conversationToken").GetString());
            Assert.False(voice.GetProperty("dynamicVariables").GetProperty("is_continuation").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/interview-sessions/{voiceSession}/provider-conversation", new { conversationId = "conv_other" })).StatusCode);
            await client.PostAsJsonAsync($"/api/interview-sessions/{voiceSession}/end", new { continuesInterview = true });
            Assert.Equal(HttpStatusCode.OK, (await Webhook(voice.GetProperty("conversationId").GetString()!, "Wstępna rozmowa")).StatusCode);
            var active = await client.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}/result");
            Assert.Equal("in_progress", active.GetProperty("status").GetString());
            var textResponse = await client.PostAsJsonAsync($"/api/interviews/{interviewId}/sessions", new { mode = "text" });
            Assert.Equal(HttpStatusCode.OK, textResponse.StatusCode);
            var text = await textResponse.Content.ReadFromJsonAsync<JsonElement>();
            var textSession = text.GetProperty("sessionId").GetGuid();
            Assert.Equal("wss://temporary-text", text.GetProperty("signedUrl").GetString());
            Assert.NotEqual(voice.GetProperty("conversationId").GetString(), text.GetProperty("conversationId").GetString());
            Assert.True(text.GetProperty("dynamicVariables").GetProperty("is_continuation").GetBoolean());
            Assert.Contains("Wstępna rozmowa", text.GetProperty("dynamicVariables").GetProperty("previous_conversation_summary").GetString());
            var bind = await client.PutAsJsonAsync($"/api/interview-sessions/{textSession}/provider-conversation", new { conversationId = text.GetProperty("conversationId").GetString() });
            Assert.True(bind.StatusCode == HttpStatusCode.NoContent, await bind.Content.ReadAsStringAsync());
            await client.PostAsJsonAsync($"/api/interview-sessions/{textSession}/end", new { });
            var processing = await client.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}/result");
            Assert.Equal("processing", processing.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/webhooks/elevenlabs", new StringContent("{}"))).StatusCode);
            var finalWebhook = await Webhook(text.GetProperty("conversationId").GetString()!, "Pacjent opisuje ból głowy.");
            Assert.True(finalWebhook.StatusCode == HttpStatusCode.OK, await finalWebhook.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await Webhook(text.GetProperty("conversationId").GetString()!, "Zmiana przy ponowieniu")).StatusCode);
            var completed = await client.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}/result");
            Assert.Equal("completed", completed.GetProperty("status").GetString());
            Assert.Equal("Pacjent opisuje ból głowy.", completed.GetProperty("finalReport").GetString());
            Assert.Equal("partial", completed.GetProperty("extractionStatus").GetString());
            Assert.Equal("ready", completed.GetProperty("importStatus").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/interviews/{token}")).StatusCode);
            var review = await client.GetFromJsonAsync<JsonElement>("/api/v1/interview");
            foreach (var observation in review.GetProperty("observations").EnumerateArray())
                Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/interview/observations/{observation.GetProperty("id").GetGuid()}/decision", new { decision = "Accepted" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/interview/complete", null)).StatusCode);
            var approvedResponse = await client.PostAsJsonAsync("/api/v1/interview/approve", new { confirmIncompleteReport = true });
            Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
            var approved = await approvedResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/interview/consent", new { granted = true })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await facility.GetAsync($"/api/v1/integration/visits/{visits[0]}/report-versions/{approved.GetProperty("versionId").GetGuid()}")).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                Assert.Contains("Ból głowy", (await db.AgentInterviewSessions.FindAsync(textSession))!.TranscriptJson!);
                Assert.True(await db.Consents.AnyAsync(x => x.VisitProcessId == visits[0]));
                Assert.True(await db.ReportVersions.AnyAsync(x => x.VisitProcessId == visits[0]));
                var draft = await db.Drafts.Include(x => x.Allergies).Include(x => x.Medications).SingleAsync(x => x.VisitProcessId == visits[0]);
                Assert.Contains(draft.Allergies, x => x.Substance == "penicylina");
                Assert.Contains(draft.Medications, x => x.Reason == "ból");
            }
            var secondId = secondVisit.GetProperty("interviewId").GetGuid();
            client.DefaultRequestHeaders.Authorization = new("Bearer", await Authorize(secondVisit.GetProperty("interviewInvitationToken").GetString()!));
            for (var i = 0; i < 3; i++)
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/interviews/{secondId}/sessions", new { mode = "text" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/interviews/{secondId}/sessions", new { mode = "text" })).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                var invitation = await db.InterviewInvitations.SingleAsync(x => x.InterviewId == secondId);
                invitation.Revoke(DateTimeOffset.UtcNow); await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/interviews/{secondId}/result")).StatusCode);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DocPrepDbContext>().Visits.Where(x => visits.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class FakeProvider : IElevenLabsClient
    {
        private readonly string suffix = Guid.NewGuid().ToString("N");
        private int count;
        public Task<ElevenLabsCredential> CreateVoiceCredential(string participantName, CancellationToken ct) =>
            Task.FromResult(new ElevenLabsCredential("temporary-voice", null, $"conv_{suffix}_{Interlocked.Increment(ref count)}"));
        public Task<ElevenLabsCredential> CreateTextCredential(string participantName, CancellationToken ct) =>
            Task.FromResult(new ElevenLabsCredential(null, "wss://temporary-text", $"conv_{suffix}_{Interlocked.Increment(ref count)}"));
    }
    internal sealed class MemorySessions : IPatientSessionStore
    {
        private readonly ConcurrentDictionary<string, Guid> tokens = new();
        public Task<string> Create(Guid visitId, DateTimeOffset expiresAt, CancellationToken ct)
        { var token = Guid.NewGuid().ToString("N"); tokens[token] = visitId; return Task.FromResult(token); }
        public Task<Guid?> Resolve(string token, CancellationToken ct) => Task.FromResult(tokens.TryGetValue(token, out var id) ? (Guid?)id : null);
        public Task RevokeForVisit(Guid visitId, CancellationToken ct)
        { foreach (var pair in tokens.Where(x => x.Value == visitId)) tokens.TryRemove(pair.Key, out _); return Task.CompletedTask; }
    }
}
