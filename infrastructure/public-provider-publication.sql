START TRANSACTION;
ALTER TABLE "ProviderProfiles" ADD "PublishedAt" timestamp with time zone;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001100100_PublicProviderPublication', '10.0.4');

COMMIT;

