using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Contracts;
using DocPrep.Infrastructure;

namespace DocPrep.UnitTests;

public sealed class InfrastructureServiceTests
{
    private static ServiceProvider Provider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Security:PatientHmacKey"] = "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODk=",
            ["Security:EncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
        }).Build();
        return new ServiceCollection().AddSingleton<IConfiguration>(config).AddLogging().AddDocPrepInfrastructure(config).BuildServiceProvider();
    }

    [Fact]
    public void Patient_data_is_correlated_deterministically_and_encrypted_reversibly()
    {
        using var provider = Provider(); var protector = provider.GetRequiredService<IPatientDataProtector>();
        Assert.Equal(protector.CorrelationKey("44051401458"), protector.CorrelationKey("44051401458"));
        var encrypted = protector.Protect("44051401458"); Assert.DoesNotContain("44051401458", encrypted); Assert.Equal("44051401458", protector.Unprotect(encrypted));
    }

    [Fact]
    public void Pdf_contains_the_same_version_identifier_as_snapshot()
    {
        using var provider = Provider(); var renderer = provider.GetRequiredService<IReportRenderer>(); var id = Guid.NewGuid();
        var snapshot = new ReportSnapshot(id, 1, 1, Guid.NewGuid(), "visit-1", Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "Ból głowy", [], [], [], [], [], [], [], [], false);
        var pdf = renderer.Render(snapshot);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4)); Assert.True(pdf.Length > 500);
    }
}
