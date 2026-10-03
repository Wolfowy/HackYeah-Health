using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteInterviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_interviews_VisitProcessId",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.AddColumn<string>(
                name: "DoctorName",
                schema: "docprep",
                table: "visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoctorSpecialty",
                schema: "docprep",
                table: "visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacilityAddress",
                schema: "docprep",
                table: "visits",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacilityName",
                schema: "docprep",
                table: "visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationInstructions",
                schema: "docprep",
                table: "visits",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Room",
                schema: "docprep",
                table: "visits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "docprep",
                table: "visits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Europe/Warsaw");

            migrationBuilder.AddColumn<string>(
                name: "VisitType",
                schema: "docprep",
                table: "visits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "InPerson");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "docprep",
                table: "medications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalNotes",
                schema: "docprep",
                table: "interview_drafts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllergiesState",
                schema: "docprep",
                table: "interview_drafts",
                type: "text",
                nullable: false,
                defaultValue: "NotAsked");

            migrationBuilder.AddColumn<string>(
                name: "ChronicConditionsState",
                schema: "docprep",
                table: "interview_drafts",
                type: "text",
                nullable: false,
                defaultValue: "NotAsked");

            migrationBuilder.AddColumn<string>(
                name: "MedicationsState",
                schema: "docprep",
                table: "interview_drafts",
                type: "text",
                nullable: false,
                defaultValue: "NotAsked");

            migrationBuilder.AddColumn<string>(
                name: "ExtractionIssuesJson",
                schema: "docprep",
                table: "agent_interviews",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                schema: "docprep",
                table: "agent_interviews",
                type: "text",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                schema: "docprep",
                table: "agent_interviews",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ImportStatus",
                schema: "docprep",
                table: "agent_interviews",
                type: "text",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<int>(
                name: "ImportedDraftRevision",
                schema: "docprep",
                table: "agent_interviews",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportedFromSessionId",
                schema: "docprep",
                table: "agent_interviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StructuredDataSchemaVersion",
                schema: "docprep",
                table: "agent_interviews",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_interviews_VisitProcessId_Generation",
                schema: "docprep",
                table: "agent_interviews",
                columns: new[] { "VisitProcessId", "Generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_interviews_VisitProcessId_Generation",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "DoctorName",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "DoctorSpecialty",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "FacilityAddress",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "FacilityName",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "LocationInstructions",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "Room",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "VisitType",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "docprep",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "AdditionalNotes",
                schema: "docprep",
                table: "interview_drafts");

            migrationBuilder.DropColumn(
                name: "AllergiesState",
                schema: "docprep",
                table: "interview_drafts");

            migrationBuilder.DropColumn(
                name: "ChronicConditionsState",
                schema: "docprep",
                table: "interview_drafts");

            migrationBuilder.DropColumn(
                name: "MedicationsState",
                schema: "docprep",
                table: "interview_drafts");

            migrationBuilder.DropColumn(
                name: "ExtractionIssuesJson",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "Generation",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "ImportStatus",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "ImportedDraftRevision",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "ImportedFromSessionId",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.DropColumn(
                name: "StructuredDataSchemaVersion",
                schema: "docprep",
                table: "agent_interviews");

            migrationBuilder.CreateIndex(
                name: "IX_agent_interviews_VisitProcessId",
                schema: "docprep",
                table: "agent_interviews",
                column: "VisitProcessId",
                unique: true);
        }
    }
}
