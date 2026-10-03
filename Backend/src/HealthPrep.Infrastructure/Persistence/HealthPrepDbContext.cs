using HealthPrep.Domain.Appointments;
using HealthPrep.Domain.Tenancy;
using HealthPrep.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace HealthPrep.Infrastructure.Persistence;

public sealed class HealthPrepDbContext(DbContextOptions<HealthPrepDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<AgentInterview> AgentInterviews => Set<AgentInterview>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<InterviewInvitation> InterviewInvitations => Set<InterviewInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("healthprep");
        var appointment = modelBuilder.Entity<Appointment>();
        appointment.HasKey(x => x.Id);
        appointment.HasIndex(x => new { x.TenantId, x.ExternalAppointmentId }).IsUnique();
        appointment.HasIndex(x => x.InterviewLinkToken).IsUnique();
        appointment.HasIndex(x => new { x.ExternalPatientId, x.ScheduledAt });
        appointment.Property(x => x.ExternalAppointmentId).HasMaxLength(100);
        appointment.Property(x => x.ExternalPatientId).HasMaxLength(100);
        appointment.Property(x => x.ConsultationReason).HasMaxLength(1000);
        appointment.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);

        ConfigureChild<InterviewAnswer>(modelBuilder, "interview_answers");
        ConfigureChild<Symptom>(modelBuilder, "symptoms");
        ConfigureChild<Medication>(modelBuilder, "medications");
        ConfigureChild<PatientQuestion>(modelBuilder, "patient_questions");
        ConfigureChild<Clarification>(modelBuilder, "clarifications");
        ConfigureChild<SummaryVersion>(modelBuilder, "summary_versions");
        ConfigureChild<TrendObservation>(modelBuilder, "trend_observations");
        modelBuilder.Entity<InterviewAnswer>().Property(x => x.Mode).HasConversion<string>();
        modelBuilder.Entity<PatientQuestion>().Property(x => x.Source).HasConversion<string>();
        modelBuilder.Entity<SummaryVersion>().Property(x => x.SnapshotJson).HasColumnType("jsonb");
        modelBuilder.Entity<TrendObservation>().Property(x => x.EvidenceJson).HasColumnType("jsonb");
        modelBuilder.Entity<Facility>().HasKey(x => x.Id);
        modelBuilder.Entity<Facility>().HasIndex(x => x.ApiKeyHash).IsUnique();

        var interview = modelBuilder.Entity<AgentInterview>();
        interview.ToTable("agent_interviews");
        interview.HasKey(x => x.Id);
        interview.HasIndex(x => x.AppointmentId).IsUnique();
        interview.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        interview.Property(x => x.StructuredDataJson).HasColumnType("jsonb");

        var invitation = modelBuilder.Entity<InterviewInvitation>();
        invitation.ToTable("interview_invitations");
        invitation.HasKey(x => x.Id);
        invitation.HasIndex(x => x.TokenHash).IsUnique();
        invitation.Property(x => x.TokenHash).HasMaxLength(64);
        invitation.HasOne<AgentInterview>().WithMany().HasForeignKey(x => x.InterviewId).OnDelete(DeleteBehavior.Cascade);

        var session = modelBuilder.Entity<AgentSession>();
        session.ToTable("agent_sessions");
        session.HasKey(x => x.Id);
        session.HasIndex(x => x.ProviderConversationId).IsUnique();
        session.HasOne<AgentInterview>().WithMany().HasForeignKey(x => x.InterviewId).OnDelete(DeleteBehavior.Cascade);
        session.HasOne<InterviewInvitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.SetNull);
        session.Ignore(x => x.TechnicalUserId);
        session.Property(x => x.TranscriptJson).HasColumnType("jsonb");
        session.Property(x => x.AnalysisJson).HasColumnType("jsonb");
        session.Property(x => x.MetadataJson).HasColumnType("jsonb");
        session.Property(x => x.StructuredDataJson).HasColumnType("jsonb");
    }

    private static void ConfigureChild<T>(ModelBuilder modelBuilder, string table) where T : class
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(table);
        entity.HasKey("Id");
        entity.HasOne<Appointment>().WithMany(typeof(T).Name switch
        {
            nameof(InterviewAnswer) => nameof(Appointment.Answers), nameof(Symptom) => nameof(Appointment.Symptoms),
            nameof(Medication) => nameof(Appointment.Medications), nameof(PatientQuestion) => nameof(Appointment.Questions),
            nameof(Clarification) => nameof(Appointment.Clarifications), nameof(SummaryVersion) => nameof(Appointment.SummaryVersions),
            _ => nameof(Appointment.Trends)
        }).HasForeignKey("AppointmentId").OnDelete(DeleteBehavior.Cascade);
    }
}
