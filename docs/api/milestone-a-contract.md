# Milestone A API contract draft

Status: Authentication, `/users/me`, and organization/membership routes are implemented. Provider profile draft and category routes are also implemented. Verification draft/history, private documents, access/download, and submission routes are implemented. Administrator review, decisions/checks, notifications, corrected drafts, and invalidated-approval renewal are implemented. Public badges/profiles remain design contracts. The matching machine-readable contract is `milestone-a.openapi.json`.

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
| POST `/organizations/{organizationId}/verifications/{applicationId}/documents` | Verification write permission | Multipart `file`, `evidenceType`, `demoAcknowledged=true`; editable draft only, If-Match; 201 metadata/new ETag. |
| DELETE `/organizations/{organizationId}/verifications/{applicationId}/documents/{documentId}` | Verification write permission | Editable draft only, If-Match; 204/new ETag. |
| POST `/organizations/{organizationId}/verifications/{applicationId}/submit` | Verified email + verification write | If-Match; validate complete evidence; 200 immutable submitted version. |
| POST `/organizations/{organizationId}/verifications/{applicationId}/revisions` | Verification write permission | If-Match; latest rejected/information-request application, or approval with missing current checks; create linked editable draft; 201. |
| GET `/admin/verifications` | Reviewer | `status`, `page`, `pageSize`; review queue. |
| GET `/admin/verifications/{applicationId}` | Reviewer | Private review details and ETag. |
| POST `/admin/verifications/{applicationId}/start-review` | Reviewer | If-Match; Submitted → UnderReview with reviewer assignment and 30-minute lease; expired/revoked claims may be reclaimed; 200. |
| POST `/admin/verifications/{applicationId}/decisions` | Reviewer | Current assigned Reviewer lease + If-Match; decision/checks/reason; atomic audit, checks, notifications, queued email; 200. |
| POST `/verification-documents/{documentId}/access` | Authorized provider member or reviewer | Session-bound relative retrieval URL plus token for X-Evidence-Access; expiry; audit access; no-store. |
| GET `/verification-documents/{documentId}/content` | Original session + X-Evidence-Access grant + current membership | Private attachment download; expired/invalid grants or unauthorized documents return 404. |
| GET `/providers/{providerId}` | Anonymous | Approved public projection and current badges; otherwise 404. |
| GET `/notifications` | Session | Own notifications, paginated. |
| PATCH `/notifications/{notificationId}` | Recipient session | `{ read: true }`; 204. |

All authentication POSTs, including anonymous registration, login, verification, and resend, require `X-CSRF-Token` from `/auth/csrf`. Fetch a fresh token after login/logout changes the identity. Authenticated business mutations also require CSRF protection for browser sessions. The Next.js BFF additionally checks the configured request origin. Upload access checks must validate the document/application/provider relationship, not only a document ID.

## Resource shapes and validation

`ProviderProfile`: `id`, `organizationId`, `displayName`, `providerType` (Individual/Business), `biography`, `categoryIds`, `serviceArea`, optional `imageId`, `version`. Draft limits: display name 1–150 characters, biography up to 4,000, service area up to 250, up to 20 distinct active categories. Drafts may omit biography, area, and categories. `version` is a UUID and is returned as a quoted strong ETag; GET and successful PUT return ETag. PUT requires the exact latest If-Match (428 if missing, 412 if stale). `status` is always Draft; `readyForVerification` means basic fields are complete, not approved or published. Non-null imageId is rejected until authorized image uploads exist. Profile responses and browser proxy responses are no-store.

`VerificationApplication`: `id`, `providerId`, `organizationId`, `status`, `submissionVersion` (0 draft/1 submitted), nullable `submittedAt`, `documents`, `decisions`, UUID `version`, `requiredEvidence`, nullable `submittedSnapshot`, and `simulated=true`. Documents expose only private authorized metadata: ID, evidence type, safe filename, content type, size, uploaded time. Object keys never leave storage services.

`DecisionRequest`: `decision` (Approved/Rejected/AdditionalInformationRequired), `reason`, `checks` containing `type` and optional `expiresAt`. Approval requires all configured evidence/checks. Rejection and information requests require a reason of 1–2,000 characters. Demo simulation flags are server-controlled.

`PublicProvider`: `id`, `displayName`, `biography`, public image, categories, service area, badges. Each badge exposes `type`, `simulated`, optional `expiresAt`, and explanatory text. Exclude membership, private applications, documents, emails, and sensitive identifiers.

Implemented demo document policy: PDF/JPEG/PNG, 10 MiB per file, one file per request, at most 20 per application. Validate MIME against signatures/end markers and require a clean ClamAV scan before private storage. Format detection is not full structural parsing. Metadata uses `fileName`, `size`, `expiresAt`, and `scanStatus=Clean`; it never exposes object keys. Default draft/submitted file retention is 30/90 days; access grants expire in 60 seconds and are bound to the authenticated session. Required types depend on Individual/Business and Plumbing/Electrical categories; see [private verification evidence](../architecture/private-verification-evidence.md). Rules are fictional demo defaults. A submitted snapshot is locked; draft evidence may be removed. Corrected revisions now create fresh linked drafts and preserve earlier evidence/snapshots/decisions.

## Atomic state changes

Decisions commit status, selected checks, reviewer history, audit event, notification, and outbox event together. Revisions preserve earlier evidence/decisions. Duplicate/stale submissions cannot create another active review application. Expiry or changes to verified details invalidate public badge eligibility.

Partner OAuth, scopes, booking idempotency, and signed webhook schemas belong to a separate Milestone C OpenAPI contract; they must not use human cookie sessions.


## Implemented review details

Reviewer access is explicitly operator-provisioned for existing verified accounts, never accepted through registration. All privileged requests check it in the database; conflicts of interest exclude current/historical organization members. Review claims last 30 minutes, require exact versions, and can be reclaimed only after expiry or reviewer revocation. The assigned reviewer alone may decide.

Decision history returns id, reviewerId, decision, previousStatus=UnderReview, newStatus, reason, decidedAt, and checks (type/expiry/revocation/simulated). Approval must match the immutable submitted profile, active categories, current evidence, and every required check. Checks are simulated and cannot outlive retained evidence. Material profile edits revoke checks; an Owner may request renewal when any required check is no longer current.

Applications expose parentApplicationId, revisionNumber, reviewStartedBy, reviewStartedAt, reviewLeaseExpiresAt, and canCreateRevision. Correction creates a new empty linked Draft, requires fresh uploads, and increments numbered submissions. A prior submitted snapshot is never overwritten.

Notifications return id, applicationId, organizationId, message, createdAt, and read. Recipient ownership AND active source membership govern list/mark-read. Notification PATCH is an idempotent mark-read operation without ETag. Private owner email is recorded transactionally in the existing encrypted retry queue; it contains only a generic update link.

See [administrator review and revisions](../architecture/administrator-review-and-revisions.md) for provisioning commands, walkthrough, validation, and production limitations.
