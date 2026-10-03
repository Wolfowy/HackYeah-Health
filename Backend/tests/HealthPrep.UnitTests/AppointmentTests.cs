using HealthPrep.Domain.Appointments;

namespace HealthPrep.UnitTests;

public sealed class AppointmentTests
{
    [Fact]
    public void Interview_cannot_be_edited_after_configured_deadline()
    {
        var now = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
        var appointment = new Appointment(Guid.NewGuid(), "visit-1", "patient-1", now.AddHours(12), "Headache", now);
        var error = Assert.Throws<DomainException>(() => appointment.AddAnswer("Question", "Answer", AnswerMode.Text, now));
        Assert.Contains("deadline", error.Message);
    }

    [Fact]
    public void Approval_is_versioned_and_consent_controls_shared_status()
    {
        var now = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
        var appointment = new Appointment(Guid.NewGuid(), "visit-1", "patient-1", now.AddDays(7), "Headache", now);
        appointment.Trends.Add(new(appointment.Id, "Headache", "recurring", "Reported again.", "[]", false));
        appointment.Approve("{}", now);
        appointment.SetSharingConsent(true, now);
        Assert.Equal(InterviewStatus.Shared, appointment.Status);
        Assert.Equal(1, appointment.SummaryVersions.Single().Version);
        Assert.True(appointment.Trends.Single().PatientApproved);
    }

    [Fact]
    public void Clinical_data_marks_summary_as_awaiting_approval()
    {
        var now = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
        var appointment = new Appointment(Guid.NewGuid(), "visit-1", "patient-1", now.AddDays(7), "Headache", now);
        appointment.ReplaceClinicalData([new("Headache", DateOnly.FromDateTime(now.Date), "daily", 6, "work", null)], [], ["What can trigger it?"], now);
        Assert.Equal(InterviewStatus.AwaitingApproval, appointment.Status);
        Assert.Single(appointment.Symptoms);
    }
}
