# Implementation checklist

Source: [Master plan](../service-booking-marketplace-plan.md), sections 29–32.

## Planning files

- [x] Working product brief and initial categories.
- [x] MVP user stories and ownership baseline.
- [x] Initial architecture record with open runtime/hosting decisions.
- [x] Milestone A conceptual domain diagram.
- [x] Milestone A low-fidelity journeys and wireframes.
- [x] Milestone A API design draft.
- [ ] Finalize runtime/package versions and local infrastructure configuration.
- [x] Create machine-readable Milestone A OpenAPI specification; JSON and internal references checked.
- [ ] Validate the specification with a dedicated OpenAPI validator.
- [ ] Finalize category evidence requirements, document retention, and upload limits.

## Step 1: Foundation

- [x] Scaffold backend solution, frontend workspace, and API health-check tests.
- [x] Add Compose definitions, environment template, database health checks, and CI build/test checks.
- [ ] Verify Compose startup with Docker and a real PostgreSQL readiness check.
- [ ] Configure local PostgreSQL/private object storage, environment templates, and health checks.
- [ ] Configure consistent errors, validation, logging, formatting, and CI.
- [ ] Produce initial migrations with ownership constraints.

## Step 2: First feature slice

- [x] Implement AUTH-01/02: Registration, queued verification email, revocable sessions, CSRF, and rate limiting; covered by backend integration tests.
- [ ] Verify the complete browser/PostgreSQL/Mailpit journey with Docker.
- [x] ORG-01: Organization ownership, invitations, and membership authorization; backend integration tests and frontend build pass.
- [ ] Verify organization invitation delivery and acceptance end-to-end with PostgreSQL/Mailpit.
- [x] PROVIDER-01 draft slice: Persistent private profiles, active categories, Owner editing/Staff read-only, version conflicts, audit, migration/backfill, and editor UI.
- [ ] PROVIDER-01 follow-up: Authorized image uploads.
- [x] Revoke current verification checks on material provider-profile changes; allow linked renewal drafts.
- [ ] Verify profile migrations and browser save/reload flow with Docker/PostgreSQL.
- [x] VERIFY-01 upload/submission slice: Private S3 storage, fail-closed malware scanning, demo evidence policy, Owner uploads, authorized download grants, immutable submissions, audit, and retention/deletion queue.
- [x] VERIFY-01 revisions: Fresh linked correction drafts, numbered resubmissions, preserved snapshots/documents/decisions, and expired/invalidated approval renewal.
- [ ] Verify PostgreSQL/MinIO/ClamAV and browser multipart/download behavior with Docker.
- [x] VERIFY-02: Operator-provisioned Reviewer access, review queue/details/claims, validated decisions/checks, optimistic concurrency, audit, recipient notifications, and transactional owner email queue.
- [ ] Verify the provider → reviewer → correction → resubmission flow with Docker and a real browser.
- [x] VERIFY-03: Explicit Owner publication, eligible public discovery/details, precise unexpired/unrevoked badges, privacy allowlist, and fictional demo disclosure; backend tests and frontend build pass.
- [ ] Verify publication migration and provider-publication/browser journey with Docker/PostgreSQL.
- [ ] Demonstrate the entire provider-registration-to-public-badge journey with fictional data.

## Step 3: Catalog and discovery

- [x] CATALOG-01 core: Owner-managed service drafts, fixed PHP centavo prices, duration/area/categories, publish/unpublish/archive, ETag/CSRF, atomic audit, and current provider eligibility.
- [x] SEARCH-01 initial slice: Public services/details, literal text/area search, category/provider/price filters, title/price sorting, and bounded pagination.
- [x] CATALOG-01 service images: Owner upload/removal, actual JPEG/PNG decode/re-encode, metadata stripping, malware checks, size/dimension/frame/count limits, separate private bucket, live public eligibility, and deletion/orphan cleanup.
- [ ] Verify service image migrations, MinIO/ClamAV, removal/archive cleanup, and interactive multipart/display flow with Docker/browser.
- [ ] SEARCH-01 follow-up: Badge-specific filters and production search/index tuning.
- [ ] Verify ServiceCatalog migration, PostgreSQL query behavior, and interactive catalog/public-search flows with Docker/browser.

## Later milestones

- [ ] Production image-processing isolation, dedicated least-privilege storage credentials, distributed cleanup leases, and image dependency/license review.
- [x] PROPERTY-01: Private owned addresses, validation, edit/archive, version conflicts, migration, and customer UI.
- [x] AVAILABILITY-01: Owner schedule, timezone/DST policy, weekly windows/exceptions/breaks/buffers/notice, one capacity, and public slots.
- [x] BOOKING-01/02: Verified customer request, owned property/current eligible service, immutable snapshots, database concurrency rollback, expiry, authorized versioned lifecycle/history, customer/provider UI.
- [x] Booking private notices and encrypted transactional email queue; existing SMTP retry worker reused.
- [ ] Verify property/booking migrations and genuinely simultaneous reservations using PostgreSQL; run full interactive provider/customer/staff/expiry/SMTP acceptance with Docker.
- [ ] Booking UX follow-ups: Visual calendar, geo coverage matching, human idempotency/rescheduling, and notification read tracking.
- [ ] Completed-booking reviews and booking notification read tracking.
- [ ] Administrator MFA and production cookie/CSRF/origin hardening verification.
- [ ] Scoped partner OAuth through an established identity provider, idempotent bookings, rate limits, and signed webhooks.
- [ ] Deployment, seeded demos, API documentation, screenshots, and demonstration video.

Checkboxes describe delivered work only. Planning documents do not imply implemented features.
