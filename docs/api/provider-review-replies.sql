START TRANSACTION;
CREATE TABLE "ProviderReviewReplies" (
    "BookingId" uuid NOT NULL,
    "ActorId" text NOT NULL,
    "Comment" character varying(2000) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ProviderReviewReplies" PRIMARY KEY ("BookingId"),
    CONSTRAINT "CK_ProviderReviewReply_Comment" CHECK (length(trim("Comment")) >= 1 AND length("Comment") <= 2000),
    CONSTRAINT "FK_ProviderReviewReplies_AspNetUsers_ActorId" FOREIGN KEY ("ActorId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProviderReviewReplies_BookingReviews_BookingId" FOREIGN KEY ("BookingId") REFERENCES "BookingReviews" ("BookingId") ON DELETE RESTRICT
);

CREATE INDEX "IX_ProviderReviewReplies_ActorId" ON "ProviderReviewReplies" ("ActorId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002121600_ProviderReviewReplies', '10.0.4');

COMMIT;
