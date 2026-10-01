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
- [ ] PROVIDER-01 follow-ups: Authorized image uploads and invalidation of future verification checks on material profile changes.
- [ ] Verify profile migrations and browser save/reload flow with Docker/PostgreSQL.
- [ ] VERIFY-01: Private evidence upload, immutable submissions, and revisions.
- [ ] VERIFY-02: Administrative decisions, concurrency, audit, and reliable notifications.
- [ ] VERIFY-03: Precise current public badges and demo disclosure.
- [ ] Demonstrate the entire provider-registration-to-public-badge journey with fictional data.

## Later milestones

- [ ] Catalog, search, properties, availability, and conflict-safe booking lifecycle.
- [ ] Notifications and completed-booking reviews.
- [ ] Scoped partner OAuth, idempotent bookings, rate limits, and signed webhooks.
- [ ] Deployment, seeded demos, API documentation, screenshots, and demonstration video.

Checkboxes describe delivered work only. Planning documents do not imply implemented features.
