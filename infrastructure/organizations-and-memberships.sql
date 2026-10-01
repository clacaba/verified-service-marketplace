START TRANSACTION;
ALTER TABLE "VerificationEmails" ADD "Subject" character varying(200) NOT NULL DEFAULT 'Verify your marketplace email';

CREATE TABLE "Organizations" (
    "Id" uuid NOT NULL,
    "Name" character varying(150) NOT NULL,
    "ProviderType" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Organizations" PRIMARY KEY ("Id")
);

CREATE TABLE "OrganizationAuditEvents" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "ActorId" text NOT NULL,
    "Action" text NOT NULL,
    "ResourceId" text NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_OrganizationAuditEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OrganizationAuditEvents_AspNetUsers_ActorId" FOREIGN KEY ("ActorId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_OrganizationAuditEvents_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
);

CREATE TABLE "OrganizationInvitations" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "NormalizedEmail" character varying(254) NOT NULL,
    "Email" character varying(254) NOT NULL,
    "Role" character varying(20) NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "AcceptedAt" timestamp with time zone,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_OrganizationInvitations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OrganizationInvitations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
);

CREATE TABLE "OrganizationMemberships" (
    "OrganizationId" uuid NOT NULL,
    "UserId" text NOT NULL,
    "Role" character varying(20) NOT NULL,
    "Active" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_OrganizationMemberships" PRIMARY KEY ("OrganizationId", "UserId"),
    CONSTRAINT "FK_OrganizationMemberships_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_OrganizationMemberships_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OrganizationAuditEvents_ActorId" ON "OrganizationAuditEvents" ("ActorId");

CREATE INDEX "IX_OrganizationAuditEvents_OrganizationId_OccurredAt" ON "OrganizationAuditEvents" ("OrganizationId", "OccurredAt");

CREATE INDEX "IX_OrganizationInvitations_OrganizationId" ON "OrganizationInvitations" ("OrganizationId");

CREATE UNIQUE INDEX "IX_OrganizationInvitations_TokenHash" ON "OrganizationInvitations" ("TokenHash");

CREATE INDEX "IX_OrganizationMemberships_UserId" ON "OrganizationMemberships" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001075019_OrganizationsAndMemberships', '10.0.4');

COMMIT;

