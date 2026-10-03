using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Contracts;
using DocPrep.Domain.Interviews;
using DocPrep.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DocPrep.Infrastructure.Services;

internal sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

internal sealed class DemoNotificationSender(ILogger<DemoNotificationSender> logger, IHostEnvironment environment) : INotificationSender
{
    public Task<NotificationResult> SendInterviewLink(string channel, string destination, string interviewInvitationToken, CancellationToken ct) =>
        Send(channel, destination, "", "", interviewInvitationToken, ct);
    public Task<NotificationResult> Send(string channel, string destination, string linkToken, string visitCode, string interviewInvitationToken, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return Task.FromResult(new NotificationResult(false, null, "Notification provider is not configured."));
        logger.LogInformation("Demo {Channel} invitation accepted by notification adapter", channel);
        return Task.FromResult(new NotificationResult(true, $"demo-{Guid.NewGuid():N}", null, true));
    }
    public Task<NotificationResult> SendSupplementation(string channel, string destination, string interviewInvitationToken, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return Task.FromResult(new NotificationResult(false, null, "Notification provider is not configured."));
        logger.LogInformation("Demo {Channel} supplementation notification accepted by notification adapter", channel);
        return Task.FromResult(new NotificationResult(true, $"demo-{Guid.NewGuid():N}", null, true));
    }
}

public sealed class NotificationOptions
{
    public const string Section = "Notifications";
    public string ProviderUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string FrontendBaseUrl { get; init; } = "http://localhost:5173";
}

internal sealed class HttpNotificationSender(HttpClient http, Microsoft.Extensions.Options.IOptions<NotificationOptions> options) : INotificationSender
{
    private readonly NotificationOptions settings = options.Value;
    public Task<NotificationResult> SendInterviewLink(string channel, string destination, string interviewInvitationToken, CancellationToken ct) =>
        SendPayload(new { channel, destination, kind = "invitation",
            interviewUrl = $"{settings.FrontendBaseUrl.TrimEnd('/')}/i/{Uri.EscapeDataString(interviewInvitationToken)}" }, ct);
    public Task<NotificationResult> Send(string channel, string destination, string linkToken, string visitCode,
        string interviewInvitationToken, CancellationToken ct) => SendPayload(new
        {
            channel, destination, visitCode,
            interviewUrl = $"{settings.FrontendBaseUrl.TrimEnd('/')}/i/{Uri.EscapeDataString(interviewInvitationToken)}",
            accessUrl = $"{settings.FrontendBaseUrl.TrimEnd('/')}/access/{Uri.EscapeDataString(linkToken)}"
        }, ct);

    public Task<NotificationResult> SendSupplementation(string channel, string destination, string interviewInvitationToken, CancellationToken ct) =>
        SendPayload(new { channel, destination, kind = "supplementation", returnUrl = $"{settings.FrontendBaseUrl.TrimEnd('/')}/i/{Uri.EscapeDataString(interviewInvitationToken)}" }, ct);

    private async Task<NotificationResult> SendPayload(object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ProviderUrl)) return new(false, null, "Notification provider is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ProviderUrl) { Content = System.Net.Http.Json.JsonContent.Create(payload) };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(false, null, $"Provider returned {(int)response.StatusCode}.");
            var providerId = response.Headers.TryGetValues("X-Message-Id", out var values) ? values.FirstOrDefault() : null;
            return new(true, providerId, null);
        }
        catch (HttpRequestException ex) { return new(false, null, ex.GetType().Name); }
    }
}

