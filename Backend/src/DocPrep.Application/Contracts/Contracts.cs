using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Visits;

namespace DocPrep.Application.Contracts;

public sealed record CreateVisitCommand(Guid FacilityId, string ExternalVisitId, string Pesel, DateTimeOffset ScheduledAt,
    DateTimeOffset ServiceExpiresAt, string Contact, ContactChannel Channel, string? AssignedClinicianId);
public sealed record InvitationResult(Guid VisitId, string LinkToken, string VisitCode, string DeliveryStatus,
    Guid InterviewId, string InterviewInvitationToken);
public sealed record ExchangeAccessResult(string SessionToken, Guid VisitId, DateTimeOffset ExpiresAt);
public sealed record AdminVisitView(Guid VisitId, string ExternalVisitId, DateTimeOffset ScheduledAt, VisitStatus Status,
    string DeliveryStatus, bool HasOpenSupplementationRound);
public sealed record PatientInterviewView(Guid VisitId, DateTimeOffset ScheduledAt, DateTimeOffset ServiceExpiresAt, VisitStatus Status,
    DraftView Draft, IReadOnlyList<PatientObservationView> Observations, SupplementationRoundView? SupplementationRound,
    int? LatestVersion, bool ConsentActive);
public sealed record DraftView(string? ConsultationReason, int Revision, IReadOnlyList<SymptomData> Symptoms,
    IReadOnlyList<MedicationData> Medications, IReadOnlyList<AllergyData> Allergies, IReadOnlyList<ConditionData> ChronicConditions,
    IReadOnlyList<string> Questions, IReadOnlyList<ClarificationView> Clarifications);
public sealed record ClarificationView(Guid Id, string FieldPath, string Kind, string Message);
public sealed record PatientObservationView(Guid Id, string SymptomName, ObservationKind Kind, string Text, ObservationDecision Decision, bool EditedByPatient);
public sealed record SupplementationRoundView(Guid Id, int Number, string Status, IReadOnlyList<SupplementationQuestionView> Questions);
public sealed record SupplementationQuestionView(Guid Id, string Text, string? Answer, AnswerMode? Mode);
public sealed record ReplaceDraftCommand(string ConsultationReason, IReadOnlyList<SymptomData> Symptoms, IReadOnlyList<MedicationData> Medications,
    IReadOnlyList<AllergyData> Allergies, IReadOnlyList<ConditionData> ChronicConditions, IReadOnlyList<string> Questions);
public sealed record SubmitAnswerCommand(string Question, string Answer, AnswerMode Mode);
public sealed record ObservationDecisionCommand(ObservationDecision Decision, string? EditedText);
public sealed record ApproveReportCommand(bool ConfirmIncompleteReport);
public sealed record ConsentCommand(bool Granted);
public sealed record AddClinicianQuestionsCommand(string ClinicianId, IReadOnlyList<string> Questions);
public sealed record SupplementationAnswerCommand(Guid QuestionId, string Answer, AnswerMode Mode);
public sealed record ReportVersionView(Guid VersionId, int VersionNumber, DateTimeOffset ApprovedAt, bool ConfirmedIncomplete);
public sealed record ReportSnapshot(Guid VersionId, int VersionNumber, int SchemaVersion, Guid VisitId, string ExternalVisitId,
    Guid FacilityId, DateTimeOffset ScheduledAt, DateTimeOffset ApprovedAt, string ConsultationReason,
    IReadOnlyList<ReportSymptom> Symptoms, IReadOnlyList<ReportMedication> Medications, IReadOnlyList<ReportAllergy> Allergies,
    IReadOnlyList<ReportCondition> ChronicConditions, IReadOnlyList<string> PatientQuestions,
    IReadOnlyList<ReportClarification> Clarifications, IReadOnlyList<ReportObservation> Observations,
    IReadOnlyList<ReportSupplementation> SupplementationAnswers, bool ConfirmedIncomplete);
public sealed record ReportSymptom(string Name, DateOnly? StartedOn, string? Frequency, int? Severity, string? DailyImpact, string? Description, IReadOnlyList<TimelineData> Timeline, string Source);
public sealed record ReportMedication(string Name, string? Dose, string? Schedule, string Source);
public sealed record ReportAllergy(string Substance, string? Reaction, string Source);
public sealed record ReportCondition(string Name, string? Description, string Source);
public sealed record ReportClarification(string FieldPath, string Kind, string Message);
public sealed record ReportObservation(Guid ObservationId, string SymptomName, string Kind, string Text, bool EditedByPatient, string Source);
public sealed record ReportSupplementation(string Question, string Answer, string Mode, string Source);
public sealed record ClinicianReportView(ReportSnapshot Report, IReadOnlyList<ObservationEvidenceView> AvailableEvidence);
public sealed record ObservationEvidenceView(Guid ObservationId, Guid SourceVisitId, Guid SourceVersionId, DateTimeOffset SourceDate, string SourceFragment);
public sealed record DeletionRequestView(Guid RequestId, string Status, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, string? LastError);
public sealed record AgentInterviewView(Guid Id, string DisplayName, DateTimeOffset VisitDate, string Status, string InterviewType, int SessionCount);
public sealed record AgentInterviewResultView(Guid Id, string Status, string? FinalReport, string? StructuredDataJson);
public sealed record AgentSessionResult(Guid SessionId, string Mode, string Provider, string? ConversationToken, string? SignedUrl, string? ConversationId);
public sealed record AgentInterviewAccess(Guid? VisitId, Guid? InterviewId, Guid? InvitationId, Guid? UserId)
{
    public static AgentInterviewAccess ForVisit(Guid visitId) => new(visitId, null, null, null);
    public static AgentInterviewAccess ForInvitation(Guid interviewId, Guid invitationId) => new(null, interviewId, invitationId, null);
}
public sealed record InterviewInvitationAccess(Guid InterviewId, Guid InvitationId, DateTimeOffset ExpiresAt);
