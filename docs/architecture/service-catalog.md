# Service listings and catalog management

Status: Core CATALOG-01 text/pricing lifecycle and initial SEARCH-01 discovery implemented. Images, advanced search, and real Docker/PostgreSQL/browser validation remain pending.

## Product rules for this slice

- Listing: a provider's fixed-price offer. It belongs to an organization and one active category already selected on that provider profile.
- Owner: creates, edits, publishes/unpublishes, and archives offers. Staff: read-only catalog access. Permissions are catalog:read and catalog:write; the backend checks current active membership on every request.
- Required fields: title 1–150, description 1–4,000, service area 1–250 trimmed characters, active provider category, integer priceMinor 1–100,000,000, currency PHP, durationMinutes 15–1,440 in multiples of 15.
- PriceMinor means centavos: 25,000 = PHP 250.00. Store integer minor units, never floating-point money. Multi-currency, tax/fee calculation, payments, and negotiated prices are deferred.
- Draft -> Published -> Draft is reversible through publication controls. Any successful edit returns the offer to Draft, even if it was public. Archived is terminal; create a new listing instead of restoring it. No hard-delete or booking record exists yet.
- Provider approval alone is insufficient: its profile must be explicitly published and currently eligible before publishing an offer.
- Public requests recheck eligibility, listing status, active category, and category membership. Expiry/revocation/unpublished profiles hide offers without a worker. Stored Published status is not a guarantee of current visibility.
- Unpublishing/archiving remains available when verification expires. A later valid renewal may restore public visibility for unchanged Published offers. Booking must independently validate eligibility and snapshot price/details in its future milestone.
- Images are NOT accepted. Unknown JSON properties, including external image URLs, are rejected. Public listing image upload/processing is a follow-up with a separate privacy path; private identity evidence must never become a public listing image.

## Try locally

1. Apply migrations (Development Compose startup, or dotnet ef database update --project apps/api on the configured host).
2. Register/verify a Provider, create an organization, and include a service category in its profile.
3. Open /organizations -> Service catalog, or /catalog?organizationId={id}. Create and save a listing.
4. Complete provider verification through a separate Reviewer, then explicitly publish its provider profile.
5. Open the saved listing and publish it. Browse /services anonymously; open /services/{serviceId}.
6. Edit the offer: it becomes Draft and disappears publicly until republished. Unpublish to keep it editable, or archive it permanently.
7. A Staff account can inspect offers but cannot save, publish, or archive. A deactivated member loses access immediately.

## API

Private routes use /api/v1/organizations/{organizationId}/services:

| Method | Suffix | Behavior |
| --- | --- | --- |
| GET | empty | Own paginated catalog; optional status Draft/Published/Archived |
| POST | empty | Create private Draft; returns 201, Location, ETag |
| GET | /{serviceId} | Private detail plus ETag |
| PUT | /{serviceId} | Exact If-Match, update and reset to Draft |
| POST | /{serviceId}/publication | Exact If-Match; JSON published true/false |
| POST | /{serviceId}/archive | Exact If-Match; terminal archive |

All mutations require X-CSRF-Token and Owner membership. Missing If-Match returns 428; stale/wildcard returns 412; unsupported lifecycle/prerequisites return 409; Staff mutations 403; outsiders/mismatched organizations 404. Field validation returns 400. Never blindly retry create requests; partner idempotency remains planned.

Public GET /api/v1/services supports q (literal case-insensitive substring title/description, max 150), area (substring, max 250), categoryId, providerId, minPriceMinor/maxPriceMinor, sort=title|priceAsc|priceDesc, page, pageSize. Sort ties use ID. Default page 1/size 20, max page 100000/size 100. Filters combine with AND. All offers already require current verification; a configurable badge-specific verification filter is deferred. Price filters are centavos and PHP only; negatives/inverted bounds/unsupported sorts are 400.

Public GET /api/v1/services/{serviceId} returns indistinguishable 404 for nonexistent/private/archived/ineligible offers. Public fields: id, title, description, priceMinor, currency, durationMinutes, serviceArea, category(id/name), provider(id/displayName), disclosure. No internal organization ID, memberships, audit records, versions, evidence, reviewer details, or customer data. Public provider links contain precise simulated badge explanations.

## Persistence and integrity

ServiceListings includes UUID organization/category foreign keys, Version concurrency token, bounded fields, UTC create/update timestamps, status, and integer price/duration. Database checks enforce price, PHP currency, duration, and known statuses. Organization/status and status/category/price indexes support initial discovery. Listing changes and organization audit events save atomically. ServiceCatalog migration and infrastructure/service-catalog.sql are included.

Search uses database filtering/paging, not loading all offers into the browser. Responses and Next fetches use no-store. A second eligibility/version check removes offers changed during projection. Separate reads are not a serializable snapshot; under concurrent changes counts may be briefly stale. Already-loaded pages must refresh. Production scale requires search/index/query-plan review and rate-limit tuning.

## Verification and remaining work

All 63 backend tests pass, including create/read/update/publish/unpublish/archive, current provider requirements, membership deactivation/organization isolation, CSRF, exact ETags, validation, literal filters, sorting/paging, and public privacy projection. Frontend typecheck/build and proxy smoke tests pass for the new routes/pages against a mock backend. PostgreSQL migration/query execution and interactive browser acceptance remain pending: Docker CLI is installed, but the engine is not running and the repository has no local .env file yet.

Next: separately authorized public service image upload/processing, then customer properties and provider availability before booking. Images require MIME/size/dimension limits, safe decoding/re-encoding and metadata stripping, malware checks, orphan/deletion cleanup, and public authorization independent of private verification evidence.
