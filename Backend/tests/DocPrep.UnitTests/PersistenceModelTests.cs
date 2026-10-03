using Microsoft.EntityFrameworkCore;
using DocPrep.Domain.Access;
using DocPrep.Domain.Interviews;
using DocPrep.Domain.Reports;
using DocPrep.Domain.Tenancy;
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
        Assert.Contains(db.Model.FindEntityType(typeof(StaffUser))!.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(StaffUser.Email));
        Assert.Contains(db.Model.FindEntityType(typeof(StaffRefreshToken))!.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(StaffRefreshToken.TokenHash));
        Assert.Contains(db.Model.FindEntityType(typeof(AgentInterviewSession))!.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(AgentInterviewSession.ProviderConversationId));
        Assert.Contains(db.Model.FindEntityType(typeof(InterviewInvitation))!.GetIndexes(), x => x.IsUnique && x.Properties.Single().Name == nameof(InterviewInvitation.TokenHash));
    }
}
