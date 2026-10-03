namespace DocPrep.Domain.Reports;

public enum PdfGenerationStatus { Pending, Ready, Failed }

public sealed class ReportVersion
{
    private ReportVersion() { }
    public ReportVersion(Guid id, Guid visitId, int number, int schemaVersion, string snapshotJson, string jsonSha256,
        bool confirmedIncomplete, int draftRevision, DateTimeOffset now)
    {
        Id = id; VisitProcessId = visitId; VersionNumber = number; SchemaVersion = schemaVersion; SnapshotJson = snapshotJson;
        JsonSha256 = jsonSha256; PdfStatus = PdfGenerationStatus.Pending;
        ConfirmedIncomplete = confirmedIncomplete; CreatedFromDraftRevision = draftRevision; ApprovedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid VisitProcessId { get; private set; }
    public int VersionNumber { get; private set; }
    public int SchemaVersion { get; private set; }
    public string SnapshotJson { get; private set; } = ""; public string JsonSha256 { get; private set; } = "";
    public byte[] PdfData { get; private set; } = []; public string PdfSha256 { get; private set; } = ""; public PdfGenerationStatus PdfStatus { get; private set; }
    public bool ConfirmedIncomplete { get; private set; }
    public int CreatedFromDraftRevision { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public void CompletePdf(byte[] data, string sha256) { PdfData = data; PdfSha256 = sha256; PdfStatus = PdfGenerationStatus.Ready; }
    public void FailPdf() { PdfData = []; PdfSha256 = ""; PdfStatus = PdfGenerationStatus.Failed; }
}

public sealed class ReportEvidence
{
    private ReportEvidence() { }
    public ReportEvidence(Guid reportVersionId, Guid observationId, Guid sourceVisitId, Guid sourceReportVersionId, DateTimeOffset sourceDate, string sourceFragment)
    { Id = Guid.NewGuid(); ReportVersionId = reportVersionId; ObservationId = observationId; SourceVisitId = sourceVisitId; SourceReportVersionId = sourceReportVersionId; SourceDate = sourceDate; SourceFragment = sourceFragment; }
    public Guid Id { get; private set; }
    public Guid ReportVersionId { get; private set; }
    public Guid ObservationId { get; private set; }
    public Guid SourceVisitId { get; private set; }
    public Guid SourceReportVersionId { get; private set; }
    public DateTimeOffset SourceDate { get; private set; }
    public string SourceFragment { get; private set; } = "";
}
