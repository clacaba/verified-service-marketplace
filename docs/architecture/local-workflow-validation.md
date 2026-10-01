# Local workflow validation — 2026-10-01

Validated against the running Docker Compose stack through the Next.js API proxy,
using real PostgreSQL, Mailpit, MinIO, and ClamAV with fictional test data.
This was HTTP integration testing, not interactive browser acceptance testing.

## Results

- Backend suite: 103 passed, zero failed or skipped (including 15 private-bucket
  policy regression cases).
- Provider, customer, and reviewer registration; email delivery/confirmation;
  cookie authentication and refreshed CSRF tokens passed.
- Organization ownership, provider draft editing, and cross-account isolation passed.
- Private evidence upload, real malware scanning, submission, reviewer claim,
  approval, publication, and precise simulated badges passed.
- Service publication, availability, customer property creation, booking request,
  duplicate-slot rejection, provider confirmation, confirmed-booking readback,
  participant isolation, and in-app notifications passed.
- Authentication rate limiting was respected during repeated test runs.
- Temporary reviewer access was revoked through the audited operator command.

The AWS SDK v4 returned MinIO's HTTP 404 `NoSuchBucketPolicy` XML in the policy
response rather than throwing an exception. The fixed storage guard accepts only
that exact error at HTTP 404 (or a successful empty policy). Nonempty policies,
other failures, malformed XML, duplicate error codes, and DTDs remain rejected.

Final test identifiers:

- Provider: `e9036c17-4a23-400f-9d1f-e5d94784188e`
- Service: `dd9c52b0-491d-4d6d-9e39-55fc7f4f8737`
- Booking: `f2337097-d061-4eda-ad17-8b9b0298c1ba` (`Confirmed`)

Fictional accounts, evidence, listings, properties, bookings, and Mailpit messages
from the test runs remain in the local development volumes. No existing data was
deleted. Tests did not collect payments or send emails outside Mailpit.

## Not covered by this run

Interactive browser behavior, service-image uploads, corrected resubmission,
concurrent racing bookings, expiry, and start/completion of a future booking were
not exercised by this HTTP workflow. Those remain separate acceptance checks;
some have automated backend test coverage. This does not certify production
security or replace the maintained-production-storage decision.
