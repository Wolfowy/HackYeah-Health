using HealthPrep.Domain.Appointments;
using HealthPrep.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HealthPrep.Infrastructure.Persistence;

public sealed class HealthPrepDbContext(DbContextOptions<HealthPrepDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Facility> Facilities => Set<Facility>();

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
