# Provider profile drafts

## Delivered behavior

An organization owns one private provider profile. New organization creation saves the profile and owner membership atomically. The migration also creates initial drafts for existing organizations, using the organization name/type. Nothing is published or marked verified.

Owners edit display name, Individual/Business type, biography, service area, and service categories. Staff can read but cannot edit. Database membership checks apply to every request; outsiders and deactivated members receive 404.

Drafts require a display name and valid provider type. Biography, area, and category selection may remain incomplete. Basic readiness requires all three; evidence submission will enforce its own complete requirements later.

## Local walkthrough

1. Start the stack following [local development](local-development.md).
2. Sign in as a Provider and create or select an organization at /organizations.
3. Open Provider profile draft, complete fields, and Save draft.
4. Reload or revisit the page to confirm persistence.
5. Open the draft in two tabs. Save in the first; saving the older second tab must fail without overwriting. Reload explicitly to discard those stale edits.
6. Sign in as invited Staff: the draft is read-only. Deactivate that membership and confirm access is removed.

## API and concurrency

GET and PUT /api/v1/organizations/{organizationId}/provider-profile require a session.
GET returns a quoted UUID ETag. PUT requires CSRF plus the exact If-Match header: missing 428, stale/wildcard 412. EF concurrency checks also reject races during saving. Updates, category assignments, organization provider type, and audit event save atomically.

GET /api/v1/categories is anonymous, paginated (20 default, 100 maximum), and returns only active categories. Initial reference categories are Cleaning, Plumbing, Electrical, and Appliance repair.

The browser proxy allowlists these routes and forwards If-Match/ETag. Private responses use no-store. The editor preserves unsaved fields when saving fails, offers explicit reload on conflict, and warns before discarding unsaved changes.

## Deferred work and validation limits

Non-null imageId is rejected; arbitrary external image identifiers are not trusted. Image upload will follow private storage authorization work. Verification applications/checks do not exist yet, so check invalidation and publishing are not implemented and must be added before verified profiles can change.

Integration tests use SQLite and cover drafts, persistence, required/invalid fields, active categories, CSRF, authentication, isolation, Staff permissions, revocation, and version conflicts. Frontend type checking/build are checked. Docker/PostgreSQL migration and real-browser email/onboarding journeys remain pending.
