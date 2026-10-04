# Customer ratings and completed-booking reviews

## Delivered scope

- Exactly one immutable review per completed booking, enforced by a database primary key.
- Only the saved booking's customer, with verified email, can submit it. Active Owner/Staff membership in that provider organization blocks self-reviews, even if membership was added after booking.
- Whole-number 1–5 rating, 10–2000 UTF-16 code units of trimmed written feedback, and explicit public-publication acknowledgement. Line breaks and tabs are accepted; other control characters and unknown request fields are rejected.
- Reviews are immediately Published in this portfolio release. No invented scores or review counts. Review images, edits, reporting, and administrator moderation workflows are deferred. Provider replies are now implemented separately; see `provider-review-replies.md`. The internal Hidden state is reserved for future moderation and excluded from public totals; there is no provider hide/delete API.
- Public responses expose only rating, feedback, timestamp, and the generic label “Customer with a completed booking.” They do not expose customer identifiers, names, emails, booking IDs, property snapshots, or internal organization IDs. Customers are warned not to type personal information into public feedback; automatic personal-data filtering is not implemented.
- Reviews are publicly readable only while the provider profile is published and currently eligible. Visibility is rechecked after projection. Private participant access survives profile unpublication/check expiry but revoked Staff lose access.
- Average rating and total count are calculated across all Published reviews for completed bookings, independent of the requested page. Empty profiles have `averageRating: null`, not a fabricated zero or five-star rating.

## API and permissions

| Route | Access | Purpose |
| --- | --- | --- |
| `GET /api/v1/bookings/{bookingId}/review` | Customer or active provider member | Read existing feedback and server-authoritative `canReview`/reason. |
| `POST /api/v1/bookings/{bookingId}/review` | Verified booking customer, non-affiliated | Publish `{rating, comment, publishAcknowledged:true}`. Requires cookie session and CSRF. Returns 201; duplicates/non-completed states return 409; wrong customer returns 404. |
| `GET /api/v1/providers/{providerId}/reviews?page=1&pageSize=10` | Anonymous, eligible published profile only | Paginated feedback and full-dataset rating summary. Page 1–100000; pageSize 1–50. |

All responses are no-store. The frontend proxy permits only GET/POST on the private review resource and GET on public reviews; writes retain exact-origin and CSRF forwarding. No update/delete routes are exposed. Append-only creation does not use If-Match; the booking key prevents duplicate submissions, including racing requests. See `docs/api/customer-reviews.openapi.json`.

## Database and local setup

`CompletedBookingReviews` adds only the BookingReviews table, a restricted booking foreign key, rating/status/nonempty-feedback checks, and an index. SQL code-point lengths differ from browser/.NET UTF-16 lengths: the API validates the 10–2000 limit; the database also rejects empty/oversized stored text. Existing booking/customer records are retained.

Local Compose applies migrations through its existing Development-only `Database__ApplyMigrations` setting when the API restarts. Do not delete volumes. Production must apply the reviewed migration separately; it is not applied automatically outside Development. Review the generated migration SQL before deployment.

## Test and acceptance checklist

Verified on 2026-10-02: all 110 backend tests passed; frontend type checking, production build, account/booking/design/review tests, and proxy smoke checks passed. The deterministic race test inserts a competing review between the HTTP pre-check and save, and verifies that the loser receives 409 while the winning feedback remains intact.

- Backend integration coverage: full completion lifecycle, review ownership and verified email, active membership/self-review, every non-completed state, duplicates, missing CSRF, invalid ratings/feedback/consent, overposting, Unicode feedback, bounded paging, averages, hidden-review exclusion, profile unpublication/revocation, private-field exclusion, DB rating/key constraints, and forbidden edit/delete methods.
- Frontend tests: validation boundaries, five accessible rating choices, required feedback/consent, no form for ineligible/already-reviewed states, escaped feedback, real aggregate/empty states.
- Proxy regression tests include review methods, CSRF/body forwarding, and untrusted-origin rejection. These mock tests do not establish backend permissions; integration tests do.
- The local API/web containers were rebuilt and the additive migration applied successfully. PostgreSQL duplicate/rating/nonempty-feedback/Unicode constraint checks passed in a rollback-only transaction, leaving existing data untouched. Public review reads and API readiness returned HTTP 200. The agent did not independently perform interactive browser review submission.
- On 2026-10-02, the user reported local testing completed successfully and approved committing and pushing the feature. This is user-reported acceptance, not independent agent confirmation of every edge case.
- Regression checklist: sign in as the booking customer, open a Completed booking, select stars, type fictional feedback, confirm public visibility, and submit. Refresh its provider profile to see the average and feedback. Verify retry behavior, mobile layout, keyboard focus, and duplicate prevention.

The implementation was initially left uncommitted; the user subsequently approved committing and pushing this feature.
