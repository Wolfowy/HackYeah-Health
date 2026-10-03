namespace DocPrep.Domain.Visits;

public sealed class ReceptionDetails
{
    private ReceptionDetails() { }
    public ReceptionDetails(Guid visitId, string encryptedPatient) { VisitProcessId = visitId; EncryptedPatient = encryptedPatient; }
    public Guid VisitProcessId { get; private set; }
    public string EncryptedPatient { get; private set; } = "";
    public void Update(string encryptedPatient) => EncryptedPatient = encryptedPatient;
}
