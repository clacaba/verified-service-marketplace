# Milestone A API contract draft

Status: Authentication, `/users/me`, and organization/membership routes are implemented. Profile and provider-verification routes remain design contracts. The matching machine-readable contract is `milestone-a.openapi.json`.

Base path: `/api/v1`. JSON uses camelCase; UUID identifiers and ISO-8601 UTC timestamps. Document bytes use multipart uploads, never JSON/base64.

## Shared conventions

- Browser authentication: proposed HttpOnly secure session cookie; mutations require a CSRF token validated by the server. Only register, login, email verification, and public profile discovery are anonymous.
- Check active membership and operation permissions for each organization route. Administrative routes require platform reviewer permissions. Return 404 for resources the caller must not be able to discover.
- Errors use `application/problem+json`: `type`, `title`, `status`, `detail`, `instance`, `requestId`, and optional field-keyed `errors`. Exclude stack traces and private data.
- Lists return `{ items, page, pageSize, totalCount }`. Default page size 20, maximum 100; invalid pagination returns 400. Review queue sorts by submitted time then ID for stable ordering.
- Mutable resources return ETags. Updates, submissions, and decisions require `If-Match`; missing precondition returns 428, stale versions return 412. Invalid business transitions return 409.
- Login, email sending, and uploads use configured limits; 429 includes `Retry-After`. Exact quotas are set before implementation.
- Breaking changes require a new major path version. Additive fields must not change existing meanings.

## Endpoints

| Method and path | Permission | Request / result |
| --- | --- | --- |
| POST `/auth/register` | Anonymous, rate limited | `{ name, email, password, accountType }`; Customer/Provider only; 202 generic confirmation; never accept administrative roles. |
| POST `/auth/verify-email` | Anonymous, rate limited | `{ userId, token }`; consume expiring token; 204. |
| POST `/auth/resend-verification` | Anonymous, rate limited | `{ email }`; 202 generic response regardless of account existence. |
| POST `/auth/login` | Anonymous, rate limited | `{ email, password }`; session cookie and safe user summary; 401 generic invalid credentials. |
| GET `/auth/csrf` | Browser session/bootstrap | CSRF token bound to the browser session; never a credential in a URL. |
| POST `/auth/logout` | Session + CSRF | Revoke session; 204. |
| GET `/users/me` | Session | Own user summary, capabilities, email status, active memberships. |
| POST `/organizations` | Provider session | `{ name, providerType }`; create provider organization and owner membership; 201. |
| GET `/organizations/{organizationId}` | Active member | Organization summary and caller permissions. |
| GET `/organizations/{organizationId}/members` | Owner | Paginated member roster; `page` and `pageSize`; includes inactive memberships. |
| POST `/organizations/{organizationId}/invitations` | Owner | `{ email, role }`; controlled staff roles; 201 without token disclosure to unauthorized callers. |
| POST `/invitations/accept` | Verified-email intended recipient session | `{ token }`; single-use invitation; 204. |
| DELETE `/organizations/{organizationId}/members/{userId}` | Owner | Deactivate member; reject removing any owner; ownership transfer is deferred; 204. |
| GET `/organizations/{organizationId}/provider-profile` | Profile read permission | Private editable profile and ETag. |
| PUT `/organizations/{organizationId}/provider-profile` | Profile write permission | Profile fields below; If-Match; 200 and new ETag. |
| GET `/categories` | Anonymous | Active categories, paginated. |
| POST `/organizations/{organizationId}/verifications` | Verification write permission | Create draft for provider; 201 with draft ID and ETag. |
| GET `/organizations/{organizationId}/verifications` | Verification read permission | Own application history, paginated. |
| GET `/organizations/{organizationId}/verifications/{applicationId}` | Verification read permission | Private application summary and ETag. |
| POST `/organizations/{organizationId}/verifications/{applicationId}/documents` | Verification write permission | Multipart `file`, `evidenceType`; editable draft only, If-Match; 201 metadata/new ETag. |
| DELETE `/organizations/{organizationId}/verifications/{applicationId}/documents/{documentId}` | Verification write permission | Editable draft only, If-Match; 204/new ETag. |
| POST `/organizations/{organizationId}/verifications/{applicationId}/submit` | Verified email + verification write | If-Match; validate complete evidence; 200 immutable submitted version. |
| POST `/organizations/{organizationId}/verifications/{applicationId}/revisions` | Verification write permission | If-Match; rejected/information-request application only; create linked editable draft; 201. |
| GET `/admin/verifications` | Reviewer | `status`, `page`, `pageSize`; review queue. |
| GET `/admin/verifications/{applicationId}` | Reviewer | Private review details and ETag. |
| POST `/admin/verifications/{applicationId}/start-review` | Reviewer | If-Match; Submitted → UnderReview; 200. |
| POST `/admin/verifications/{applicationId}/decisions` | Reviewer | If-Match; decision/checks/reason; 200 updated application. |
| POST `/verification-documents/{documentId}/access` | Authorized provider member or reviewer | Short-lived private retrieval URL with expiry; audit access; no-store. |
| GET `/providers/{providerId}` | Anonymous | Approved public projection and current badges; otherwise 404. |
| GET `/notifications` | Session | Own notifications, paginated. |
| PATCH `/notifications/{notificationId}` | Recipient session | `{ read: true }`; 204. |

All authentication POSTs, including anonymous registration, login, verification, and resend, require `X-CSRF-Token` from `/auth/csrf`. Fetch a fresh token after login/logout changes the identity. Authenticated business mutations also require CSRF protection for browser sessions. The Next.js BFF additionally checks the configured request origin. Upload access checks must validate the document/application/provider relationship, not only a document ID.

## Resource shapes and validation

`ProviderProfile`: `id`, `organizationId`, `displayName`, `providerType` (Individual/Business), `biography`, `categoryIds`, `serviceArea`, optional `imageId`, `version`. Proposed limits: display name 1–150 characters, biography up to 4,000, service area 1–250, at least one active category. Trusted image references must belong to the organization.

`VerificationApplication`: `id`, `providerId`, `organizationId`, `status`, `submissionVersion`, nullable `submittedAt`, `documents`, `decisions`, `version`. Documents expose only private authorized metadata: ID, evidence type, safe filename, content type, size, uploaded time. Object keys never leave storage services.

`DecisionRequest`: `decision` (Approved/Rejected/AdditionalInformationRequired), `reason`, `checks` containing `type` and optional `expiresAt`. Approval requires all configured evidence/checks. Rejection and information requests require a reason of 1–2,000 characters. Demo simulation flags are server-controlled.

`PublicProvider`: `id`, `displayName`, `biography`, public image, categories, service area, badges. Each badge exposes `type`, `simulated`, optional `expiresAt`, and explanatory text. Exclude membership, private applications, documents, emails, and sensitive identifiers.

Proposed document policy: PDF/JPEG/PNG, 10 MiB per file; validate actual content, reject unsafe files, generate private storage keys, and use a configured retention policy. Final storage access duration and required evidence configuration must be set before upload implementation.

## Atomic state changes

Decisions commit status, selected checks, reviewer history, audit event, notification, and outbox event together. Revisions preserve earlier evidence/decisions. Duplicate/stale submissions cannot create another active review application. Expiry or changes to verified details invalidate public badge eligibility.

Partner OAuth, scopes, booking idempotency, and signed webhook schemas belong to a separate Milestone C OpenAPI contract; they must not use human cookie sessions.
