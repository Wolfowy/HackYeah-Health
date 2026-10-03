using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_accounts",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_accounts_patients_PatientIdentityId",
                        column: x => x.PatientIdentityId,
                        principalSchema: "docprep",
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_refresh_tokens",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_refresh_tokens_patient_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "docprep",
                        principalTable: "patient_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patient_accounts_Email",
                schema: "docprep",
                table: "patient_accounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_accounts_PatientIdentityId",
                schema: "docprep",
                table: "patient_accounts",
                column: "PatientIdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_refresh_tokens_AccountId",
                schema: "docprep",
                table: "patient_refresh_tokens",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_refresh_tokens_TokenHash",
                schema: "docprep",
                table: "patient_refresh_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_refresh_tokens",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "patient_accounts",
                schema: "docprep");
        }
    }
}
