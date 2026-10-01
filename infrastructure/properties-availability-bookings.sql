START TRANSACTION;
CREATE TABLE "CustomerProperties" (
    "Id" uuid NOT NULL,
    "CustomerId" text NOT NULL,
    "Label" character varying(80) NOT NULL,
    "AddressLine1" character varying(200) NOT NULL,
    "AddressLine2" character varying(200) NOT NULL,
    "City" character varying(100) NOT NULL,
    "Region" character varying(100) NOT NULL,
    "PostalCode" character varying(20) NOT NULL,
    "CountryCode" character varying(2) NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "ArchivedAt" timestamp with time zone,
    CONSTRAINT "PK_CustomerProperties" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CustomerProperties_AspNetUsers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_CustomerProperties_CustomerId_ArchivedAt" ON "CustomerProperties" ("CustomerId", "ArchivedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001112459_CustomerProperties', '10.0.4');

COMMIT;

START TRANSACTION;
CREATE TABLE "Bookings" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "PropertyId" uuid NOT NULL,
    "CustomerId" text NOT NULL,
    "Status" character varying(40) NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone NOT NULL,
    "ReservedFrom" timestamp with time zone NOT NULL,
    "ReservedUntil" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "SnapshotJson" text NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Bookings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Bookings_AspNetUsers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Bookings_CustomerProperties_PropertyId" FOREIGN KEY ("PropertyId") REFERENCES "CustomerProperties" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Bookings_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Bookings_ServiceListings_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES "ServiceListings" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ProviderSchedules" (
    "Id" uuid NOT NULL,
    "ConfigurationJson" text NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_ProviderSchedules" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProviderSchedules_Organizations_Id" FOREIGN KEY ("Id") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "BookingEvents" (
    "Id" uuid NOT NULL,
    "BookingId" uuid NOT NULL,
    "ActorId" text NOT NULL,
    "Status" character varying(40) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BookingEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_BookingEvents_Bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "BookingNotices" (
    "Id" uuid NOT NULL,
    "BookingId" uuid NOT NULL,
    "UserId" text NOT NULL,
    "Message" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BookingNotices" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_BookingNotices_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_BookingNotices_Bookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BookingEvents_BookingId" ON "BookingEvents" ("BookingId");

CREATE INDEX "IX_BookingNotices_BookingId" ON "BookingNotices" ("BookingId");

CREATE INDEX "IX_BookingNotices_UserId_CreatedAt" ON "BookingNotices" ("UserId", "CreatedAt");

CREATE INDEX "IX_Bookings_CustomerId_StartsAt" ON "Bookings" ("CustomerId", "StartsAt");

CREATE INDEX "IX_Bookings_OrganizationId_Status_ReservedFrom_ReservedUntil" ON "Bookings" ("OrganizationId", "Status", "ReservedFrom", "ReservedUntil");

CREATE INDEX "IX_Bookings_PropertyId" ON "Bookings" ("PropertyId");

CREATE INDEX "IX_Bookings_ServiceId" ON "Bookings" ("ServiceId");

CREATE INDEX "IX_Bookings_Status_ExpiresAt" ON "Bookings" ("Status", "ExpiresAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001113913_ProviderAvailabilityAndBookings', '10.0.4');

COMMIT;

START TRANSACTION;
ALTER TABLE "Bookings" ADD CONSTRAINT "CK_Booking_Interval" CHECK ("StartsAt" < "EndsAt" AND "ReservedFrom" <= "StartsAt" AND "ReservedUntil" >= "EndsAt");

ALTER TABLE "Bookings" ADD CONSTRAINT "CK_Booking_Status" CHECK ("Status" IN ('PendingProviderConfirmation','Confirmed','InProgress','Completed','Declined','Expired','CancelledByCustomer','CancelledByProvider'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001114518_BookingReservationConstraints', '10.0.4');

COMMIT;

