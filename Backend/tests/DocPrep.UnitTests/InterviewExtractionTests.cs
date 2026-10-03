using System.Text.Json;
using DocPrep.Application.Interviews;
using DocPrep.Domain.Interviews;

namespace DocPrep.UnitTests;

public sealed class InterviewExtractionTests
{
    private readonly InterviewExtractionService service = new(null!, null!);

    [Fact]
    public void Normalizes_interview_json_with_multiple_clinical_items_and_explicit_states()
    {
        using var document = JsonDocument.Parse("""
        {
          "interview_json": { "value": "{\"schemaVersion\":1,\"consultationReason\":\"Ból głowy\",\"symptoms\":[{\"name\":\"Ból głowy\",\"startedOnText\":\"od trzech dni\",\"startedOnState\":\"provided\",\"severity\":6,\"timeline\":[]}],\"medications\":[{\"name\":\"Ibuprofen\",\"doseState\":\"unknown\",\"reason\":\"ból\"},{\"name\":\"Witamina D\",\"dose\":\"2000 IU\",\"doseState\":\"provided\"}],\"allergies\":[],\"chronicConditions\":[],\"patientQuestions\":[],\"medicationsState\":\"provided\",\"allergiesState\":\"provided\",\"chronicConditionsState\":\"provided\"}" }
        }
        """);

        var result = service.Normalize(document.RootElement);

        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Equal(2, result.Data!.Medications.Count);
        Assert.Equal(FieldState.Unknown, result.Data.Medications[0].DoseState);
        Assert.Equal("ból", result.Data.Medications[0].Reason);
        Assert.Empty(result.Data.Allergies);
        Assert.Equal(FieldState.Provided, result.Data.AllergiesState);
        Assert.Contains(result.Issues, x => x.FieldPath == "medications.0.dose" && x.Kind == "unknown");
    }

    [Fact]
    public void Invalid_json_is_failed_instead_of_being_exposed_as_structured_data()
    {
        using var document = JsonDocument.Parse("""{ "interview_json": { "value": "not-json" } }""");
        var result = service.Normalize(document.RootElement);
        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.Null(result.Data);
        Assert.Contains(result.Issues, x => x.Kind == "invalid_json");
    }

    [Fact]
    public void Invalid_severity_becomes_an_explicit_partial_result()
    {
        using var document = JsonDocument.Parse("""
        { "consultationReason":"ból", "symptoms":[{"name":"ból", "severity":15}],
          "medications":[], "allergies":[], "chronicConditions":[], "patientQuestions":[] }
        """);
        var result = service.Normalize(document.RootElement);
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Null(result.Data!.Symptoms.Single().Severity);
        Assert.Contains(result.Issues, x => x.FieldPath.EndsWith("severity"));
    }
}
