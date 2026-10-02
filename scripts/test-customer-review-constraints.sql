-- Local PostgreSQL smoke check: changes are ALWAYS rolled back. Requires one unreviewed demo booking.
BEGIN;
DO $review_check$
DECLARE
    chosen uuid;
BEGIN
    SELECT b."Id" INTO chosen FROM "Bookings" b
    WHERE NOT EXISTS (SELECT 1 FROM "BookingReviews" r WHERE r."BookingId" = b."Id")
    ORDER BY b."CreatedAt" LIMIT 1;
    IF chosen IS NULL THEN RAISE EXCEPTION 'Create an unreviewed fictional booking before this constraint check.'; END IF;
    INSERT INTO "BookingReviews" ("BookingId","Rating","Comment","Status","CreatedAt")
    VALUES (chosen,4,repeat(chr(128512),5),'Published',now());
    BEGIN
        INSERT INTO "BookingReviews" ("BookingId","Rating","Comment","Status","CreatedAt")
        VALUES (chosen,5,'Duplicate fictional feedback.','Published',now());
        RAISE EXCEPTION 'Duplicate review unexpectedly accepted.';
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASS: one-review-per-booking constraint.';
    END;
    BEGIN
        INSERT INTO "BookingReviews" ("BookingId","Rating","Comment","Status","CreatedAt")
        VALUES (chosen,6,'Invalid fictional rating.','Published',now());
        RAISE EXCEPTION 'Invalid rating unexpectedly accepted.';
    EXCEPTION WHEN check_violation THEN RAISE NOTICE 'PASS: rating constraint.';
    END;
    BEGIN
        INSERT INTO "BookingReviews" ("BookingId","Rating","Comment","Status","CreatedAt")
        VALUES (chosen,4,'','Published',now());
        RAISE EXCEPTION 'Empty feedback unexpectedly accepted.';
    EXCEPTION WHEN check_violation THEN RAISE NOTICE 'PASS: nonempty-feedback constraint.';
    END;
    RAISE NOTICE 'PASS: Unicode feedback and PostgreSQL constraints; transaction will roll back.';
END $review_check$;
ROLLBACK;
