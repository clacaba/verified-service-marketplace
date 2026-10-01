# MVP requirements and acceptance criteria

Status: Initial planning baseline. No feature is implemented by this document.

Source: [Master plan](../service-booking-marketplace-plan.md), especially sections 25 and 28–32. Scope defaults are identified in the [product brief](product-brief.md).

## Ownership and access

- A user may have customer and provider capabilities and multiple organization memberships.
- Each provider profile belongs to one organization. An independent provider also has an organization.
- Active membership and permission checks govern organizational access; a supplied `organization_id` alone grants nothing.
- Customers own their properties. `property_id` describes a physical location and never substitutes for organizational isolation.
- Bookings authorize explicit participants: customer, permitted provider staff, and originating partner where applicable.
- Administrators are provisioned through a controlled process; public registration cannot grant administrative roles.
- Partner-created customers stay isolated to their originating organization; email matching cannot attach them to a human account automatically.

## Milestone A: Provider onboarding and verification

### AUTH-01 — Register and verify email

As a customer or provider, I want an account so I can use protected marketplace features.

- Validate names, normalized emails, and the configured password policy on the backend.
- Use ASP.NET Core Identity password hashing and prevent duplicate normalized emails.
- Email verification tokens expire and are single-use.
- Allow onboarding drafts before email verification; require verified email before submitting evidence or booking.
- Reject role escalation through registration input.

### AUTH-02 — Manage sessions

As a user, I want secure sign-in and sign-out so my records remain private.

- Invalid credentials produce a consistent error; login attempts are rate limited.
- Expired or revoked sessions cannot authorize requests; logout revokes the relevant credential.
- Proposed browser approach: HttpOnly cookies with secure transport and CSRF protection for mutations.
- Test unauthorized roles, inactive membership, and another user's private records.

### ORG-01 — Manage provider membership

As a provider owner, I want an organization so staff permissions are explicit.

- Onboarding creates an organization and owner membership atomically.
- Owners invite staff through an expiring invitation accepted by its intended recipient.
- Owners and staff have distinct permissions; deactivated members lose access immediately.
- Cross-organization read and write attempts are rejected.

### PROVIDER-01 — Maintain a profile

As a provider, I want to describe my services so customers can evaluate me.

- Capture display name, individual/business type, biography, categories, service area, and optional image.
- Validate required fields in the API; authorized members can save and revisit drafts.
- Changes to verified identity or business fields invalidate affected checks and require review.

### VERIFY-01 — Submit private evidence

As a provider, I want to submit documents so my eligibility can be reviewed.

- Required evidence depends on provider type and configured category requirements.
- Enforce file size, allowed types, and content validation; use only fictional evidence in demonstrations.
- Store documents privately; authorized access expires and is audited.
- Record immutable submission versions, document references, and submission timestamps.
- Drafts and responses to information requests are editable; submitted/under-review versions are locked.
- Public responses exclude evidence, object keys, and sensitive identifiers.

### VERIFY-02 — Review an application

As an administrator, I want to review evidence so eligible providers can be approved.

- Queue submitted and under-review applications with bounded pagination.
- Support approval, rejection, and additional-information requests.
- Require a provider-facing reason for rejection and information requests.
- Record reviewer, time, previous/new status, and audit event for every decision.
- Use optimistic concurrency; stale decisions fail instead of overwriting another reviewer.
- Record notifications transactionally with decisions; permit corrected resubmissions after rejection or information requests.

### VERIFY-03 — Show precise badges

As a customer, I want to understand exactly what has been checked.

- Display specific approved checks such as identity and business verification with expiry where applicable.
- Explain simulated checks clearly; do not claim a real background check occurred.
- Expired, suspended, or revoked checks cannot appear as current.
- Public profile responses contain no private evidence.
- Publishing and accepting new work require all configured current checks; existing bookings remain available to authorized participants.

## Milestone B: Catalog and booking

| Story | User need | Acceptance criteria |
| --- | --- | --- |
| CATALOG-01 | Approved providers publish services. | Authorized members edit; validate category, title, description, fixed price/currency, duration, area, images; publishing requires current approval; unpublished services cannot be booked. |
| SEARCH-01 | Customers discover suitable services. | Server-side text/category/area/price/verification filters, documented sorting, bounded pagination; only eligible listings; loading, empty, and error states. |
| PROPERTY-01 | Customers save service locations. | Only owners manage properties; validate addresses; booking snapshots survive property changes; disclose full addresses only to authorized participants. |
| AVAILABILITY-01 | Providers define working time. | Weekly hours, exceptions, breaks, buffers, minimum notice, and time zone; UTC timestamps; valid DST behavior; one schedulable capacity per provider initially. |
| BOOKING-01 | Customers request a service slot. | Verified customer, eligible service, owned location, valid slot; server-calculated price and immutable snapshots; pending requests reserve capacity; database concurrency prevents overlaps; configurable pending expiry releases capacity. |
| BOOKING-02 | Participants track work. | Provider staff confirm/decline/start/complete; reject invalid transitions and stale updates; audit actor/time/status/reason; proposed cancellation before work starts without fees. |
| NOTIFY-01 | Users receive status updates. | Private in-app notifications; transactional event recording; email jobs with retries and delivery tracking; users mark only their own notifications read. |
| REVIEW-01 | Customers review completed work. | One review per completed booking by its customer; integer rating 1–5; configured text limit; administrator moderation requires reason; providers cannot delete reviews; derive aggregates from published reviews. |

Proposed booking lifecycle: `PendingProviderConfirmation → Confirmed → InProgress → Completed`. Pending requests may become `Declined`, `Expired`, `CancelledByCustomer`, or `CancelledByProvider`. Confirmed bookings may be cancelled before work begins. Payments and disputes are deferred.

## Milestone C: Partner API

| Story | User need | Acceptance criteria |
| --- | --- | --- |
| PARTNER-01 | Approved companies authenticate applications. | Administrator registers clients; short-lived OAuth client-credentials tokens bind audience/client/organization/scopes; secrets are securely hashed or identity-provider managed; revocation and rotation work. |
| PARTNER-02 | Partners discover and book services. | Versioned `/api/v1` contracts; organization-isolated customer external IDs; explicit originating partner on bookings; enforce scopes and participation; identical idempotent retries return prior result, conflicting key reuse fails; rate limits return 429 with retry guidance. |
| PARTNER-03 | Partners receive booking events. | Own-organization HTTPS endpoints; block internal/private destinations including DNS/redirect targets; signed timestamp/payload; stable event IDs; backoff, delivery logs, authorized replay; transactional outbox; document consumer deduplication. |

## Completion criteria

Every implemented story requires appropriate migrations, backend validation and authorization, documented contracts, accessible frontend states, tests for permissions/business rules/failures, and logs excluding credentials or private documents. Use fictional seeded data. Complete each milestone end to end before expanding the next.

The first implementation is foundation, authentication, organizations, provider profiles, evidence submission, review, and badges. Prepare the [domain diagram](../diagrams/milestone-a-domain.md), [wireframes](../architecture/milestone-a-wireframes.md), and [API draft](../api/milestone-a-contract.md) before implementing migrations or endpoints.
