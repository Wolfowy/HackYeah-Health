using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ElevenLabsAgentInterviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_interviews",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    InterviewType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinalReport = table.Column<string>(type: "text", nullable: true),
                    StructuredDataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_interviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_interviews_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_webhook_events",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_interview_sessions",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderConversationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TranscriptJson = table.Column<string>(type: "jsonb", nullable: true),
                    AnalysisJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_interview_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_interview_sessions_agent_interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalSchema: "docprep",
                        principalTable: "agent_interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_interview_sessions_staff_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "docprep",
                        principalTable: "staff_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "interview_invitations",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstOpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SessionCount = table.Column<int>(type: "integer", nullable: false),
                    MaxSessionCount = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_interview_invitations_agent_interviews_InterviewId",
                        column: x => x.InterviewId,
                        principalSchema: "docprep",
                        principalTable: "agent_interviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_interview_sessions_InterviewId",
                schema: "docprep",
                table: "agent_interview_sessions",
                column: "InterviewId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_interview_sessions_ProviderConversationId",
                schema: "docprep",
                table: "agent_interview_sessions",
                column: "ProviderConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_interview_sessions_UserId",
                schema: "docprep",
                table: "agent_interview_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_interviews_VisitProcessId",
                schema: "docprep",
                table: "agent_interviews",
                column: "VisitProcessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_webhook_events_Provider_ExternalEventId",
                schema: "docprep",
                table: "external_webhook_events",
                columns: new[] { "Provider", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interview_invitations_InterviewId",
                schema: "docprep",
                table: "interview_invitations",
                column: "InterviewId");

            migrationBuilder.CreateIndex(
                name: "IX_interview_invitations_TokenHash",
                schema: "docprep",
                table: "interview_invitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_interview_sessions",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "external_webhook_events",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "interview_invitations",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "agent_interviews",
                schema: "docprep");
        }
    }
}
