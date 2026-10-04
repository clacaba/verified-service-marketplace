-- PostgreSQL storage checks only. All temporary feedback and responses are rolled back.
BEGIN;
DO $reply_check$
DECLARE chosen uuid; actor text;
BEGIN
    SELECT r."BookingId",m."UserId" INTO chosen,actor FROM "BookingReviews" r
    JOIN "Bookings" b ON b."Id"=r."BookingId"
    JOIN "OrganizationMemberships" m ON m."OrganizationId"=b."OrganizationId" AND m."Active" AND m."Role"='Owner'
    WHERE NOT EXISTS(SELECT 1 FROM "ProviderReviewReplies" p WHERE p."BookingId"=r."BookingId") LIMIT 1;
    IF chosen IS NULL THEN
        SELECT b."Id",m."UserId" INTO chosen,actor FROM "Bookings" b
        JOIN "OrganizationMemberships" m ON m."OrganizationId"=b."OrganizationId" AND m."Active" AND m."Role"='Owner'
        WHERE NOT EXISTS(SELECT 1 FROM "BookingReviews" r WHERE r."BookingId"=b."Id") LIMIT 1;
        IF chosen IS NULL THEN RAISE EXCEPTION 'Create a fictional unreviewed/unreplied booking before storage checks.'; END IF;
        INSERT INTO "BookingReviews"("BookingId","Rating","Comment","Status","CreatedAt")
        VALUES(chosen,3,'Temporary fictional feedback.','Published',now());
    END IF;
    INSERT INTO "ProviderReviewReplies"("BookingId","ActorId","Comment","CreatedAt")
    VALUES(chosen,actor,repeat(chr(128512),5),now());
    BEGIN
        INSERT INTO "ProviderReviewReplies"("BookingId","ActorId","Comment","CreatedAt")
        VALUES(chosen,actor,'Duplicate fictional response.',now());
        RAISE EXCEPTION 'Duplicate response unexpectedly accepted.';
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASS: one-response-per-review constraint.'; END;
    BEGIN
        INSERT INTO "ProviderReviewReplies"("BookingId","ActorId","Comment","CreatedAt") VALUES(chosen,actor,'',now());
        RAISE EXCEPTION 'Empty response unexpectedly accepted.';
    EXCEPTION WHEN check_violation THEN RAISE NOTICE 'PASS: nonempty-response constraint.'; END;
    BEGIN
        INSERT INTO "ProviderReviewReplies"("BookingId","ActorId","Comment","CreatedAt") VALUES(gen_random_uuid(),actor,'Missing fictional review.',now());
        RAISE EXCEPTION 'Response without review unexpectedly accepted.';
    EXCEPTION WHEN foreign_key_violation THEN RAISE NOTICE 'PASS: existing-review foreign key.'; END;
    RAISE NOTICE 'PASS: Unicode response storage; transaction will roll back.';
END $reply_check$;
ROLLBACK;
