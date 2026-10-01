START TRANSACTION;
CREATE TABLE "EvidenceDeletions" (
    "Id" uuid NOT NULL,
    "ObjectKey" character varying(200) NOT NULL,
    "DueAt" timestamp with time zone NOT NULL,
    "Attempts" integer NOT NULL,
    CONSTRAINT "PK_EvidenceDeletions" PRIMARY KEY ("Id")
);

CREATE TABLE "VerificationApplications" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "ProviderId" uuid NOT NULL,
    "Status" character varying(40) NOT NULL,
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    "SubmissionVersion" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "SubmittedAt" timestamp with time zone,
    "SnapshotJson" text NOT NULL,
    CONSTRAINT "PK_VerificationApplications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_VerificationApplications_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VerificationApplications_ProviderProfiles_ProviderId" FOREIGN KEY ("ProviderId") REFERENCES "ProviderProfiles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VerificationDocuments" (
    "Id" uuid NOT NULL,
    "ApplicationId" uuid NOT NULL,
    "Version" uuid NOT NULL,
    "EvidenceType" character varying(40) NOT NULL,
    "FileName" character varying(120) NOT NULL,
    "ContentType" character varying(50) NOT NULL,
    "Size" bigint NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "ObjectKey" character varying(200) NOT NULL,
    "UploadedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "DeletedAt" timestamp with time zone,
    CONSTRAINT "PK_VerificationDocuments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_VerificationDocuments_VerificationApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "VerificationApplications" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_EvidenceDeletions_DueAt" ON "EvidenceDeletions" ("DueAt");

CREATE UNIQUE INDEX "IX_VerificationApplications_OrganizationId" ON "VerificationApplications" ("OrganizationId") WHERE "IsActive" = TRUE;

CREATE INDEX "IX_VerificationApplications_ProviderId" ON "VerificationApplications" ("ProviderId");

CREATE INDEX "IX_VerificationDocuments_ApplicationId" ON "VerificationDocuments" ("ApplicationId");

CREATE INDEX "IX_VerificationDocuments_ExpiresAt" ON "VerificationDocuments" ("ExpiresAt");

CREATE UNIQUE INDEX "IX_VerificationDocuments_ObjectKey" ON "VerificationDocuments" ("ObjectKey");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001083226_PrivateVerificationEvidence', '10.0.4');

COMMIT;

