using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_webhook_events_VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events",
                column: "VisitProcessId");

            migrationBuilder.AddForeignKey(
                name: "FK_external_webhook_events_visits_VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events",
                column: "VisitProcessId",
                principalSchema: "docprep",
                principalTable: "visits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_external_webhook_events_visits_VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events");

            migrationBuilder.DropIndex(
                name: "IX_external_webhook_events_VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events");

            migrationBuilder.DropColumn(
                name: "VisitProcessId",
                schema: "docprep",
                table: "external_webhook_events");
        }
    }
}
