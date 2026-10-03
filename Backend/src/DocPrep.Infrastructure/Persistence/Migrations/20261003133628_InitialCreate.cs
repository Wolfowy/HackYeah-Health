using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocPrep.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "docprep");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "deletion_requests",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientCorrelationKey = table.Column<string>(type: "text", nullable: false),
                    RequestedByFacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationReference = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deletion_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "facilities",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EncryptedPesel = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalVisitId = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedClinicianId = table.Column<string>(type: "text", nullable: true),
                    ContactChannel = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LatestApprovedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestSharedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_visits_facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "docprep",
                        principalTable: "facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_patients_PatientIdentityId",
                        column: x => x.PatientIdentityId,
                        principalSchema: "docprep",
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    EncryptedDestination = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interview_drafts",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultationReason = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_interview_drafts_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_access_grants",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkTokenHash = table.Column<string>(type: "text", nullable: false),
                    VisitCodeHash = table.Column<string>(type: "text", nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_access_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_access_grants_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_versions",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    JsonSha256 = table.Column<string>(type: "text", nullable: false),
                    PdfData = table.Column<byte[]>(type: "bytea", nullable: false),
                    PdfSha256 = table.Column<string>(type: "text", nullable: false),
                    PdfStatus = table.Column<string>(type: "text", nullable: false),
                    ConfirmedIncomplete = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedFromDraftRevision = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_versions_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sharing_consents",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionSource = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sharing_consents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sharing_consents_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplementation_rounds",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    OpenedByClinicianId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplementation_rounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplementation_rounds_visits_VisitProcessId",
                        column: x => x.VisitProcessId,
                        principalSchema: "docprep",
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allergies",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Substance = table.Column<string>(type: "text", nullable: false),
                    Reaction = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allergies_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chronic_conditions",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chronic_conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chronic_conditions_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clarifications",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldPath = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Resolution = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clarifications_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interview_answers",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_interview_answers_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medications",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Dose = table.Column<string>(type: "text", nullable: true),
                    DoseState = table.Column<string>(type: "text", nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medications_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observation_proposals",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymptomName = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    PatientEditedText = table.Column<string>(type: "text", nullable: true),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observation_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_observation_proposals_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_questions",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_questions_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "symptoms",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    StartedOnState = table.Column<string>(type: "text", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: true),
                    DailyImpact = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symptoms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_symptoms_interview_drafts_InterviewDraftId",
                        column: x => x.InterviewDraftId,
                        principalSchema: "docprep",
                        principalTable: "interview_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplementation_questions",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplementationRoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AskedByClinicianId = table.Column<string>(type: "text", nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplementation_questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplementation_questions_supplementation_rounds_Supplement~",
                        column: x => x.SupplementationRoundId,
                        principalSchema: "docprep",
                        principalTable: "supplementation_rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observation_evidence",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReportVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceFragment = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observation_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_observation_evidence_observation_proposals_ObservationPropo~",
                        column: x => x.ObservationProposalId,
                        principalSchema: "docprep",
                        principalTable: "observation_proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "symptom_timeline",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SymptomId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Period = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symptom_timeline", x => x.Id);
                    table.ForeignKey(
                        name: "FK_symptom_timeline_symptoms_SymptomId",
                        column: x => x.SymptomId,
                        principalSchema: "docprep",
                        principalTable: "symptoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplementation_answers",
                schema: "docprep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplementationQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplementation_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplementation_answers_supplementation_questions_Supplemen~",
                        column: x => x.SupplementationQuestionId,
                        principalSchema: "docprep",
                        principalTable: "supplementation_questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "docprep",
                table: "facilities",
                columns: new[] { "Id", "Name" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "Przychodnia Demo" });

            migrationBuilder.CreateIndex(
                name: "IX_allergies_InterviewDraftId",
                schema: "docprep",
                table: "allergies",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_VisitProcessId_OccurredAt",
                schema: "docprep",
                table: "audit_events",
                columns: new[] { "VisitProcessId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_chronic_conditions_InterviewDraftId",
                schema: "docprep",
                table: "chronic_conditions",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_clarifications_InterviewDraftId",
                schema: "docprep",
                table: "clarifications",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_VisitProcessId",
                schema: "docprep",
                table: "delivery_attempts",
                column: "VisitProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_interview_answers_InterviewDraftId",
                schema: "docprep",
                table: "interview_answers",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_interview_drafts_VisitProcessId",
                schema: "docprep",
                table: "interview_drafts",
                column: "VisitProcessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medications_InterviewDraftId",
                schema: "docprep",
                table: "medications",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_observation_evidence_ObservationProposalId",
                schema: "docprep",
                table: "observation_evidence",
                column: "ObservationProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_observation_proposals_InterviewDraftId",
                schema: "docprep",
                table: "observation_proposals",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_access_grants_LinkTokenHash",
                schema: "docprep",
                table: "patient_access_grants",
                column: "LinkTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_access_grants_VisitCodeHash",
                schema: "docprep",
                table: "patient_access_grants",
                column: "VisitCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_access_grants_VisitProcessId",
                schema: "docprep",
                table: "patient_access_grants",
                column: "VisitProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_questions_InterviewDraftId",
                schema: "docprep",
                table: "patient_questions",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_patients_CorrelationKey",
                schema: "docprep",
                table: "patients",
                column: "CorrelationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_versions_VisitProcessId_VersionNumber",
                schema: "docprep",
                table: "report_versions",
                columns: new[] { "VisitProcessId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sharing_consents_VisitProcessId_RevokedAt",
                schema: "docprep",
                table: "sharing_consents",
                columns: new[] { "VisitProcessId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_supplementation_answers_SupplementationQuestionId",
                schema: "docprep",
                table: "supplementation_answers",
                column: "SupplementationQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplementation_questions_SupplementationRoundId",
                schema: "docprep",
                table: "supplementation_questions",
                column: "SupplementationRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_supplementation_rounds_VisitProcessId",
                schema: "docprep",
                table: "supplementation_rounds",
                column: "VisitProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_symptom_timeline_SymptomId",
                schema: "docprep",
                table: "symptom_timeline",
                column: "SymptomId");

            migrationBuilder.CreateIndex(
                name: "IX_symptoms_InterviewDraftId",
                schema: "docprep",
                table: "symptoms",
                column: "InterviewDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_visits_FacilityId_ExternalVisitId",
                schema: "docprep",
                table: "visits",
                columns: new[] { "FacilityId", "ExternalVisitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_visits_PatientIdentityId",
                schema: "docprep",
                table: "visits",
                column: "PatientIdentityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allergies",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "chronic_conditions",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "clarifications",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "deletion_requests",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "interview_answers",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "medications",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "observation_evidence",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "patient_access_grants",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "patient_questions",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "report_versions",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "sharing_consents",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "supplementation_answers",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "symptom_timeline",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "observation_proposals",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "supplementation_questions",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "symptoms",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "supplementation_rounds",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "interview_drafts",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "facilities",
                schema: "docprep");

            migrationBuilder.DropTable(
                name: "patients",
                schema: "docprep");
        }
    }
}
