START TRANSACTION;
ALTER TABLE "VerificationApplications" ADD "ParentApplicationId" uuid;

ALTER TABLE "VerificationApplications" ADD "ReviewLeaseExpiresAt" timestamp with time zone;

ALTER TABLE "VerificationApplications" ADD "ReviewStartedAt" timestamp with time zone;

ALTER TABLE "VerificationApplications" ADD "ReviewStartedBy" text;

ALTER TABLE "VerificationApplications" ADD "RevisionNumber" integer NOT NULL DEFAULT 0;

ALTER TABLE "AspNetUsers" ADD "IsReviewer" boolean NOT NULL DEFAULT FALSE;

CREATE TABLE "Notifications" (
    "Id" uuid NOT NULL,
    "UserId" text NOT NULL,
    "ApplicationId" uuid NOT NULL,
    "Message" character varying(2200) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReadAt" timestamp with time zone,
    CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Notifications_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Notifications_VerificationApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "VerificationApplications" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PlatformAccessAudits" (
    "Id" uuid NOT NULL,
    "UserId" text NOT NULL,
    "Action" text NOT NULL,
    "Source" text NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PlatformAccessAudits" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PlatformAccessAudits_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VerificationDecisions" (
    "Id" uuid NOT NULL,
    "ApplicationId" uuid NOT NULL,
    "ReviewerId" text NOT NULL,
    "Decision" character varying(40) NOT NULL,
    "Reason" character varying(2000) NOT NULL,
    "DecidedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_VerificationDecisions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_VerificationDecisions_AspNetUsers_ReviewerId" FOREIGN KEY ("ReviewerId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VerificationDecisions_VerificationApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "VerificationApplications" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VerificationChecks" (
    "Id" uuid NOT NULL,
    "DecisionId" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "Type" character varying(40) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "Simulated" boolean NOT NULL,
    CONSTRAINT "PK_VerificationChecks" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_VerificationChecks_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VerificationChecks_VerificationDecisions_DecisionId" FOREIGN KEY ("DecisionId") REFERENCES "VerificationDecisions" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_VerificationApplications_ParentApplicationId" ON "VerificationApplications" ("ParentApplicationId") WHERE "ParentApplicationId" IS NOT NULL;

CREATE INDEX "IX_VerificationApplications_ReviewStartedBy" ON "VerificationApplications" ("ReviewStartedBy");

CREATE INDEX "IX_Notifications_ApplicationId" ON "Notifications" ("ApplicationId");

CREATE INDEX "IX_Notifications_UserId_CreatedAt" ON "Notifications" ("UserId", "CreatedAt");

CREATE INDEX "IX_PlatformAccessAudits_UserId" ON "PlatformAccessAudits" ("UserId");

CREATE INDEX "IX_VerificationChecks_DecisionId" ON "VerificationChecks" ("DecisionId");

CREATE INDEX "IX_VerificationChecks_OrganizationId_ExpiresAt" ON "VerificationChecks" ("OrganizationId", "ExpiresAt");

CREATE INDEX "IX_VerificationDecisions_ApplicationId" ON "VerificationDecisions" ("ApplicationId");

CREATE INDEX "IX_VerificationDecisions_ReviewerId" ON "VerificationDecisions" ("ReviewerId");

ALTER TABLE "VerificationApplications" ADD CONSTRAINT "FK_VerificationApplications_AspNetUsers_ReviewStartedBy" FOREIGN KEY ("ReviewStartedBy") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT;

ALTER TABLE "VerificationApplications" ADD CONSTRAINT "FK_VerificationApplications_VerificationApplications_ParentApp~" FOREIGN KEY ("ParentApplicationId") REFERENCES "VerificationApplications" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001085833_AdministratorReviewAndRevisions', '10.0.4');

COMMIT;

