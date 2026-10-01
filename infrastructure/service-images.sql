START TRANSACTION;
CREATE TABLE "ServiceImageDeletions" (
    "Id" uuid NOT NULL,
    "ObjectKey" character varying(200) NOT NULL,
    "DueAt" timestamp with time zone NOT NULL,
    "Attempts" integer NOT NULL,
    CONSTRAINT "PK_ServiceImageDeletions" PRIMARY KEY ("Id")
);

CREATE TABLE "ServiceImages" (
    "Id" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "AltText" character varying(150) NOT NULL,
    "ObjectKey" character varying(200) NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "Width" integer NOT NULL,
    "Height" integer NOT NULL,
    "Size" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "DeletedAt" timestamp with time zone,
    CONSTRAINT "PK_ServiceImages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ServiceImages_ServiceListings_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "ServiceListings" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_ServiceImageDeletions_DueAt" ON "ServiceImageDeletions" ("DueAt");

CREATE UNIQUE INDEX "IX_ServiceImages_ObjectKey" ON "ServiceImages" ("ObjectKey");

CREATE INDEX "IX_ServiceImages_ServiceId" ON "ServiceImages" ("ServiceId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001105940_ServiceImages', '10.0.4');

COMMIT;

