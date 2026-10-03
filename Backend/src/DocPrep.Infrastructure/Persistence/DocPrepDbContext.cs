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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("docprep");
        b.Entity<Facility>(e => { e.ToTable("facilities"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); e.HasData(new Facility(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Przychodnia Demo")); });
        b.Entity<PatientIdentity>(e => { e.ToTable("patients"); e.HasKey(x => x.Id); e.HasIndex(x => x.CorrelationKey).IsUnique(); e.Property(x => x.CorrelationKey).HasMaxLength(128); });
        b.Entity<VisitProcess>(e =>
        {
            e.ToTable("visits"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.FacilityId, x.ExternalVisitId }).IsUnique();
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientIdentity>().WithMany().HasForeignKey(x => x.PatientIdentityId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.ContactChannel).HasConversion<string>(); e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        });
        b.Entity<PatientAccessGrant>(e =>
        {
            e.ToTable("patient_access_grants"); e.HasKey(x => x.Id); e.HasIndex(x => x.LinkTokenHash).IsUnique(); e.HasIndex(x => x.VisitCodeHash).IsUnique();
            e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DeliveryAttempt>(e => { e.ToTable("delivery_attempts"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); e.HasOne<VisitProcess>().WithMany().HasForeignKey(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<InterviewDraft>(e => { e.ToTable("interview_drafts"); e.HasKey(x => x.Id); e.HasIndex(x => x.VisitProcessId).IsUnique(); e.HasOne<VisitProcess>().WithOne().HasForeignKey<InterviewDraft>(x => x.VisitProcessId).OnDelete(DeleteBehavior.Cascade); });
        Child<InterviewAnswer>(b, "interview_answers", "InterviewDraftId", nameof(InterviewDraft.Answers));
        Child<Symptom>(b, "symptoms", "InterviewDraftId", nameof(InterviewDraft.Symptoms));
        Child<Medication>(b, "medications", "InterviewDraftId", nameof(InterviewDraft.Medications));
        Child<Allergy>(b, "allergies", "InterviewDraftId", nameof(InterviewDraft.Allergies));
        Child<ChronicCondition>(b, "chronic_conditions", "InterviewDraftId", nameof(InterviewDraft.ChronicConditions));
        Child<PatientQuestion>(b, "patient_questions", "InterviewDraftId", nameof(InterviewDraft.PatientQuestions));
        Child<Clarification>(b, "clarifications", "InterviewDraftId", nameof(InterviewDraft.Clarifications));
        b.Entity<SymptomTimelineEntry>(e => { e.ToTable("symptom_timeline"); e.HasKey(x => x.Id); e.HasOne<Symptom>().WithMany(x => x.Timeline).HasForeignKey(x => x.SymptomId).OnDelete(DeleteBehavior.Cascade); });
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
    { var e = b.Entity<T>(); e.ToTable(table); e.HasKey("Id"); e.HasOne<InterviewDraft>().WithMany(navigation).HasForeignKey(foreignKey).OnDelete(DeleteBehavior.Cascade); }
}
