START TRANSACTION;
CREATE TABLE "BookingReviews" (
    "BookingId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Comment" character varying(2000) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BookingReviews" PRIMARY KEY ("BookingId"),
    CONSTRAINT "CK_BookingReview_Comment" CHECK (length(trim("Comment")) >= 1 AND length("Comment") <= 2000),
    CONSTRAINT "CK_BookingReview_Rating" CHECK ("Rating" >= 1 AND "Rating" <= 5),
    CONSTRAINT "CK_BookingReview_Status" CHECK ("Status" IN ('Published','Hidden')),
    CONSTRAINT "FK_BookingReviews_Bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BookingReviews_Status_CreatedAt" ON "BookingReviews" ("Status", "CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002112613_CompletedBookingReviews', '10.0.4');

COMMIT;
