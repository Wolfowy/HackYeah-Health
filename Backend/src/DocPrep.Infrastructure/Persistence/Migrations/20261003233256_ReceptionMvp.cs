using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceptionMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                schema: "docprep",
                table: "visits",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedToken",
                schema: "docprep",
                table: "interview_invitations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSimulated",
                schema: "docprep",
                table: "delivery_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "clinicians",
                schema: "docprep",
                columns: table => new
                {
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Specialty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultRoom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinicians", x => new { x.FacilityId, x.Id });
                    table.ForeignKey(
                        name: "FK_clinicians_facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "docprep",
                        principalTable: "facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reception_details",
                schema: "docprep",
                columns: table => new
                {
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedPatient = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reception_details", x => x.VisitProcessId);
                    table.ForeignKey(
                        name: "FK_reception_details_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visits_FacilityId_ScheduledAt",
                schema: "docprep",
                table: "visits",
                columns: new[] { "FacilityId", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinicians",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "reception_details",
                schema: "docprep");

            migrationBuilder.DropIndex(
                name: "IX_visits_FacilityId_ScheduledAt",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                schema: "docprep",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "EncryptedToken",
                schema: "docprep",
                table: "interview_invitations");

            migrationBuilder.DropColumn(
                name: "IsSimulated",
                schema: "docprep",
                table: "delivery_attempts");
        }
    }
}
