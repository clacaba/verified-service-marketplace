# Provider replies to customer reviews

## Scope and permissions

An active Owner or Staff member of the booking's provider organization, with verified email, may publish exactly one response to its Published customer review. The booking must still be Completed. A booking's customer cannot reply to their own review, even if subsequently invited as Staff. Wrong-organization/revoked/nonmember requests return 404, unverified/self-response requests return 403, and unavailable/duplicate response states return 409.

Provider responses are immutable in this release. There are no reply edit/delete endpoints, and replies cannot modify the customer rating, feedback, review visibility, or aggregate scores. Reply editing, reporting, moderation tools and notifications remain deferred.

## API

`POST /api/v1/bookings/{bookingId}/review/reply`

Body: `{ "comment": "Fictional provider response.", "publishAcknowledged": true }`.

Cookie session, CSRF token, and exact frontend-proxy origin are required. Feedback validation matches customer reviews: 10–2000 trimmed UTF-16 code units, line breaks/tabs allowed, other control characters rejected. Unknown fields (including rating, actorId, or organizationId) are rejected. Explicit public-publication acknowledgement is mandatory.

`GET /api/v1/bookings/{bookingId}/review` now includes `canReply` and `replyReason`, and the private/public review representations include a nullable `reply` object. Reply data contains only `comment`, `createdAt`, and `authorLabel: "Provider response"`; it never contains responder name, email, staff/account ID, booking ID, or organization ID. Public response text is user-authored; users are warned not to include personal information. Automatic personal-data filtering is not implemented.

Public replies are nested beneath their review and share its existing provider-publication/eligibility and Hidden-review rules. Private participant history remains accessible when a public profile becomes unavailable; revoked Staff lose access. Every successful reply persists its internal actor and an organization `review.replied` audit event atomically with the reply.

## Concurrency and storage

The additive `ProviderReviewReplies` migration creates a separate table with one reply per review enforced by its booking-key primary key, restricted review/account foreign keys, and a nonempty/maximum-feedback check. UTF-16 minimum length is validated by the API, not SQL's Unicode code-point count. A losing racing submission returns 409 without replacing the winner or leaving a losing audit entry. Creation is append-only and does not need If-Match.

Compose applies the migration only through the existing Development-only migration setting. Database volumes and existing reviews are preserved. Production deployments must review/apply `docs/api/provider-review-replies.sql` separately. No new credentials or environment variables are required.

## UI and acceptance

Open a Completed booking under the provider workspace. The review panel shows server-authoritative reply eligibility and, for eligible providers, a required response textarea, length feedback, public-consent checkbox, and submit button. Existing replies appear to booking participants and beneath public profile reviews, using the current marketplace design. Conflict/ambiguous failures require reload before retrying.

Automated coverage includes Owner/Staff authorization, verified email, revoked access, self-response rejection, missing/Hidden/non-completed review states, CSRF, body validation/overposting, Unicode responses, duplicate and racing submissions with atomic audit rollback, no edit/delete methods, private-field exclusion, unchanged rating aggregates, and public/private visibility. Frontend tests cover consent/required controls, eligibility/existing/conflict states, safe plain-text rendering and public reply placement. Proxy tests cover body/CSRF forwarding, allowed methods and invalid origins.

Verified on 2026-10-02: all 117 backend tests passed; frontend type checking, production build, account/booking/design/customer-review/provider-reply tests and proxy smoke checks passed. The additive migration matches the EF model. API/web containers were rebuilt, the migration applied locally, and real PostgreSQL reply uniqueness, nonempty-text, review foreign-key and Unicode checks passed in a rollback-only transaction. Local API readiness and public reviews/replies returned HTTP 200 with no-store caching. Existing data was preserved.

Manual interactive browser validation and real-stack authorized reply submission remain pending. Test customer review → provider reply → customer readback → public profile; then check duplicate/revoked Staff behavior, mobile layout and keyboard focus. Implementation does not commit or push changes automatically.
