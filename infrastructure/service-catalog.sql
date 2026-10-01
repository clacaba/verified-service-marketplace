START TRANSACTION;
CREATE TABLE "ServiceListings" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "Title" character varying(150) NOT NULL,
    "Description" character varying(4000) NOT NULL,
    "PriceMinor" bigint NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "ServiceArea" character varying(250) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ServiceListings" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ServiceListing_Currency" CHECK ("Currency" = 'PHP'),
    CONSTRAINT "CK_ServiceListing_Duration" CHECK ("DurationMinutes" >= 15 AND "DurationMinutes" <= 1440 AND "DurationMinutes" % 15 = 0),
    CONSTRAINT "CK_ServiceListing_Price" CHECK ("PriceMinor" >= 1 AND "PriceMinor" <= 100000000),
    CONSTRAINT "CK_ServiceListing_Status" CHECK ("Status" IN ('Draft', 'Published', 'Archived')),
    CONSTRAINT "FK_ServiceListings_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ServiceListings_ServiceCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "ServiceCategories" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_ServiceListings_CategoryId" ON "ServiceListings" ("CategoryId");

CREATE INDEX "IX_ServiceListings_OrganizationId_Status" ON "ServiceListings" ("OrganizationId", "Status");

CREATE INDEX "IX_ServiceListings_Status_CategoryId_PriceMinor" ON "ServiceListings" ("Status", "CategoryId", "PriceMinor");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001102438_ServiceCatalog', '10.0.4');

COMMIT;

