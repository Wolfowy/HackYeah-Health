using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocPrep.Application.Abstractions;
using DocPrep.Application.Contracts;
using DocPrep.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

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
    public void Pdf_contains_version_and_readable_report_content_including_Polish_characters()
    {
        using var provider = Provider(); var renderer = provider.GetRequiredService<IReportRenderer>(); var id = Guid.NewGuid();
        var snapshot = new ReportSnapshot(id, 1, 1, Guid.NewGuid(), "visit-1", Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "Ból głowy i przewlekłe zmęczenie",
            [new("Ból głowy", new DateOnly(2026, 10, 1), "codziennie", 7, "Utrudnia pracę", "Narasta wieczorem", [], "patient"),
             new("Zmęczenie", null, "rano", 4, "Utrudnia wstawanie", "Od tygodnia", [], "patient")],
            [new("Paracetamol", "500 mg", "doraźnie", "patient", "ból głowy"),
             new("Magnez", "200 mg", "raz dziennie", "patient", "suplementacja")],
            [new("Penicylina", "wysypka", "patient")],
            [new("Nadciśnienie", "Od dwóch lat", "patient")],
            ["Jak ograniczyć ból?"],
            [new("symptoms[0]", "missing", "Brak dokładnej godziny początku")],
            [new(Guid.NewGuid(), "Ból głowy", "patient", "Sen łagodzi objawy", true, "patient")],
            [new("Czy występują zawroty głowy?", "Nie występują", "text", "patient")],
            false, "Zażółć gęślą jaźń");
        var pdf = renderer.Render(snapshot);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(1, document.NumberOfPages);
        var text = ContentOrderTextExtractor.GetText(document.GetPage(1));
        foreach (var expected in new[] { id.ToString(), "Powód konsultacji", "Ból głowy", "2026-10-01", "Zmęczenie",
                     "Paracetamol", "500 mg", "Magnez", "200 mg", "Penicylina", "wysypka", "Nadciśnienie",
                     "Jak ograniczyć ból?", "Brak dokładnej godziny początku", "Sen łagodzi objawy",
                     "zmiana pacjenta", "Czy występują zawroty głowy?", "Nie występują", "Zażółć gęślą jaźń" })
            Assert.Contains(expected, text);
    }
}
