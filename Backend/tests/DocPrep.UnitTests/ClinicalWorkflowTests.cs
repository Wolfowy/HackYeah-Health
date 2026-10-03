using DocPrep.Domain.Common;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Supplementation;

namespace DocPrep.UnitTests;

public sealed class ClinicalWorkflowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T10:00:00Z");

    [Fact]
    public void Draft_distinguishes_missing_and_provided_fields()
    {
        var draft = new InterviewDraft(Guid.NewGuid(), Now);
        draft.Replace("Ból głowy", [new("Ból głowy", null, FieldState.Unknown, "codziennie", 6, "utrudnia pracę", null, [])],
            [new("Lek A", null, FieldState.Unknown, "rano")], [], [], [], Now);
        Assert.Equal(2, draft.Clarifications.Count);
        Assert.All(draft.Clarifications, x => Assert.Equal(ClarificationKind.Missing, x.Kind));
    }

    [Fact]
    public void Observation_requires_explicit_final_decision()
    {
        var observation = new ObservationProposal(Guid.NewGuid(), "Ból głowy", ObservationKind.Recurring, "Zgłoszony ponownie.", Now);
        Assert.Throws<DomainException>(() => observation.Decide(ObservationDecision.Pending, null, Now));
        observation.Decide(ObservationDecision.EditedAndAccepted, "Ból występuje rzadziej.", Now);
        Assert.Equal("Ból występuje rzadziej.", observation.ReportText);
    }

    [Fact]
    public void New_questions_are_added_to_one_open_round()
    {
        var round = new SupplementationRound(Guid.NewGuid(), 1, "doctor-1", Now);
        round.AddQuestion("Czy ból nasila się przy wysiłku?", "doctor-1", Now);
        round.AddQuestion("Czy występują nudności?", "doctor-1", Now);
        Assert.Equal(2, round.Questions.Count);
    }

    [Fact]
    public void Round_cannot_be_ready_until_every_question_is_answered()
    {
        var round = new SupplementationRound(Guid.NewGuid(), 1, "doctor-1", Now);
        round.AddQuestion("Pytanie", "doctor-1", Now);
        Assert.Throws<DomainException>(round.MarkAnswersReady);
        round.Questions.Single().AnswerQuestion("Odpowiedź", AnswerMode.Text, Now);
        round.MarkAnswersReady();
        Assert.Equal(SupplementationRoundStatus.AwaitingApproval, round.Status);
    }
}
