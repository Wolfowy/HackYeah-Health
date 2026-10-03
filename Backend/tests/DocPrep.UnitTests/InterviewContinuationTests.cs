using DocPrep.Application.Interviews;
using DocPrep.Domain.Interviews;

namespace DocPrep.UnitTests;

public sealed class InterviewContinuationTests
{
    [Fact]
    public void Context_uses_saved_summary_and_unanswered_clinician_questions()
    {
        var session = new AgentInterviewSession(Guid.NewGuid(), null, InterviewSessionMode.Voice, DateTimeOffset.UtcNow);
        session.Complete("[]", "{\"transcript_summary\":\"Pacjent zgłosił ból głowy.\"}", "{}", DateTimeOffset.UtcNow);
        var result = InterviewContinuationContext.Build([session], "Raport pierwotny", ["Od kiedy boli?"]);
        Assert.Contains("Pacjent zgłosił ból głowy.", result);
        Assert.Contains("Raport pierwotny", result);
        Assert.Contains("Od kiedy boli?", result);
    }

    [Fact]
    public void Context_falls_back_to_transcript_without_inventing_a_summary()
    {
        var session = new AgentInterviewSession(Guid.NewGuid(), null, InterviewSessionMode.Text, DateTimeOffset.UtcNow);
        session.Complete("[{\"role\":\"user\",\"message\":\"Boli od wtorku\"}]", "{}", "{}", DateTimeOffset.UtcNow);
        Assert.Contains("Pacjent: Boli od wtorku", InterviewContinuationContext.Build([session]));
        Assert.Equal("", InterviewContinuationContext.Build([]));
    }

    [Fact]
    public void Context_is_bounded_and_tolerates_invalid_legacy_json()
    {
        var session = new AgentInterviewSession(Guid.NewGuid(), null, InterviewSessionMode.Text, DateTimeOffset.UtcNow);
        session.Complete("broken", "broken", "{}", DateTimeOffset.UtcNow);
        Assert.Equal("", InterviewContinuationContext.Build([session]));
        var context = InterviewContinuationContext.Build([session], new string('x', 20_000), ["Najnowsze pytanie"]);
        Assert.InRange(context.Length, 1, InterviewContinuationContext.MaxLength);
        Assert.Contains("Najnowsze pytanie", context);
    }
}
