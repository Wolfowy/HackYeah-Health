using System.Text.Json;
using DocPrep.Domain.Interviews;

namespace DocPrep.Application.Interviews;

public static class InterviewContinuationContext
{
    public const int MaxLength = 6000;

    public static string Build(IEnumerable<AgentInterviewSession> sessions, string? previousReport = null,
        IEnumerable<string>? supplementationQuestions = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousReport)) parts.Add("Poprzedni wywiad: " + previousReport);
        foreach (var session in sessions.OrderBy(x => x.CreatedAt))
        {
            var summary = Summary(session.AnalysisJson);
            if (!string.IsNullOrWhiteSpace(summary)) parts.Add("Podsumowanie sesji: " + summary);
            else
            {
                var messages = Messages(session.TranscriptJson);
                if (messages.Count > 0) parts.Add("Fragment wcześniejszej rozmowy:\n" + string.Join('\n', messages));
            }
        }
        if (supplementationQuestions is not null)
        {
            var questions = supplementationQuestions.ToList();
            if (questions.Count > 0) parts.Add("Pytania lekarza do uzupełnienia:\n" + string.Join('\n', questions));
        }
        // Preserve recent information when earlier sessions have large summaries.
        var context = string.Join("\n\n", parts);
        return context.Length <= MaxLength ? context : "[Starszy kontekst skrócono]\n" + context[^(MaxLength - 28)..];
    }

    private static string? Summary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("transcript_summary", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<string> Messages(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var entry in document.RootElement.EnumerateArray().TakeLast(12))
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String) continue;
                var text = message.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var role = entry.TryGetProperty("role", out var roleValue) && roleValue.ValueKind == JsonValueKind.String && roleValue.GetString() == "user" ? "Pacjent" : "Asystent";
                result.Add($"{role}: {text[..Math.Min(text.Length, 400)]}");
            }
        }
        catch (JsonException) { }
        return result;
    }
}
