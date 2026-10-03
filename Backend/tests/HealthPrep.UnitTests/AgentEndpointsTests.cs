using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HealthPrep.Api;
using HealthPrep.Api.Agents;
using HealthPrep.Domain.Appointments;
using HealthPrep.Infrastructure;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthPrep.UnitTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEALTHPREP_TEST_POSTGRES"))) Skip = "Requires an isolated PostgreSQL database (HEALTHPREP_TEST_POSTGRES)."; }
}

public sealed class AgentEndpointsTests
{
    [PostgresFact]
    public async Task Invitation_scopes_credentials_webhooks_revocation_and_limits_work_together()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("HEALTHPREP_TEST_POSTGRES"),
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["ElevenLabs:ApiKey"] = "provider-test-key",
            ["ElevenLabs:AgentId"] = "agent-test",
            ["ElevenLabs:WebhookSecret"] = "webhook-test-secret"
        });
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddAgentInterviews(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        var fakeProvider = new ProviderHandler();
        builder.Services.AddTransient(sp => new ElevenLabsProvider(new HttpClient(fakeProvider, disposeHandler: false)
        { BaseAddress = new Uri("https://api.elevenlabs.io/v1/") }, sp.GetRequiredService<IOptions<ElevenLabsOptions>>()));
        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRateLimiter();
        app.MapAgentInterviews();
        app.Urls.Add("http://127.0.0.1:0");
        var visit = new Appointment(Guid.NewGuid(), Guid.NewGuid().ToString(), "patient-test", DateTimeOffset.UtcNow.AddDays(5), "Test", DateTimeOffset.UtcNow);
        var createdVisits = new List<Guid> { visit.Id };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Appointments.Add(visit);
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/visits/{visit.Id}/interview")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Patient-Id", "another-patient");
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/visits/{visit.Id}/interview")).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Patient-Id");
            client.DefaultRequestHeaders.Add("X-Patient-Id", "patient-test");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/visits/{visit.Id}/interview")).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Patient-Id");
            // Create the invitation through the service: the facility-key filter is independently used by existing API.
            string invitationPath;
            using (var scope = app.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<AgentInterviewService>();
                var response = JsonSerializer.SerializeToElement(await service.CreateInvitation(visit.Id, visit.TenantId, default));
                invitationPath = response.GetProperty("invitationUrl").GetString()!;
            }
            var token = invitationPath.Split('/').Last();
            var info = await client.GetFromJsonAsync<JsonElement>($"/api/public/interviews/{token}");
            Assert.False(info.GetProperty("interview").TryGetProperty("patientId", out _));
            var authorization = await client.PostAsJsonAsync($"/api/public/interviews/{token}/authorize", new { });
            var auth = await authorization.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new("Bearer", auth.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/interviews/{Guid.NewGuid()}/result")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/interview/sessions", new { mode = "unknown" })).StatusCode);

            var voice = await (await client.PostAsJsonAsync("/api/interview/sessions", new { mode = "voice" })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("temporary-voice", voice.GetProperty("conversationToken").GetString());
            var sessionId = voice.GetProperty("sessionId").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/interview-sessions/{sessionId}/provider-conversation", new { conversationId = "conv_other" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/interview-sessions/{sessionId}/provider-conversation", new { conversationId = voice.GetProperty("conversationId").GetString() })).StatusCode);
            await client.PostAsJsonAsync($"/api/interview-sessions/{sessionId}/end", new { continuesInterview = true });
            var text = await (await client.PostAsJsonAsync("/api/interview/sessions", new { mode = "text" })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("wss://temporary-text", text.GetProperty("signedUrl").GetString());
            Assert.DoesNotContain("provider-test-key", text.GetRawText());
            var textSession = text.GetProperty("sessionId").GetGuid();
            await client.PostAsJsonAsync($"/api/interview-sessions/{textSession}/end", new { });
            var pending = await client.GetFromJsonAsync<JsonElement>("/api/interview/result");
            Assert.Equal("processing", pending.GetProperty("status").GetString());

            var payload = JsonSerializer.Serialize(new { type = "post_call_transcription", data = new {
                agent_id = "agent-test", conversation_id = text.GetProperty("conversationId").GetString(), status = "done",
                transcript = new[] { new { role = "user", message = "Ból głowy" } },
                analysis = new { transcript_summary = "Pacjent opisuje ból głowy.", data_collection_results = new { reason = new { value = "Ból głowy" } } }, metadata = new { }
            } });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/webhooks/elevenlabs", new StringContent(payload))).StatusCode);
            async Task<HttpResponseMessage> SignedWebhook(string body)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-test-secret"), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/elevenlabs") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                request.Headers.Add("ElevenLabs-Signature", $"t={timestamp},v0={hash}");
                return await client.SendAsync(request);
            }
            Assert.Equal(HttpStatusCode.OK, (await SignedWebhook(payload)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SignedWebhook(payload.Replace("Pacjent opisuje ból głowy.", "Changed on retry"))).StatusCode);
            var result = await client.GetFromJsonAsync<JsonElement>("/api/interview/result");
            Assert.Equal("completed", result.GetProperty("status").GetString());
            Assert.Equal("Pacjent opisuje ból głowy.", result.GetProperty("summary").GetString());
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/public/interviews/{token}")).StatusCode);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>();
                Assert.Contains("Ból głowy", (await db.AgentSessions.FindAsync(textSession))!.TranscriptJson!);
                Assert.Equal(InterviewStatus.NotStarted, (await db.Appointments.FindAsync(visit.Id))!.Status);
                Assert.False((await db.Appointments.FindAsync(visit.Id))!.SharingConsentGranted);
            }

            // A new interview demonstrates an atomic limit, plus revocation of an existing bearer session.
            string secondToken;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>();
                var secondVisit = new Appointment(visit.TenantId, Guid.NewGuid().ToString(), "patient-test", DateTimeOffset.UtcNow.AddDays(5), "Test", DateTimeOffset.UtcNow);
                createdVisits.Add(secondVisit.Id);
                db.Appointments.Add(secondVisit); await db.SaveChangesAsync();
                var service = scope.ServiceProvider.GetRequiredService<AgentInterviewService>();
                var second = JsonSerializer.SerializeToElement(await service.CreateInvitation(secondVisit.Id, visit.TenantId, default));
                secondToken = second.GetProperty("invitationUrl").GetString()!.Split('/').Last();
            }
            var secondAuth = await (await client.PostAsJsonAsync($"/api/public/interviews/{secondToken}/authorize", new { })).Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new("Bearer", secondAuth.GetProperty("accessToken").GetString());
            var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/interview/sessions", new { mode = "text" })));
            Assert.Equal(3, parallel.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.Single(parallel.Where(x => x.StatusCode == HttpStatusCode.TooManyRequests));
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>();
                var invitation = await db.InterviewInvitations.SingleAsync(x => x.TokenHash == HealthPrep.Domain.Agents.InterviewInvitation.Hash(secondToken));
                invitation.Revoke(DateTimeOffset.UtcNow); await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync("/api/interview/result")).StatusCode);
            Assert.All(fakeProvider.Requests, path => Assert.Contains("agent_id=agent-test", path));
        }
        finally
        {
            await app.StopAsync();
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HealthPrepDbContext>().Appointments
                .Where(x => createdVisits.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        private int count;
        private readonly string suffix = Guid.NewGuid().ToString("N");
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("provider-test-key", request.Headers.GetValues("xi-api-key").Single());
            var number = Interlocked.Increment(ref count);
            lock (Requests) Requests.Add(request.RequestUri!.ToString());
            var content = request.RequestUri!.AbsolutePath.EndsWith("/token")
                ? JsonContent.Create(new { token = "temporary-voice", conversation_id = $"conv_test_{number}_{suffix}" })
                : JsonContent.Create(new { signed_url = "wss://temporary-text", conversation_id = $"conv_test_{number}_{suffix}" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
