-- Apply once to an existing HealthPrep schema, before starting the updated API.
-- Generated from the EF model verified on PostgreSQL. No existing data is changed.
BEGIN;

CREATE TABLE healthprep.agent_interviews (
    "Id" uuid NOT NULL,
    "AppointmentId" uuid NOT NULL,
    "Status" text NOT NULL,
    "Summary" text,
    "StructuredDataJson" jsonb NOT NULL,
    "CompletedAt" timestamp with time zone,
    "ResultSessionStartedAt" timestamp with time zone
);

CREATE TABLE healthprep.agent_sessions (
    "Id" uuid NOT NULL,
    "InterviewId" uuid NOT NULL,
    "InvitationId" uuid,
    "Mode" text NOT NULL,
    "Status" text NOT NULL,
    "ProviderConversationId" text,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "TranscriptJson" jsonb,
    "AnalysisJson" jsonb,
    "MetadataJson" jsonb,
    "StructuredDataJson" jsonb NOT NULL,
    "Summary" text,
    "ContinuesInterview" boolean NOT NULL
);

CREATE TABLE healthprep.interview_invitations (
    "Id" uuid NOT NULL,
    "InterviewId" uuid NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "SessionCount" integer NOT NULL,
    "MaxSessions" integer NOT NULL
);

ALTER TABLE ONLY healthprep.agent_interviews
    ADD CONSTRAINT "PK_agent_interviews" PRIMARY KEY ("Id");

ALTER TABLE ONLY healthprep.agent_sessions
    ADD CONSTRAINT "PK_agent_sessions" PRIMARY KEY ("Id");

ALTER TABLE ONLY healthprep.interview_invitations
    ADD CONSTRAINT "PK_interview_invitations" PRIMARY KEY ("Id");

CREATE UNIQUE INDEX "IX_agent_interviews_AppointmentId" ON healthprep.agent_interviews USING btree ("AppointmentId");

CREATE INDEX "IX_agent_sessions_InterviewId" ON healthprep.agent_sessions USING btree ("InterviewId");

CREATE INDEX "IX_agent_sessions_InvitationId" ON healthprep.agent_sessions USING btree ("InvitationId");

CREATE UNIQUE INDEX "IX_agent_sessions_ProviderConversationId" ON healthprep.agent_sessions USING btree ("ProviderConversationId");

CREATE INDEX "IX_interview_invitations_InterviewId" ON healthprep.interview_invitations USING btree ("InterviewId");

CREATE UNIQUE INDEX "IX_interview_invitations_TokenHash" ON healthprep.interview_invitations USING btree ("TokenHash");

ALTER TABLE ONLY healthprep.agent_interviews
    ADD CONSTRAINT "FK_agent_interviews_Appointments_AppointmentId" FOREIGN KEY ("AppointmentId") REFERENCES healthprep."Appointments"("Id") ON DELETE CASCADE;

ALTER TABLE ONLY healthprep.agent_sessions
    ADD CONSTRAINT "FK_agent_sessions_agent_interviews_InterviewId" FOREIGN KEY ("InterviewId") REFERENCES healthprep.agent_interviews("Id") ON DELETE CASCADE;

ALTER TABLE ONLY healthprep.agent_sessions
    ADD CONSTRAINT "FK_agent_sessions_interview_invitations_InvitationId" FOREIGN KEY ("InvitationId") REFERENCES healthprep.interview_invitations("Id") ON DELETE SET NULL;

ALTER TABLE ONLY healthprep.interview_invitations
    ADD CONSTRAINT "FK_interview_invitations_agent_interviews_InterviewId" FOREIGN KEY ("InterviewId") REFERENCES healthprep.agent_interviews("Id") ON DELETE CASCADE;

COMMIT;