internal sealed class HttpTranscriptionService(HttpClient http, IConfiguration configuration) : ITranscriptionService
{
    public async Task<string> Transcribe(Stream audio, string contentType, CancellationToken ct)
    {
        var endpoint = configuration["Transcription:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ConflictError("transcription.unavailable", "Voice transcription is not configured; use text mode.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint); using var content = new StreamContent(audio);
        content.Headers.ContentType = new(contentType); request.Content = content;
        var key = configuration["Transcription:ApiKey"]; if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("X-Api-Key", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new ConflictError("transcription.failed", "Audio transcription failed; no audio was retained.");
        var payload = JsonSerializer.Deserialize<TranscriptionPayload>(await response.Content.ReadAsStringAsync(ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return string.IsNullOrWhiteSpace(payload?.Text) ? throw new ConflictError("transcription.empty", "Transcription returned no text.") : payload.Text;
    }
    private sealed record TranscriptionPayload(string Text);
}

internal sealed class AdaptiveQuestionProvider : IInterviewQuestionProvider
{
    public Task<string?> Next(InterviewDraft draft, CancellationToken ct)
    {
        string? next = draft.Answers.Count switch
        {
            0 => "Jaki jest główny powód konsultacji?",
            1 => "Kiedy pojawiły się objawy i jak zmieniały się w czasie?",
            2 => "Jak często występują i jakie mają nasilenie w skali 0–10?",
            3 => "Jak wpływają na codzienne funkcjonowanie, sen lub pracę?",
            4 => "Jakie leki i suplementy przyjmujesz, w jakich dawkach?",
            5 => "Czy masz alergie lub choroby przewlekłe?",
            6 => "Jakie pytania chcesz zadać lekarzowi?",
            _ => null
        };
        return Task.FromResult(next);
    }
}

internal sealed class QuestReportRenderer : IReportRenderer
{
    public QuestReportRenderer() => QuestPDF.Settings.License = LicenseType.Community;
    public byte[] Render(ReportSnapshot report) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4); page.Margin(22); page.DefaultTextStyle(x => x.FontSize(8));
        page.Header().Column(c => { c.Item().Text("DocPrep — raport przygotowania do wizyty").Bold().FontSize(14); c.Item().Text($"Wersja {report.VersionNumber} • {report.ApprovedAt:yyyy-MM-dd HH:mm} UTC • {report.VersionId}").FontColor(Colors.Grey.Darken1); });
        page.Content().PaddingVertical(8).Column(c =>
        {
            Section(c, "Powód konsultacji", [report.ConsultationReason]);
            Section(c, "Objawy", report.Symptoms.Select(x => $"{x.Name}: od {x.StartedOn?.ToString() ?? "nieznane"}, częstość {x.Frequency ?? "nieznana"}, nasilenie {x.Severity?.ToString() ?? "nieznane"}/10. {x.DailyImpact} {x.Description} Chronologia: {string.Join("; ", x.Timeline.Select(t => $"{t.OccurredOn?.ToString() ?? t.Period ?? "czas nieznany"}: {t.Description}"))}"));
            Section(c, "Leki", report.Medications.Select(x => $"{x.Name}, dawka: {x.Dose ?? "nieznana"}, schemat: {x.Schedule ?? "nieznany"}, powód: {x.Reason ?? "niepodany"}"));
            Section(c, "Alergie", report.Allergies.Select(x => $"{x.Substance}: {x.Reaction ?? "reakcja nieznana"}"));
            Section(c, "Choroby przewlekłe", report.ChronicConditions.Select(x => $"{x.Name}: {x.Description}"));
            Section(c, "Pytania pacjenta", report.PatientQuestions);
            Section(c, "Dodatkowe uwagi", [report.AdditionalNotes]);
            Section(c, "Braki i sprzeczności", report.Clarifications.Select(x => x.Message));
            Section(c, "Zatwierdzone obserwacje", report.Observations.Select(x => $"{x.SymptomName}: {x.Text}{(x.EditedByPatient ? " [zmiana pacjenta]" : "")}"));
            Section(c, "Odpowiedzi uzupełniające", report.SupplementationAnswers.Select(x => $"{x.Question} — {x.Answer}"));
        });
        page.Footer().AlignCenter().Text("Informacje pochodzą od pacjenta; raport nie stanowi diagnozy ani zalecenia leczenia.").FontSize(7).FontColor(Colors.Grey.Darken1);
    })).GeneratePdf();

    private static void Section(ColumnDescriptor column, string title, IEnumerable<string?> values)
    {
        var items = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); if (items.Count == 0) return;
        column.Item().PaddingTop(4).Text(title).SemiBold().FontSize(9); foreach (var item in items) column.Item().PaddingLeft(6).Text("• " + item);
    }
}
