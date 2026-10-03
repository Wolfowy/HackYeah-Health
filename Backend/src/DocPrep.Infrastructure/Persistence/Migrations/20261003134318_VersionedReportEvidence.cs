using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionedReportEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_evidence",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReportVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceFragment = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_evidence_report_versions_ReportVersionId",
                        column: x => x.ReportVersionId,
                        principalSchema: "docprep",
                        principalTable: "report_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_report_evidence_ReportVersionId",
                schema: "docprep",
                table: "report_evidence",
                column: "ReportVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_evidence",
                schema: "docprep");
        }
    }
}
