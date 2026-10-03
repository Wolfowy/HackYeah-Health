using HealthPrep.Application.Abstractions;
using HealthPrep.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HealthPrep.Infrastructure.Persistence;

internal sealed class AppointmentRepository(HealthPrepDbContext db) : IAppointmentRepository
{
    public Task Add(Appointment appointment, CancellationToken ct) => db.Appointments.AddAsync(appointment, ct).AsTask();
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public Task<Appointment?> Get(Guid id, CancellationToken ct) => Query().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Appointment?> GetByLinkToken(string token, CancellationToken ct) => Query().SingleOrDefaultAsync(x => x.InterviewLinkToken == token, ct);
    public async Task<IReadOnlyList<Appointment>> GetForPatient(string externalPatientId, CancellationToken ct) =>
        await Query().Where(x => x.ExternalPatientId == externalPatientId).OrderByDescending(x => x.ScheduledAt).ToListAsync(ct);
    public async Task<IReadOnlyList<Appointment>> GetForTenant(Guid tenantId, CancellationToken ct) =>
        await Query().Where(x => x.TenantId == tenantId).OrderBy(x => x.ScheduledAt).ToListAsync(ct);

    private IQueryable<Appointment> Query() => db.Appointments
        .Include(x => x.Answers).Include(x => x.Symptoms).Include(x => x.Medications).Include(x => x.Questions)
        .Include(x => x.Clarifications).Include(x => x.SummaryVersions).Include(x => x.Trends).AsSplitQuery();
}
