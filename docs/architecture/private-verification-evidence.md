# Private verification evidence and submissions

Status: Demo upload/submission slice implemented. Administrator review, decisions, revision drafts, and notifications are now implemented; [review workflow](administrator-review-and-revisions.md). Public profiles and precise simulated badges are implemented; see [publication workflow](public-provider-profiles.md).

## Demo evidence policy

Only fictional files may be used. Every upload requires demoAcknowledged=true. This is a portfolio exercise, not identity verification, legal advice, or a certification system.

- Individual: Identity.
- Business: Identity and BusinessRegistration.
- Plumbing/Electrical categories additionally require TradeQualification.
- These rules are configurable under Evidence in apps/api/appsettings.json and environment variables, and are not jurisdiction-specific requirements.
- Required evidence is recomputed against the current profile when submitting, then frozen with the profile snapshot.

## Workflow and permissions

1. Register a Provider, verify its email, create an organization, and complete its profile (biography, area, and active categories).
2. Open /organizations, then Verification evidence.
3. Create one private Draft. Only one active application per organization is allowed by a database constraint.
4. Upload fictional PDF/JPEG/PNG files for the displayed requirements. Download privately or remove draft evidence.
5. Submit: verified email, complete profile, and current required documents are mandatory.
6. Status becomes Submitted; its profile, evidence hashes/references, and requirements snapshot is immutable. Submission does not approve or publish anything.
7. A separately provisioned reviewer records a decision; Owners can create a fresh linked correction draft when requested.

Owners create/upload/remove/submit. Active Staff can read and download, not mutate. Cross-organization IDs return 404. All mutations require CSRF. Application changes require exact quoted UUID If-Match: missing 428, stale 412, locked submission 409. Simultaneous profile changes and submission are detected through the profile concurrency token.

## Upload and retrieval safety

Uploads are multipart file/evidenceType/demoAcknowledged, one file per request, 10 MiB maximum, 20 documents per application. The request limit is 11 MiB including multipart overhead. MIME must match PDF/JPEG/PNG signature and end-marker checks; filename extensions are not trusted. This is bounded format detection, not full PDF/image parsing or a guarantee that a file is harmless.

ClamAV scans bytes before storage. Malware rejection returns 400; unavailable or ambiguous scanner/storage results return 503. There is no scanner bypass in the application. Keep signatures updated. Stronger structural parsing, content disarm, and production security review remain hardening work.

S3-compatible MinIO stores bytes using random internal keys, never browser-provided names. The dedicated bucket is created only in Development and must have no bucket policy; access fails closed if a policy is present. Do not expose it anonymously or change its ACLs. Local root storage credentials are development-only; production requires scoped service credentials, a pre-provisioned private bucket with enforced ownership/public-access blocking, transport and at-rest encryption, secure backup/retention policy, and reviewed configuration. Production API storage endpoints require HTTPS.

POST /api/v1/verification-documents/{id}/access returns a relative content URL, a short-lived token, and expiry. Tokens travel in X-Evidence-Access, never URL query strings. GET content requires the original authenticated account AND session, checks current membership or eligible platform Reviewer access and document expiry again, verifies SHA-256 integrity, and audits download. Tokens default to 60 seconds (configurable 10–300). Downloads are attachments with application/octet-stream, nosniff, sandbox, no-referrer, and no-store. They are not public S3 links and must not be logged or persisted by clients.

## Persistence and retention

Each mutation records its organization audit event transactionally. Submission freezes snapshot JSON and evidence SHA-256 hashes alongside status/version/timestamp. Later profile edits do not alter an existing submitted snapshot; material profile changes now revoke approved checks. Public badge projection remains pending.

Fictional draft files expire after 30 days; submission resets retained submitted files to 90 days. This is a demo default, not a production retention commitment. Expiry immediately denies downloads even if the cleanup worker is offline. Removal/expiry soft-deletes metadata and queues private object deletion. The worker runs every minute, retries failures with bounded backoff, and does not log filenames, tokens, or keys. Application/snapshot metadata remains for history.

An upload writes a cleanup reservation to PostgreSQL before writing the object. Success consumes that reservation together with document metadata and the parent version update. Failed or interrupted uploads remain eligible for cleanup after one hour. Enable Evidence__RetentionEnabled=true (already enabled in Compose). Use one worker instance for this initial implementation; distributed leases, retention policy approval, monitoring, and backup deletion require production work. Deleted file bytes cannot be recovered from the application.

## Local setup

Docker Compose now includes ClamAV and persists its signature database. Its first startup/signature download can take time; retry uploads only after the scanner is ready. API startup does not require a ready scanner; upload failures are explicit and fail closed.

Run docker compose up --build. Mailpit: http://localhost:8025. MinIO console: http://localhost:9001. Do not change bucket anonymous access. The API creates verification-evidence on first successful storage operation in Development. On host runs, set Storage__AccessKey/Storage__SecretKey using your ignored .env values, enable the retention worker, and start the scanner dependency.

## Validation status

Automated backend integration tests use SQLite plus injected storage/scanner doubles. They verify required evidence, email/profile completeness, locked snapshots, conflicting versions, MIME/content rejection, size/CSRF checks, scanner failures, Staff permissions, cross-organization denial, expiring/session-bound download grants, immediate revocation, retention cleanup, and cleanup retries. Frontend typecheck and production build pass.

The original evidence slice's 34 backend tests pass alongside the added review tests, including a real loopback TCP fixture for scanner framing and an upload/save race that verifies rollback and orphan cleanup. The HTTP proxy smoke test also checks multipart byte preservation, download bytes, ETag/no-store forwarding, origin checks, and the route allowlist using a mock API. Run npm run build, then node scripts/smoke-verification-proxy.mjs.

The PostgreSQL migration and reviewable SQL are included. Actual PostgreSQL/MinIO/ClamAV containers and a real-browser multipart/download flow remain unverified because Docker is unavailable here.

Protocol references: [ClamAV INSTREAM](https://docs.clamav.net/manual/Usage/ClamdProtocol.html), [AWS SDK S3 PutObject](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/MS3PutObjectAsyncPutObjectRequestCancellationToken.html).
