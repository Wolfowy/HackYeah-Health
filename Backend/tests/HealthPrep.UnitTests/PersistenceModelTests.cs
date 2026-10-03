using HealthPrep.Domain.Appointments;
using HealthPrep.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthPrep.UnitTests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Ef_model_contains_aggregate_collections_and_json_snapshots()
    {
        var options = new DbContextOptionsBuilder<HealthPrepDbContext>()
            .UseNpgsql("Host=localhost;Database=model_validation;Username=test;Password=test")
            .Options;
        using var db = new HealthPrepDbContext(options);
        var appointment = db.Model.FindEntityType(typeof(Appointment));
        Assert.NotNull(appointment);
        Assert.Equal(7, appointment!.GetNavigations().Count());
        Assert.Equal("jsonb", db.Model.FindEntityType(typeof(SummaryVersion))!.FindProperty(nameof(SummaryVersion.SnapshotJson))!.GetColumnType());
    }
}
