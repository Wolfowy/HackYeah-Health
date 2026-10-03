using Microsoft.EntityFrameworkCore;
using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Observations;
using DocPrep.Domain.Privacy;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Sharing;
using DocPrep.Domain.Supplementation;
using DocPrep.Domain.Tenancy;
using DocPrep.Domain.Visits;

namespace DocPrep.Infrastructure.Persistence;

public sealed class DocPrepDbContext(DbContextOptions<DocPrepDbContext> options) : DbContext(options)
{
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<PatientIdentity> Patients => Set<PatientIdentity>();
    public DbSet<VisitProcess> Visits => Set<VisitProcess>();
    public DbSet<PatientAccessGrant> AccessGrants => Set<PatientAccessGrant>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<InterviewDraft> Drafts => Set<InterviewDraft>();
    public DbSet<ObservationProposal> Observations => Set<ObservationProposal>();
    public DbSet<ReportVersion> ReportVersions => Set<ReportVersion>();
    public DbSet<ReportEvidence> ReportEvidence => Set<ReportEvidence>();
    public DbSet<SharingConsent> Consents => Set<SharingConsent>();
    public DbSet<SupplementationRound> SupplementationRounds => Set<SupplementationRound>();
    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<StaffRefreshToken> StaffRefreshTokens => Set<StaffRefreshToken>();
    public DbSet<PatientAccount> PatientAccounts => Set<PatientAccount>();
    public DbSet<PatientRefreshToken> PatientRefreshTokens => Set<PatientRefreshToken>();
    public DbSet<AgentInterview> AgentInterviews => Set<AgentInterview>();
    public DbSet<AgentInterviewSession> AgentInterviewSessions => Set<AgentInterviewSession>();
    public DbSet<InterviewInvitation> InterviewInvitations => Set<InterviewInvitation>();
    public DbSet<ExternalWebhookEvent> ExternalWebhookEvents => Set<ExternalWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("docprep");
        b.Entity<Facility>(e => { e.ToTable("facilities"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); e.HasData(new Facility(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Przychodnia Demo")); });
        b.Entity<StaffUser>(e =>
        {
            e.ToTable("staff_users"); e.HasKey(x => x.Id); e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320); e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Role).HasConversion<string>(); e.Property(x => x.ClinicianId).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(2000);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StaffRefreshToken>(e =>
        {
            e.ToTable("staff_refresh_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128); e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            e.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PatientAccount>(e =>
        {
            e.ToTable("patient_accounts"); e.HasKey(x => x.Id); e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.PatientIdentityId).IsUnique(); e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(200); e.Property(x => x.AvatarUrl).HasMaxLength(2000);
            e.Property(x => x.PasswordHash).HasMaxLength(2000);
            e.HasOne<PatientIdentity>().WithOne().HasForeignKey<PatientAccount>(x => x.PatientIdentityId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PatientRefreshToken>(e =>
        {
            e.ToTable("patient_refresh_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128); e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            e.HasOne<PatientAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AgentInterview>(e =>
        {
            e.ToTable("agent_interviews"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VisitProcessId, x.Generation }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.InterviewType).HasMaxLength(100);
            e.Property(x => x.StructuredDataJson).HasColumnType("jsonb");
            e.Property(x => x.ExtractionStatus).HasConversion<string>(); e.Property(x => x.ImportStatus).HasConversion<string>();
            e.Property(x => x.ExtractionIssuesJson).HasColumnType("jsonb");
            e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AgentInterviewSession>(e =>
        {
            e.ToTable("agent_interview_sessions"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProviderConversationId).IsUnique(); e.Property(x => x.ProviderConversationId).HasMaxLength(200);
            e.Property(x => x.Provider).HasMaxLength(50); e.Property(x => x.Mode).HasConversion<string>(); e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.TranscriptJson).HasColumnType("jsonb"); e.Property(x => x.AnalysisJson).HasColumnType("jsonb"); e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.HasOne<AgentInterview>().WithMany().HasForeignKey(x => x.InterviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<InterviewInvitation>(e =>
        {
            e.ToTable("interview_invitations"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128); e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            e.HasOne<AgentInterview>().WithMany().HasForeignKey(x => x.InterviewId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExternalWebhookEvent>(e =>
        {
            e.ToTable("external_webhook_events"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.Provider, x.ExternalEventId }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(50); e.Property(x => x.ExternalEventId).HasMaxLength(300); e.Property(x => x.PayloadHash).HasMaxLength(128);
            e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PatientIdentity>(e => { e.ToTable("patients"); e.HasKey(x => x.Id); e.HasIndex(x => x.CorrelationKey).IsUnique(); e.Property(x => x.CorrelationKey).HasMaxLength(128); });
        b.Entity<VisitProcess>(e =>
        {
            e.ToTable("visits"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.FacilityId, x.ExternalVisitId }).IsUnique();
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientIdentity>().WithMany().HasForeignKey(x => x.PatientIdentityId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.ContactChannel).HasConversion<string>(); e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            e.Property(x => x.TimeZone).HasMaxLength(100); e.Property(x => x.DoctorName).HasMaxLength(200);
            e.Property(x => x.DoctorSpecialty).HasMaxLength(200); e.Property(x => x.FacilityName).HasMaxLength(200);
            e.Property(x => x.FacilityAddress).HasMaxLength(500); e.Property(x => x.Room).HasMaxLength(100);
            e.Property(x => x.VisitType).HasMaxLength(100); e.Property(x => x.LocationInstructions).HasMaxLength(1000);
        });
        b.Entity<PatientAccessGrant>(e =>
        {
            e.ToTable("patient_access_grants"); e.HasKey(x => x.Id); e.HasIndex(x => x.LinkTokenHash).IsUnique(); e.HasIndex(x => x.VisitCodeHash).IsUnique();
            e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DeliveryAttempt>(e => { e.ToTable("delivery_attempts"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<InterviewDraft>(e => { e.ToTable("interview_drafts"); e.HasKey(x => x.Id); e.HasIndex(x => x.VisitProcessId).IsUnique(); e.Property(x => x.MedicationsState).HasConversion<string>(); e.Property(x => x.AllergiesState).HasConversion<string>(); e.Property(x => x.ChronicConditionsState).HasConversion<string>(); e.HasOne<VisitProcess>().WithOne().HasForeignKey<InterviewDraft>(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        Child<InterviewAnswer>(b, "interview_answers", "InterviewDraftId", nameof(InterviewDraft.Answers));
        Child<Symptom>(b, "symptoms", "InterviewDraftId", nameof(InterviewDraft.Symptoms));
        Child<Medication>(b, "medications", "InterviewDraftId", nameof(InterviewDraft.Medications));
        Child<Allergy>(b, "allergies", "InterviewDraftId", nameof(InterviewDraft.Allergies));
        Child<ChronicCondition>(b, "chronic_conditions", "InterviewDraftId", nameof(InterviewDraft.ChronicConditions));
        Child<PatientQuestion>(b, "patient_questions", "InterviewDraftId", nameof(InterviewDraft.PatientQuestions));
        Child<Clarification>(b, "clarifications", "InterviewDraftId", nameof(InterviewDraft.Clarifications));
        b.Entity<SymptomTimelineEntry>(e => { e.ToTable("symptom_timeline"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasOne<Symptom>().WithMany(x => x.Timeline).HasForeignKey(x => x.SymptomId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<InterviewAnswer>().Property(x => x.Mode).HasConversion<string>(); b.Entity<Symptom>().Property(x => x.StartedOnState).HasConversion<string>();
        b.Entity<Medication>().Property(x => x.DoseState).HasConversion<string>(); b.Entity<Clarification>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<ObservationProposal>(e =>
        {
            e.ToTable("observation_proposals"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasConversion<string>(); e.Property(x => x.Decision).HasConversion<string>();
            e.HasOne<InterviewDraft>().WithMany().HasForeignKey(x => x.InterviewDraftId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ObservationEvidence>(e => { e.ToTable("observation_evidence"); e.HasKey(x => x.Id); e.HasOne<ObservationProposal>().WithMany(x => x.Evidence).HasForeignKey(x => x.ObservationProposalId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<ReportVersion>(e => { e.ToTable("report_versions"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VisitProcessId, x.VersionNumber }).IsUnique(); e.Property(x => x.SnapshotJson).HasColumnType("jsonb"); e.Property(x => x.PdfStatus).HasConversion<string>(); e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<ReportEvidence>(e => { e.ToTable("report_evidence"); e.HasKey(x => x.Id); e.HasOne<ReportVersion>().WithMany().HasForeignKey(x => x.ReportVersionId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SharingConsent>(e => { e.ToTable("sharing_consents"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VisitProcessId, x.RevokedAt }); e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SupplementationRound>(e => { e.ToTable("supplementation_rounds"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SupplementationQuestion>(e => { e.ToTable("supplementation_questions"); e.HasKey(x => x.Id); e.HasOne<SupplementationRound>().WithMany(x => x.Questions).HasForeignKey(x => x.SupplementationRoundId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SupplementationAnswer>(e => { e.ToTable("supplementation_answers"); e.HasKey(x => x.Id); e.Property(x => x.Mode).HasConversion<string>(); e.HasOne<SupplementationQuestion>().WithOne(x => x.Answer).HasForeignKey<SupplementationAnswer>(x => x.SupplementationQuestionId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<DeletionRequest>(e => { e.ToTable("deletion_requests"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); });
        b.Entity<AuditEvent>(e => { e.ToTable("audit_events"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VisitProcessId, x.OccurredAt }); });
    }

    private static void Child<T>(ModelBuilder b, string table, string foreignKey, string navigation) where T : class
    { var e = b.Entity<T>(); e.ToTable(table); e.HasKey("Id"); e.Property<Guid>("Id").ValueGeneratedNever(); e.HasOne<InterviewDraft>().WithMany(navigation).HasForeignKey(foreignKey).OnDelete(DeleteBehavior.Cascade); }
}
