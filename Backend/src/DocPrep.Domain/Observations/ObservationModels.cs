using DocPrep.Domain.Common;

namespace DocPrep.Domain.Observations;

public enum ObservationKind { New, Recurring, Changed, Pattern, InsufficientData }
public enum ObservationDecision { Pending, Accepted, Rejected, EditedAndAccepted }

public sealed class ObservationProposal
{
    private ObservationProposal() { }
    public ObservationProposal(Guid draftId, string symptomName, ObservationKind kind, string text, DateTimeOffset now)
    { Id = Guid.NewGuid(); InterviewDraftId = draftId; SymptomName = symptomName; Kind = kind; OriginalText = text; Decision = ObservationDecision.Pending; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid InterviewDraftId { get; private set; }
    public string SymptomName { get; private set; } = "";
    public ObservationKind Kind { get; private set; }
    public string OriginalText { get; private set; } = ""; public string? PatientEditedText { get; private set; }
    public ObservationDecision Decision { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public List<ObservationEvidence> Evidence { get; } = [];
    public void Decide(ObservationDecision decision, string? editedText, DateTimeOffset now)
    {
        if (decision == ObservationDecision.Pending) throw new DomainException("observation.invalid_decision", "A final observation decision is required.");
        if (decision == ObservationDecision.EditedAndAccepted) PatientEditedText = Guard.Required(editedText, nameof(editedText), 1000);
        else PatientEditedText = null;
        Decision = decision; DecidedAt = now;
    }
    public string ReportText => PatientEditedText ?? OriginalText;
}

public sealed class ObservationEvidence(Guid observationProposalId, Guid sourceVisitId, Guid sourceReportVersionId, DateTimeOffset sourceDate, string sourceFragment)
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid ObservationProposalId { get; private set; } = observationProposalId;
    public Guid SourceVisitId { get; private set; } = sourceVisitId; public Guid SourceReportVersionId { get; private set; } = sourceReportVersionId;
    public DateTimeOffset SourceDate { get; private set; } = sourceDate; public string SourceFragment { get; private set; } = sourceFragment;
}
