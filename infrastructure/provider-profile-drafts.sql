START TRANSACTION;
CREATE TABLE "ProviderProfiles" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "DisplayName" character varying(150) NOT NULL,
    "ProviderType" character varying(20) NOT NULL,
    "Biography" character varying(4000) NOT NULL,
    "ServiceArea" character varying(250) NOT NULL,
    "Version" uuid NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ProviderProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProviderProfiles_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ServiceCategories" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Active" boolean NOT NULL,
    CONSTRAINT "PK_ServiceCategories" PRIMARY KEY ("Id")
);

CREATE TABLE "ProviderCategories" (
    "ProfileId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    CONSTRAINT "PK_ProviderCategories" PRIMARY KEY ("ProfileId", "CategoryId"),
    CONSTRAINT "FK_ProviderCategories_ProviderProfiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "ProviderProfiles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProviderCategories_ServiceCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "ServiceCategories" ("Id") ON DELETE RESTRICT
);

INSERT INTO "ServiceCategories" ("Id", "Active", "Name")
VALUES ('10000000-0000-0000-0000-000000000001', TRUE, 'Cleaning');
INSERT INTO "ServiceCategories" ("Id", "Active", "Name")
VALUES ('10000000-0000-0000-0000-000000000002', TRUE, 'Plumbing');
INSERT INTO "ServiceCategories" ("Id", "Active", "Name")
VALUES ('10000000-0000-0000-0000-000000000003', TRUE, 'Electrical');
INSERT INTO "ServiceCategories" ("Id", "Active", "Name")
VALUES ('10000000-0000-0000-0000-000000000004', TRUE, 'Appliance repair');

CREATE INDEX "IX_ProviderCategories_CategoryId" ON "ProviderCategories" ("CategoryId");

CREATE UNIQUE INDEX "IX_ProviderProfiles_OrganizationId" ON "ProviderProfiles" ("OrganizationId");

INSERT INTO "ProviderProfiles" ("Id", "OrganizationId", "DisplayName", "ProviderType", "Biography", "ServiceArea", "Version", "UpdatedAt")
SELECT "Id", "Id", "Name", "ProviderType", '', '', "Id", CURRENT_TIMESTAMP FROM "Organizations";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001080351_ProviderProfileDrafts', '10.0.4');

COMMIT;

