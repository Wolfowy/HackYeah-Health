using DocPrep.Domain.Common;
using DocPrep.Domain.Interviews;

namespace DocPrep.Domain.Supplementation;

public enum SupplementationRoundStatus { Open, AwaitingApproval, Closed, Cancelled }
public sealed class SupplementationRound
{
    private SupplementationRound() { }
    public SupplementationRound(Guid visitId, int number, string clinicianId, DateTimeOffset now)
    { Id = Guid.NewGuid(); VisitProcessId = visitId; Number = number; OpenedByClinicianId = clinicianId; Status = SupplementationRoundStatus.Open; OpenedAt = now; }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public int Number { get; private set; }
    public string OpenedByClinicianId { get; private set; } = ""; public SupplementationRoundStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public List<SupplementationQuestion> Questions { get; } = [];
    public void AddQuestion(string text, string clinicianId, DateTimeOffset now)
    { if (Status != SupplementationRoundStatus.Open) throw new DomainException("supplementation.round_closed", "The supplementation round is not open."); Questions.Add(new(Id, text, clinicianId, now)); }
    public void MarkAnswersReady() { if (Questions.Count == 0 || Questions.Any(x => x.Answer is null)) throw new DomainException("supplementation.answers_missing", "All questions require an answer."); Status = SupplementationRoundStatus.AwaitingApproval; }
    public void Close(DateTimeOffset now) { Status = SupplementationRoundStatus.Closed; ClosedAt = now; }
}
public sealed class SupplementationQuestion
{
    private SupplementationQuestion() { }
    internal SupplementationQuestion(Guid roundId, string text, string clinicianId, DateTimeOffset now)
    { Id = Guid.NewGuid(); SupplementationRoundId = roundId; Text = Guard.Required(text, nameof(text), 1000); AskedByClinicianId = clinicianId; AskedAt = now; }
    public Guid Id { get; private set; }
    public Guid SupplementationRoundId { get; private set; }
    public string Text { get; private set; } = "";
    public string AskedByClinicianId { get; private set; } = ""; public DateTimeOffset AskedAt { get; private set; }
    public SupplementationAnswer? Answer { get; private set; }
    public void AnswerQuestion(string text, AnswerMode mode, DateTimeOffset now) => Answer = new(Id, text, mode, now);
}
public sealed class SupplementationAnswer(Guid supplementationQuestionId, string text, AnswerMode mode, DateTimeOffset answeredAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid SupplementationQuestionId { get; private set; } = supplementationQuestionId;
    public string Text { get; private set; } = Guard.Required(text, nameof(text), 8000); public AnswerMode Mode { get; private set; } = mode; public DateTimeOffset AnsweredAt { get; private set; } = answeredAt;
}
