using System.Globalization;
using System.Text;
using HealthPrep.Application.Abstractions;
using HealthPrep.Domain.Appointments;

namespace HealthPrep.Infrastructure.Services;

internal sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

internal sealed class RuleBasedInterviewQuestionProvider : IInterviewQuestionProvider
{
    public Task<string?> GetNextQuestion(Appointment appointment, CancellationToken ct)
    {
        var count = appointment.Answers.Count;
        string? next = count switch
        {
            0 => "What is the main reason for your consultation?",
            1 => "When did the symptoms begin and how have they changed?",
            2 => "How often do they occur and how severe are they from 0 to 10?",
            3 => "How do they affect your daily activities, sleep, or work?",
            4 => "What medicines and supplements do you take, including doses?",
            5 => "Do you have allergies or chronic conditions?",
            6 => "What questions would you like to ask the doctor?",
            _ => null
        };
        return Task.FromResult(next);
    }
}

internal sealed class SimplePdfRenderer : IReportPdfRenderer
{
    public byte[] Render(AppointmentSummary report)
    {
        var lines = new List<string>
        {
            "VISIT PREPARATION REPORT", $"Appointment: {report.ScheduledAt:yyyy-MM-dd HH:mm} UTC",
            $"Reason: {report.ConsultationReason}", "", "SYMPTOMS"
        };
        lines.AddRange(report.Symptoms.Select(x => $"- {x.Name}; since: {x.StartedOn?.ToString() ?? "unknown"}; severity: {x.Severity?.ToString() ?? "unknown"}/10"));
        lines.Add(""); lines.Add("MEDICATIONS / ALLERGIES");
        lines.AddRange(report.Medications.Select(x => $"- {(x.IsAllergy ? "ALLERGY" : "MED")}: {x.Name}, {x.Dose ?? "dose unknown"}"));
        lines.Add(""); lines.Add("QUESTIONS"); lines.AddRange(report.Questions.Select(x => $"- {x.Text}"));
        lines.Add(""); lines.Add("ITEMS TO CONFIRM"); lines.AddRange(report.Clarifications.Select(x => $"- {x.Message}"));
        lines.Add(""); lines.Add("OBSERVATIONS (not medical advice)"); lines.AddRange(report.Trends.Select(x => $"- {x.SymptomName}: {x.Description}"));
        return BuildPdf(lines.Take(42));
    }

    private static byte[] BuildPdf(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT /F1 10 Tf 50 790 Td 13 TL ");
        foreach (var line in lines) content.Append('(').Append(Escape(ToAscii(line))).Append(") Tj T* ");
        content.Append("ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream"
        };
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString()); pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string ToAscii(string value) => new(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c <= 127).ToArray());
}
