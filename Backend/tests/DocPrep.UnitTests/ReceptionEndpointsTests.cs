using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocPrep.Application.Abstractions;
using DocPrep.Domain.Tenancy;
using DocPrep.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DocPrep.UnitTests;

[Collection("Postgres")]
public sealed class ReceptionEndpointsTests
{
    [PostgresFact]
    public async Task Reception_persists_appointments_links_and_enforces_calendar_and_facility_boundaries()
    {
        var otherFacility = Guid.NewGuid();
        var doctorId = $"test-{Guid.NewGuid():N}";
        var secondDoctorId = $"test-{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPatientSessionStore>();
                services.AddSingleton<IPatientSessionStore, AgentEndpointsTests.MemorySessions>();
            });
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("DOCPREP_TEST_POSTGRES"),
                ["Notifications:FrontendBaseUrl"] = "http://127.0.0.1:5173",
                ["Integration:Clients:3:FacilityId"] = otherFacility.ToString(),
                ["Integration:Clients:3:Name"] = "Other facility",
                ["Integration:Clients:3:Role"] = "Administrative",
                ["Integration:Clients:3:ClinicianId"] = "",
                ["Integration:Clients:3:ApiKeySha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test-other-key"))).ToLowerInvariant()
            }));
        });
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Api-Key", "demo-admin-key");
        using var clinician = factory.CreateClient();
        clinician.DefaultRequestHeaders.Add("X-Api-Key", "demo-clinician-key");
        using var foreign = factory.CreateClient();
        foreign.DefaultRequestHeaders.Add("X-Api-Key", "test-other-key");
        using var anonymous = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
            db.Facilities.Add(new(otherFacility, "Other facility"));
            await db.SaveChangesAsync();
        }
        var at = DateTimeOffset.UtcNow.AddDays(5);
        var createdIds = new List<Guid>();
        var room = "R-" + doctorId;
        object Appointment(DateTimeOffset date, string? doctor = null, string? selectedRoom = null, int duration = 30) => new
        {
            patientName = "Pacjent Testowy", phone = "+48500100200", email = "patient@example.invalid",
            doctorId = doctor ?? doctorId, scheduledAt = date, durationMinutes = duration, room = selectedRoom ?? room
        };
        async Task<JsonElement> Created(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var value = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (value.TryGetProperty("appointment", out var appointment)) createdIds.Add(appointment.GetProperty("visitId").GetGuid());
            return value;
        }
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/doctors")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync("/api/v1/admin/appointments")).StatusCode);
            await Created(await admin.PostAsJsonAsync("/api/v1/admin/doctors", new { id = doctorId, name = "Lekarz Testowy", specialty = "Internista", defaultRoom = room }));
            await Created(await admin.PostAsJsonAsync("/api/v1/admin/doctors", new { id = secondDoctorId, name = "Drugi Lekarz", specialty = "Internista" }));
            var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@docprep.local", password = "DocPrepDemo!2026" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            anonymous.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/admin/doctors")).StatusCode);
            anonymous.DefaultRequestHeaders.Authorization = null;

            var first = await Created(await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at)));
            var visit = first.GetProperty("appointment");
            var id = visit.GetProperty("visitId").GetGuid();
            var url = first.GetProperty("invitation").GetProperty("url").GetString()!;
            Assert.StartsWith("http://127.0.0.1:5173/i/", url);
            Assert.Equal("Pending", visit.GetProperty("deliveryStatus").GetString());
            Assert.Equal(30, visit.GetProperty("durationMinutes").GetInt32());
            Assert.False(visit.TryGetProperty("report", out _));
            Assert.Equal("no-store", (await admin.GetAsync($"/api/v1/admin/appointments/{id}")).Headers.CacheControl?.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/admin/appointments/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/admin/appointments/{id}/invitation")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await foreign.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddMinutes(15)))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddMinutes(15), secondDoctorId))).StatusCode);
            await Created(await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddMinutes(30))));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddDays(1), duration: 0))).StatusCode);

            var calendarPath = $"/api/v1/admin/calendar?from={Uri.EscapeDataString(at.AddMinutes(5).ToString("O"))}&to={Uri.EscapeDataString(at.AddMinutes(15).ToString("O"))}&doctorId={doctorId}";
            var calendar = await admin.GetFromJsonAsync<JsonElement>(calendarPath);
            Assert.Single(calendar.EnumerateArray());
            Assert.Equal(id, calendar[0].GetProperty("visitId").GetGuid());
            var search = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/appointments?q=Pacjent%20Testowy&doctorId={doctorId}&pageSize=1");
            Assert.Equal(2, search.GetProperty("total").GetInt32());
            Assert.Single(search.GetProperty("items").EnumerateArray());

            var sent = await admin.PostAsJsonAsync($"/api/v1/admin/appointments/{id}/invitation/send", new { channel = "Email" });
            Assert.True(sent.StatusCode == HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
            Assert.Equal("demo", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deliveryMode").GetString());
            Assert.Equal(url, (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/appointments/{id}/invitation")).GetProperty("url").GetString());
            var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/appointments/{id}")).GetProperty("version").GetUInt32();
            var update = new { scheduledAt = at.AddHours(2), durationMinutes = 45, doctorId, room, expectedVersion = version };
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/admin/appointments/{id}", update)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/admin/appointments/{id}", update)).StatusCode);
            var regenerated = await admin.PostAsJsonAsync($"/api/v1/admin/appointments/{id}/invitation/regenerate", new { channel = "Sms" });
            Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
            Assert.NotEqual(url, (await regenerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString());
            var oldToken = url.Split('/').Last();
            Assert.Equal(HttpStatusCode.Conflict, (await anonymous.GetAsync($"/api/public/interviews/{oldToken}")).StatusCode);

            var concurrent = await Task.WhenAll(
                admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddDays(2))),
                admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddDays(2))));
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
            await Created(concurrent.Single(x => x.StatusCode == HttpStatusCode.Created));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/appointments/{id}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.GetAsync($"/api/v1/admin/appointments/{id}/invitation")).StatusCode);
            await Created(await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddHours(2))));

            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/admin/doctors/{doctorId}", new { name = "Lekarz Testowy", specialty = "Internista", isActive = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/appointments", Appointment(at.AddDays(3)))).StatusCode);
            using var verificationScope = factory.Services.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
            Assert.DoesNotContain("Pacjent Testowy", (await verificationDb.ReceptionDetails.FindAsync(id))!.EncryptedPatient);
            Assert.DoesNotContain(oldToken, (await verificationDb.InterviewInvitations.FirstAsync(x => x.InterviewId == first.GetProperty("invitation").GetProperty("interviewId").GetGuid())).EncryptedToken!);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
            await db.Visits.Where(x => createdIds.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Clinicians.Where(x => x.Id == doctorId || x.Id == secondDoctorId).ExecuteDeleteAsync();
            await db.Facilities.Where(x => x.Id == otherFacility).ExecuteDeleteAsync();
        }
    }
}
