using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocPrep.Api;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Tenancy;
using DocPrep.Domain.Visits;
using DocPrep.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocPrep.UnitTests;

[Collection("Postgres")]
public sealed class MvpFlowTests
{
    [PostgresFact]
    public async Task Doctor_queue_is_scoped_by_credentials_and_exposes_only_patient_name_and_real_duration()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient();
        await Login(admin, "admin@docprep.local");
        var doctorA = await CreateDoctor(admin, "A");
        var doctorB = await CreateDoctor(admin, "B");
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        await Login(clientA, doctorA.Email);
        await Login(clientB, doctorB.Email);
        var created = new List<Guid>();
        try
        {
            var visitA = await CreateVisit(admin, doctorA.Id, "Pacjent Alfa", 45);
            var idA = VisitId(visitA); created.Add(idA);
            var visitB = await CreateVisit(admin, doctorB.Id, "Pacjent Beta", 20);
            var idB = VisitId(visitB); created.Add(idB);
            var legacyResponse = await admin.PostAsJsonAsync("/api/v1/integration/visits", new
            {
                externalVisitId = "legacy-" + Guid.NewGuid().ToString("N"), pesel = "00000000000",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(6), serviceExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                assignedClinicianId = doctorA.Id, contact = "legacy@example.invalid", channel = "Email"
            });
            var legacy = await Json(legacyResponse, HttpStatusCode.Created);
            var legacyId = legacy.GetProperty("visitId").GetGuid(); created.Add(legacyId);
            // A client-side clinicianId query cannot expand the scope derived from the JWT.
            var listA = await Json(await clientA.GetAsync($"/api/v1/integration/visits?page=1&pageSize=100&clinicianId={doctorB.Id}"));
            Assert.Equal(2, listA.GetProperty("total").GetInt32());
            Assert.All(listA.GetProperty("items").EnumerateArray(), x => Assert.Equal(doctorA.Id,
                x.GetProperty("visit").GetProperty("doctor").GetProperty("id").GetString()));
            var listB = await Json(await clientB.GetAsync("/api/v1/integration/visits?page=1&pageSize=100"));
            Assert.Equal(idB, Assert.Single(listB.GetProperty("items").EnumerateArray()).GetProperty("visitId").GetGuid());
            var statusResponse = await clientA.GetAsync($"/api/v1/integration/visits/{idA}/status");
            Assert.True(statusResponse.Headers.CacheControl?.NoStore);
            var status = await Json(statusResponse);
            Assert.Equal("Pacjent Alfa", status.GetProperty("patientName").GetString());
            Assert.Equal(45, status.GetProperty("durationMinutes").GetInt32());
            Assert.Equal(status.GetProperty("scheduledAt").GetDateTimeOffset().AddMinutes(45), status.GetProperty("endsAt").GetDateTimeOffset());
            Assert.False(status.TryGetProperty("patient", out _));
            Assert.DoesNotContain("patient@example.invalid", status.GetRawText());
            Assert.DoesNotContain("500100200", status.GetRawText());
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/v1/integration/visits/{idB}/status")).StatusCode);
            var legacyStatus = await Json(await clientA.GetAsync($"/api/v1/integration/visits/{legacyId}/status"));
            Assert.Equal(JsonValueKind.Null, legacyStatus.GetProperty("patientName").ValueKind);
            Assert.Equal(30, legacyStatus.GetProperty("durationMinutes").GetInt32());
            var listAdmin = await Json(await admin.GetAsync("/api/v1/integration/visits?page=1&pageSize=100"));
            Assert.All(created, id => Assert.Contains(listAdmin.GetProperty("items").EnumerateArray(), x => x.GetProperty("visitId").GetGuid() == id));
            using var system = factory.CreateClient(); system.DefaultRequestHeaders.Add("X-Api-Key", "demo-system-key");
            Assert.Equal(HttpStatusCode.OK, (await system.GetAsync($"/api/v1/integration/visits/{idB}/status")).StatusCode);
            using var unbound = factory.CreateClient(); unbound.DefaultRequestHeaders.Add("X-Api-Key", "mvp-unbound-clinician");
            Assert.Equal(HttpStatusCode.Forbidden, (await unbound.GetAsync("/api/v1/integration/visits?page=1&pageSize=100")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unbound.GetAsync($"/api/v1/integration/visits/{idA}/status")).StatusCode);
        }
        finally { await Cleanup(factory, created, [doctorA.Email, doctorB.Email]); }
    }

    [PostgresFact]
    public async Task Reception_link_provider_draft_approval_separate_consent_and_same_report_pdf_form_one_api_flow()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient(); await Login(admin, "admin@docprep.local");
        var doctor = await CreateDoctor(admin, "Owner");
        var otherDoctor = await CreateDoctor(admin, "Other");
        using var clinician = factory.CreateClient(); await Login(clinician, doctor.Email);
        using var other = factory.CreateClient(); await Login(other, otherDoctor.Email);
        var created = new List<Guid>();
        try
        {
            var visit = await CreateVisit(admin, doctor.Id, "Pacjent Przepływu", 40);
            var id = VisitId(visit); created.Add(id);
            using var patient = factory.CreateClient();
            var interviewId = await Authorize(patient, visit.GetProperty("invitation").GetProperty("url").GetString()!);
            var credential = await Json(await patient.PostAsJsonAsync($"/api/interviews/{interviewId}/sessions", new { mode = "text" }));
            await Json(await patient.PostAsJsonAsync($"/api/interview-sessions/{credential.GetProperty("sessionId").GetGuid()}/end", new { }), HttpStatusCode.NoContent);
            await Webhook(patient, credential.GetProperty("conversationId").GetString()!);
            var result = await Json(await patient.GetAsync($"/api/interviews/{interviewId}/result"));
            Assert.Equal("completed", result.GetProperty("status").GetString());
            Assert.Equal("ready", result.GetProperty("importStatus").GetString());
            var review = await Json(await patient.GetAsync("/api/v1/interview"));
            Assert.False(review.GetProperty("consentActive").GetBoolean());
            Assert.Equal("Ból głowy", review.GetProperty("draft").GetProperty("consultationReason").GetString());
            Assert.Equal("Paracetamol", Assert.Single(review.GetProperty("draft").GetProperty("medications").EnumerateArray()).GetProperty("name").GetString());
            // The patient corrects imported content using its revision, before approving it.
            var correction = DraftCommand(review, "Pacjent poprawił opis bólu", "Uwagi sprawdzone przez pacjenta");
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", correction));
            Assert.Equal(HttpStatusCode.Conflict, (await patient.PutAsJsonAsync("/api/v1/interview/draft", correction)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await patient.PostAsJsonAsync($"/api/interviews/{interviewId}/result/retry-import", new { })).StatusCode);
            review = await Json(await patient.GetAsync("/api/v1/interview"));
            foreach (var observation in review.GetProperty("observations").EnumerateArray())
                await Json(await patient.PutAsJsonAsync($"/api/v1/interview/observations/{observation.GetProperty("id").GetGuid()}/decision", new { decision = "Accepted" }));
            await Json(await patient.PostAsync("/api/v1/interview/complete", null), HttpStatusCode.NoContent);
            var approved = await Json(await patient.PostAsJsonAsync("/api/v1/interview/approve", new { confirmIncompleteReport = false }));
            var versionId = approved.GetProperty("versionId").GetGuid();
            var versionPath = $"/api/v1/integration/visits/{id}/report-versions/{versionId}";
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(versionPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(versionPath + "/pdf")).StatusCode);
            Assert.False((await Json(await patient.GetAsync("/api/v1/interview"))).GetProperty("consentActive").GetBoolean());
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/consent", new { granted = true }), HttpStatusCode.NoContent);
            var clinicianReport = await Json(await clinician.GetAsync(versionPath));
            var report = clinicianReport.GetProperty("report");
            Assert.Equal(versionId, report.GetProperty("versionId").GetGuid());
            Assert.Equal("Pacjent poprawił opis bólu", report.GetProperty("consultationReason").GetString());
            Assert.Equal("Uwagi sprawdzone przez pacjenta", report.GetProperty("additionalNotes").GetString());
            var patientReport = await Json(await patient.GetAsync("/api/v1/interview/report"));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(report.GetRawText()), JsonNode.Parse(patientReport.GetRawText())),
                "The clinician and patient must read the same immutable report snapshot.");
            var clinicianPdf = await clinician.GetAsync(versionPath + "/pdf");
            Assert.Equal(HttpStatusCode.OK, clinicianPdf.StatusCode);
            Assert.Equal("application/pdf", clinicianPdf.Content.Headers.ContentType?.MediaType);
            var pdfBytes = await clinicianPdf.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));
            using (var pdf = PdfDocument.Open(pdfBytes))
            {
                var text = string.Join("\n", pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
                Assert.Contains(versionId.ToString(), text);
                Assert.Contains("Pacjent poprawił opis bólu", text);
                Assert.Contains("Uwagi sprawdzone przez pacjenta", text);
                Assert.Contains("Paracetamol", text);
                Assert.Contains("500 mg", text);
            }
            Assert.Equal(pdfBytes, await (await patient.GetAsync("/api/v1/interview/report.pdf")).Content.ReadAsByteArrayAsync());
            await Json(await patient.PostAsync("/api/v1/interview/report/regenerate-pdf", null), HttpStatusCode.NoContent);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(patientReport.GetRawText()),
                JsonNode.Parse((await Json(await patient.GetAsync("/api/v1/interview/report"))).GetRawText())));
            using (var regeneratedPdf = PdfDocument.Open(await (await patient.GetAsync("/api/v1/interview/report.pdf")).Content.ReadAsByteArrayAsync()))
            {
                var text = string.Join("\n", regeneratedPdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
                Assert.Contains(versionId.ToString(), text);
                Assert.Contains("Pacjent poprawił opis bólu", text);
            }
            var versions = await Json(await clinician.GetAsync($"/api/v1/integration/visits/{id}/report-versions"));
            Assert.Equal(versionId, Assert.Single(versions.EnumerateArray()).GetProperty("versionId").GetGuid());
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(versionPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(versionPath + "/pdf")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(versionPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(versionPath + "/pdf")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/integration/visits/{id}/status")).StatusCode);
            // Editing the draft keeps the approved snapshot visible while the existing consent remains active.
            review = await Json(await patient.GetAsync("/api/v1/interview"));
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", DraftCommand(review, "Nowsza niezatwierdzona edycja", "Nowa uwaga")));
            Assert.Equal(report.GetRawText(), (await Json(await clinician.GetAsync(versionPath))).GetProperty("report").GetRawText());
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/consent", new { granted = false }), HttpStatusCode.NoContent);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(versionPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(versionPath + "/pdf")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync($"/api/v1/integration/visits/{id}/report-versions")).StatusCode);
        }
        finally { await Cleanup(factory, created, [doctor.Email, otherDoctor.Email]); }
    }

    [PostgresFact]
    public async Task First_webhook_does_not_overwrite_a_manual_draft_and_concurrent_revisions_are_rejected()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient(); await Login(admin, "admin@docprep.local");
        var doctor = await CreateDoctor(admin, "Manual");
        var created = new List<Guid>();
        try
        {
            var visit = await CreateVisit(admin, doctor.Id, "Pacjent Ręcznej Edycji", 30);
            var id = VisitId(visit); created.Add(id);
            using var patient = factory.CreateClient();
            var interviewId = await Authorize(patient, visit.GetProperty("invitation").GetProperty("url").GetString()!);
            var credential = await Json(await patient.PostAsJsonAsync($"/api/interviews/{interviewId}/sessions", new { mode = "text" }));
            var review = await Json(await patient.GetAsync("/api/v1/interview"));
            var manual = DraftCommand(review, "Ręczny opis przed pierwszym importem", "Zachowaj tę poprawkę");
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", manual));
            await Webhook(patient, credential.GetProperty("conversationId").GetString()!);
            var result = await Json(await patient.GetAsync($"/api/interviews/{interviewId}/result"));
            Assert.Equal("failed", result.GetProperty("importStatus").GetString());
            review = await Json(await patient.GetAsync("/api/v1/interview"));
            Assert.Equal("Ręczny opis przed pierwszym importem", review.GetProperty("draft").GetProperty("consultationReason").GetString());
            Assert.Equal("Zachowaj tę poprawkę", review.GetProperty("draft").GetProperty("additionalNotes").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await patient.PostAsJsonAsync($"/api/interviews/{interviewId}/result/retry-import", new { })).StatusCode);
            using var scopeA = factory.Services.CreateScope();
            using var scopeB = factory.Services.CreateScope();
            var dbA = scopeA.ServiceProvider.GetRequiredService<DocPrepDbContext>();
            var dbB = scopeB.ServiceProvider.GetRequiredService<DocPrepDbContext>();
            var draftA = await dbA.Drafts.SingleAsync(x => x.VisitProcessId == id);
            var draftB = await dbB.Drafts.SingleAsync(x => x.VisitProcessId == id);
            draftA.Replace("Pierwsza równoczesna korekta", [], [], [], [], [], DateTimeOffset.UtcNow);
            draftB.Replace("Druga nieaktualna korekta", [], [], [], [], [], DateTimeOffset.UtcNow);
            await dbA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
            dbA.ChangeTracker.Clear();
            Assert.Equal("Pierwsza równoczesna korekta", (await dbA.Drafts.SingleAsync(x => x.VisitProcessId == id)).ConsultationReason);
        }
        finally { await Cleanup(factory, created, [doctor.Email]); }
    }

    [PostgresFact]
    public async Task One_approval_accepts_pending_observations_and_shares_corrected_report_without_separate_decisions_or_consent()
    {
        await using var factory = Factory();
        using var admin = factory.CreateClient(); await Login(admin, "admin@docprep.local");
        var doctor = await CreateDoctor(admin, "SingleApproval");
        using var clinician = factory.CreateClient(); await Login(clinician, doctor.Email);
        var created = new List<Guid>();
        try
        {
            var visit = await CreateVisit(admin, doctor.Id, "Pacjent Jednego Zatwierdzenia", 30);
            var id = VisitId(visit); created.Add(id);
            using var patient = factory.CreateClient();
            var review = await ImportInterview(patient, visit, "Penicylina");
            Assert.Equal("Paracetamol", review.GetProperty("draft").GetProperty("medications")[0].GetProperty("name").GetString());
            Assert.Equal("Penicylina", review.GetProperty("draft").GetProperty("allergies")[0].GetProperty("substance").GetString());
            var corrected = DraftCommand(review, "Opis sprawdzony przed udostępnieniem", "Poprawione leki i alergeny");
            corrected["medications"]![0]!["name"] = "Metamizol";
            corrected["allergies"]![0]!["substance"] = "Cefalosporyny";
            review = await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", corrected));
            var pending = Assert.Single(review.GetProperty("observations").EnumerateArray());
            Assert.Equal("Pending", pending.GetProperty("decision").GetString());
            Assert.Equal("Nowo zgłoszony objaw: Ból głowy.", pending.GetProperty("text").GetString());

            // Existing final decisions must survive bulk approval; only Pending changes to Accepted.
            Guid rejectedId, editedId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                var draftId = await db.Drafts.Where(x => x.VisitProcessId == id).Select(x => x.Id).SingleAsync();
                var rejected = new ObservationProposal(draftId, "Odrzucony objaw", ObservationKind.New, "Odrzucona obserwacja", DateTimeOffset.UtcNow);
                rejected.Decide(ObservationDecision.Rejected, null, DateTimeOffset.UtcNow);
                var edited = new ObservationProposal(draftId, "Poprawiony objaw", ObservationKind.New, "Pierwotna obserwacja", DateTimeOffset.UtcNow);
                edited.Decide(ObservationDecision.EditedAndAccepted, "Pacjent poprawił obserwację", DateTimeOffset.UtcNow);
                db.Observations.AddRange(rejected, edited); await db.SaveChangesAsync();
                rejectedId = rejected.Id; editedId = edited.Id;
            }
            var approved = await Json(await patient.PostAsJsonAsync("/api/v1/interview/approve", new
            { confirmIncompleteReport = false, acceptAllObservations = true, shareWithFacility = true }));
            var versionId = approved.GetProperty("versionId").GetGuid();
            var path = $"/api/v1/integration/visits/{id}/report-versions/{versionId}";
            var report = (await Json(await clinician.GetAsync(path))).GetProperty("report");
            Assert.Equal("Metamizol", report.GetProperty("medications")[0].GetProperty("name").GetString());
            Assert.Equal("Cefalosporyny", report.GetProperty("allergies")[0].GetProperty("substance").GetString());
            Assert.Equal(2, report.GetProperty("observations").GetArrayLength());
            Assert.DoesNotContain(report.GetProperty("observations").EnumerateArray(), x => x.GetProperty("observationId").GetGuid() == rejectedId);
            Assert.Contains(report.GetProperty("observations").EnumerateArray(), x => x.GetProperty("observationId").GetGuid() == editedId &&
                x.GetProperty("text").GetString() == "Pacjent poprawił obserwację" && x.GetProperty("editedByPatient").GetBoolean());
            var pdfBytes = await (await clinician.GetAsync(path + "/pdf")).Content.ReadAsByteArrayAsync();
            using (var pdf = PdfDocument.Open(pdfBytes))
            {
                var text = string.Join("\n", pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
                Assert.Contains("Metamizol", text); Assert.Contains("Cefalosporyny", text);
                Assert.Contains("Nowo zgłoszony objaw: Ból głowy.", text);
                Assert.DoesNotContain("Paracetamol", text); Assert.DoesNotContain("Penicylina", text);
            }
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                var storedVisit = await db.Visits.SingleAsync(x => x.Id == id);
                Assert.Equal(VisitStatus.Shared, storedVisit.Status); Assert.Equal(versionId, storedVisit.LatestSharedVersionId);
                Assert.Single(await db.Consents.Where(x => x.VisitProcessId == id && x.RevokedAt == null).ToListAsync());
                Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.VisitProcessId == id && x.Action == "consent.granted"));
                Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.VisitProcessId == id && x.Action == $"report.approved:{versionId}"));
                Assert.Equal(ObservationDecision.Accepted, (await db.Observations.SingleAsync(x => x.Id == pending.GetProperty("id").GetGuid())).Decision);
                Assert.Equal(ObservationDecision.Rejected, (await db.Observations.SingleAsync(x => x.Id == rejectedId)).Decision);
                Assert.Equal(ObservationDecision.EditedAndAccepted, (await db.Observations.SingleAsync(x => x.Id == editedId)).Decision);
            }
            review = await Json(await patient.GetAsync("/api/v1/interview"));
            Assert.True(review.GetProperty("consentActive").GetBoolean());
            var newer = DraftCommand(review, "Kolejna niezatwierdzona korekta", "Nowsza uwaga");
            newer["medications"]![0]!["name"] = "Nowy lek";
            newer["allergies"]![0]!["substance"] = "Nowy alergen";
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", newer));
            Assert.Equal(report.GetRawText(), (await Json(await clinician.GetAsync(path))).GetProperty("report").GetRawText());
            Assert.Equal(pdfBytes, await (await clinician.GetAsync(path + "/pdf")).Content.ReadAsByteArrayAsync());
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/consent", new { granted = false }), HttpStatusCode.NoContent);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(path)).StatusCode);
        }
        finally { await Cleanup(factory, created, [doctor.Email]); }
    }

    [PostgresFact]
    public async Task Failed_pdf_keeps_approved_snapshot_without_granting_consent_and_preserves_previous_shared_version()
    {
        var failures = new PdfFailures();
        await using var factory = Factory(failures);
        using var admin = factory.CreateClient(); await Login(admin, "admin@docprep.local");
        var doctor = await CreateDoctor(admin, "PdfFailure");
        using var clinician = factory.CreateClient(); await Login(clinician, doctor.Email);
        var created = new List<Guid>();
        try
        {
            var visit = await CreateVisit(admin, doctor.Id, "Pacjent Ponownej Generacji", 30);
            var id = VisitId(visit); created.Add(id);
            using var patient = factory.CreateClient();
            await ImportInterview(patient, visit);
            failures.FailNext = true;
            var error = await Json(await patient.PostAsJsonAsync("/api/v1/interview/approve", new
            { confirmIncompleteReport = false, acceptAllObservations = true, shareWithFacility = true }), HttpStatusCode.Conflict);
            Assert.Equal("report.generation_failed", error.GetProperty("code").GetString());
            ReportVersion failed;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                failed = await db.ReportVersions.AsNoTracking().SingleAsync(x => x.VisitProcessId == id);
                Assert.Equal(PdfGenerationStatus.Failed, failed.PdfStatus);
                Assert.False(await db.Consents.AnyAsync(x => x.VisitProcessId == id));
                Assert.False(await db.AuditEvents.AnyAsync(x => x.VisitProcessId == id && x.Action == "consent.granted"));
                Assert.Null((await db.Visits.SingleAsync(x => x.Id == id)).LatestSharedVersionId);
            }
            var path = $"/api/v1/integration/visits/{id}/report-versions/{failed.Id}";
            Assert.Equal(HttpStatusCode.Forbidden, (await clinician.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await patient.PutAsJsonAsync("/api/v1/interview/consent", new { granted = true })).StatusCode);
            var review = await Json(await patient.GetAsync("/api/v1/interview"));
            Assert.Equal(1, review.GetProperty("latestVersion").GetInt32()); Assert.False(review.GetProperty("consentActive").GetBoolean());

            // The front retries PDF and grants consent, without another approval or another version.
            await Json(await patient.PostAsync("/api/v1/interview/report/regenerate-pdf", null), HttpStatusCode.NoContent);
            Assert.False((await Json(await patient.GetAsync("/api/v1/interview"))).GetProperty("consentActive").GetBoolean());
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/consent", new { granted = true }), HttpStatusCode.NoContent);
            var report = (await Json(await clinician.GetAsync(path))).GetProperty("report");
            var originalPdf = await (await clinician.GetAsync(path + "/pdf")).Content.ReadAsByteArrayAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                var ready = Assert.Single(await db.ReportVersions.AsNoTracking().Where(x => x.VisitProcessId == id).ToListAsync());
                Assert.Equal(failed.Id, ready.Id); Assert.Equal(failed.SnapshotJson, ready.SnapshotJson);
                Assert.Equal(failed.JsonSha256, ready.JsonSha256); Assert.Equal(PdfGenerationStatus.Ready, ready.PdfStatus);
            }
            review = await Json(await patient.GetAsync("/api/v1/interview"));
            await Json(await patient.PutAsJsonAsync("/api/v1/interview/draft", DraftCommand(review, "Nowa wersja z błędem PDF", "Nowsza zatwierdzona uwaga")));
            failures.FailNext = true;
            await Json(await patient.PostAsJsonAsync("/api/v1/interview/approve", new
            { confirmIncompleteReport = false, acceptAllObservations = true, shareWithFacility = true }), HttpStatusCode.Conflict);
            Assert.Equal(report.GetRawText(), (await Json(await clinician.GetAsync(path))).GetProperty("report").GetRawText());
            Assert.Equal(originalPdf, await (await clinician.GetAsync(path + "/pdf")).Content.ReadAsByteArrayAsync());
            var versions = await Json(await clinician.GetAsync($"/api/v1/integration/visits/{id}/report-versions"));
            Assert.Equal(failed.Id, Assert.Single(versions.EnumerateArray()).GetProperty("versionId").GetGuid());
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
                var storedVisit = await db.Visits.SingleAsync(x => x.Id == id);
                Assert.Equal(failed.Id, storedVisit.LatestSharedVersionId);
                Assert.NotEqual(failed.Id, storedVisit.LatestApprovedVersionId);
                var newer = await db.ReportVersions.SingleAsync(x => x.VisitProcessId == id && x.VersionNumber == 2);
                Assert.Equal(PdfGenerationStatus.Failed, newer.PdfStatus);
                Assert.Contains("Nowa wersja z błędem PDF", JsonSerializer.Deserialize<ReportSnapshot>(newer.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ConsultationReason);
                Assert.Equal(failed.SnapshotJson, (await db.ReportVersions.SingleAsync(x => x.Id == failed.Id)).SnapshotJson);
                Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.VisitProcessId == id && x.Action == "consent.granted"));
            }
        }
        finally { await Cleanup(factory, created, [doctor.Email]); }
    }

    private static WebApplicationFactory<global::Program> Factory(PdfFailures? failures = null) =>
        new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("DOCPREP_TEST_POSTGRES"),
                ["Authentication:SeedUsers:Enabled"] = "true",
                ["ElevenLabs:AgentId"] = "agent-mvp-test",
                ["ElevenLabs:WebhookSecret"] = "mvp-webhook-secret"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IElevenLabsClient>(); services.AddSingleton<IElevenLabsClient>(new Provider());
                services.RemoveAll<IPatientSessionStore>(); services.AddSingleton<IPatientSessionStore, AgentEndpointsTests.MemorySessions>();
                if (failures is not null)
                {
                    var rendererType = services.Single(x => x.ServiceType == typeof(IReportRenderer)).ImplementationType!;
                    services.RemoveAll<IReportRenderer>();
                    services.AddSingleton<IReportRenderer>(provider => new ControlledRenderer(
                        (IReportRenderer)ActivatorUtilities.CreateInstance(provider, rendererType), failures));
                }
                services.PostConfigure<IntegrationOptions>(options => options.Clients.Add(new(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"), "Unbound test clinician", FacilityRole.Clinician, null,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("mvp-unbound-clinician"))))));
            });
        });

    private static async Task Login(HttpClient client, string email)
    {
        var result = await Json(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "DocPrepDemo!2026" }));
        client.DefaultRequestHeaders.Authorization = new("Bearer", result.GetProperty("accessToken").GetString());
    }
    private static async Task<(string Id, string Email)> CreateDoctor(HttpClient admin, string name)
    {
        var id = "mvp-" + Guid.NewGuid().ToString("N"); var email = id + "@example.invalid";
        await Json(await admin.PostAsJsonAsync("/api/v1/staff", new
        { email, displayName = "Lekarz " + name, role = "Clinician", clinicianId = id, password = "DocPrepDemo!2026" }), HttpStatusCode.Created);
        return (id, email);
    }
    private static async Task<JsonElement> CreateVisit(HttpClient admin, string doctorId, string name, int duration) =>
        await Json(await admin.PostAsJsonAsync("/api/v1/admin/appointments", new
        {
            patientName = name, phone = "+48500100200", email = "patient@example.invalid", doctorId,
            scheduledAt = DateTimeOffset.UtcNow.AddDays(4), durationMinutes = duration, room = doctorId, sendInvitation = false
        }), HttpStatusCode.Created);
    private static Guid VisitId(JsonElement visit) => visit.GetProperty("appointment").GetProperty("visitId").GetGuid();
    private static async Task<Guid> Authorize(HttpClient patient, string url)
    {
        var token = new Uri(url).Segments.Last();
        await Json(await patient.GetAsync($"/api/public/interviews/{token}"));
        var access = await Json(await patient.PostAsJsonAsync($"/api/public/interviews/{token}/authorize", new { }));
        patient.DefaultRequestHeaders.Authorization = new("Bearer", access.GetProperty("accessToken").GetString());
        return access.GetProperty("interviewId").GetGuid();
    }
    private static JsonObject DraftCommand(JsonElement review, string reason, string notes)
    {
        var draft = JsonNode.Parse(review.GetProperty("draft").GetRawText())!.AsObject();
        draft["expectedRevision"] = draft["revision"]!.GetValue<int>();
        draft["consultationReason"] = reason; draft["additionalNotes"] = notes;
        draft.Remove("revision"); draft.Remove("clarifications");
        return draft;
    }
    private static async Task<JsonElement> ImportInterview(HttpClient patient, JsonElement visit, string? allergy = null)
    {
        var interviewId = await Authorize(patient, visit.GetProperty("invitation").GetProperty("url").GetString()!);
        var credential = await Json(await patient.PostAsJsonAsync($"/api/interviews/{interviewId}/sessions", new { mode = "text" }));
        await Json(await patient.PostAsJsonAsync($"/api/interview-sessions/{credential.GetProperty("sessionId").GetGuid()}/end", new { }), HttpStatusCode.NoContent);
        await Webhook(patient, credential.GetProperty("conversationId").GetString()!, allergy);
        return await Json(await patient.GetAsync("/api/v1/interview"));
    }
    private static async Task Webhook(HttpClient patient, string conversationId, string? allergy = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = JsonSerializer.Serialize(new
        {
            type = "post_call_transcription", event_timestamp = timestamp,
            data = new
            {
                agent_id = "agent-mvp-test", conversation_id = conversationId, status = "done",
                transcript = new[] { new { role = "user", message = "Ból głowy. Biorę paracetamol." } },
                analysis = new
                {
                    transcript_summary = "Pacjent zgłasza ból głowy.",
                    data_collection_results = new { interview_json = new { value = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1, consultationReason = "Ból głowy",
                        symptoms = new[] { new { name = "Ból głowy", startedOn = "2026-10-01", startedOnState = "provided", severity = 5, timeline = Array.Empty<object>() } },
                        medications = new[] { new { name = "Paracetamol", dose = "500 mg", doseState = "provided", schedule = "doraźnie", reason = "ból" } },
                        allergies = allergy is null ? Array.Empty<object>() : [new { substance = allergy, reaction = "wysypka" }],
                        chronicConditions = Array.Empty<object>(), patientQuestions = Array.Empty<string>(),
                        medicationsState = "provided", allergiesState = "provided", chronicConditionsState = "provided"
                    }) } }
                }, metadata = new { }
            }
        });
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("mvp-webhook-secret"), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/elevenlabs")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("ElevenLabs-Signature", $"t={timestamp},v0={signature}");
        await Json(await patient.SendAsync(request));
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return expected == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Cleanup(WebApplicationFactory<global::Program> factory, List<Guid> visits, string[] emails)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocPrepDbContext>();
        await db.Visits.Where(x => visits.Contains(x.Id)).ExecuteDeleteAsync();
        var normalized = emails.Select(StaffUser.NormalizeEmail).ToArray();
        var clinicianIds = await db.StaffUsers.Where(x => normalized.Contains(x.Email)).Select(x => x.ClinicianId).ToListAsync();
        await db.StaffUsers.Where(x => normalized.Contains(x.Email)).ExecuteDeleteAsync();
        await db.Clinicians.Where(x => x.FacilityId == Guid.Parse("11111111-1111-1111-1111-111111111111") && clinicianIds.Contains(x.Id)).ExecuteDeleteAsync();
    }
    private sealed class Provider : IElevenLabsClient
    {
        public Task<ElevenLabsCredential> CreateVoiceCredential(string participantName, CancellationToken ct) =>
            Task.FromResult(new ElevenLabsCredential("mvp-test-voice", null, "conv_" + Guid.NewGuid().ToString("N")));
        public Task<ElevenLabsCredential> CreateTextCredential(string participantName, CancellationToken ct) =>
            Task.FromResult(new ElevenLabsCredential(null, "wss://mvp-test.invalid", "conv_" + Guid.NewGuid().ToString("N")));
    }
    private sealed class PdfFailures { public bool FailNext { get; set; } }
    private sealed class ControlledRenderer(IReportRenderer renderer, PdfFailures failures) : IReportRenderer
    {
        public byte[] Render(ReportSnapshot snapshot)
        {
            if (failures.FailNext) { failures.FailNext = false; throw new InvalidOperationException("Simulated PDF rendering failure."); }
            return renderer.Render(snapshot);
        }
    }
}
