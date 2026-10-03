using Microsoft.EntityFrameworkCore;
using DocPrep.Domain.Access;
using DocPrep.Domain.Reports;
using DocPrep.Infrastructure.Persistence;

namespace DocPrep.UnitTests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Model_has_unique_access_credentials_and_immutable_json_snapshot()
    {
        var options = new DbContextOptionsBuilder<DocPrepDbContext>().UseNpgsql("Host=localhost;Database=model;Username=test;Password=test").Options;
        using var db = new DocPrepDbContext(options);
        var grant = db.Model.FindEntityType(typeof(PatientAccessGrant))!;
        Assert.Contains(grant.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(PatientAccessGrant.LinkTokenHash));
        Assert.Equal("jsonb", db.Model.FindEntityType(typeof(ReportVersion))!.FindProperty(nameof(ReportVersion.SnapshotJson))!.GetColumnType());
    }
}
