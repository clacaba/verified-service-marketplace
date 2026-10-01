# Customer properties, provider availability, and bookings

## Delivered scope

Customer properties are physical service addresses, not tenants or organization boundaries. Any signed-in human account can act as a customer, including a Provider account; booking requires verified email. Only the owning customer can list, read, edit, or archive a property. Provider staff cannot browse customer properties. Addresses appear only inside bookings accessible to the customer and active provider organization members. All private responses are no-store; use fictional addresses in the portfolio demo.

Address fields: label 1–80, street line 1 1–200, optional street line 2 0–200, city/region 1–100, postal code 1–20, uppercase ISO country code. Values are trimmed; control characters and unknown JSON fields are rejected. Country validation is structural, not postal/geocoding verification. The region/postal fields are mandatory for this initial MVP. Archiving hides a property from future selection, preserves its record, and prohibits edits. It is not a privacy-erasure mechanism.

## Availability

One shared capacity per provider **organization**, across all its services and staff. Owner edits; Staff reads. No configured schedule means no bookable slots. GET returns a quoted all-zero GUID ETag until configured; PUT must include it. Subsequent PUTs require the current exact ETag. Weekly windows use IANA timezone and local minutes since midnight, 15-minute boundaries, Sunday=0. Multiple windows represent breaks. Windows cannot overlap or cross midnight. At most six windows/day and 60 date exceptions. A dated exception replaces that day's weekly hours; an empty windows array closes the day. Empty weekly hours close the calendar.

Buffer: 0–120 minutes before **and** after each appointment, inside working windows. Minimum notice: 0–10,080 minutes before appointment start. Public slot queries use explicit offset-bearing timestamps, at most seven days within a 30-day horizon. Results are UTC with the provider timezone for display. Slots are advisory, not reservations. Ambiguous fall-back local times and appointments/buffers spanning a timezone offset change are deliberately unavailable; this MVP does not offer both fall-back occurrences. Existing reservations remain valid after schedule edits, and still block capacity under their captured buffers.

The `/availability` editor currently uses validated JSON window inputs with examples; a visual calendar editor remains a UX enhancement. `/services/{id}` has a UTC-date selector and displays slots in the provider timezone.

## Booking creation and concurrency

POST requires verified email, a currently eligible published service, the customer's active property, and a valid free slot. Active organization members cannot book their own organization. Pricing/duration/address/timezone/buffer are calculated and captured on the server; clients cannot submit prices or ownership fields. The response snapshot's nested keys are PascalCase because it is a stored immutable JSON document, while normal endpoint fields use camelCase.

Pending requests reserve capacity. Overlap uses half-open UTC buffered intervals: `existing.from < requested.until && existing.until > requested.from`. Adjacent unbuffered bookings are allowed. A request changes the same ProviderSchedule.Version concurrency token that calendar updates and booking decisions change. EF's database compare-and-swap and SaveChanges transaction roll back booking insertion, history, notices, and queued emails if a competing reservation or calendar change wins. This is a database-backed optimistic serialization protocol, not an in-process lock. A conflict returns 409; clients reload slots and retry. All future booking-writing code must participate in this protocol; direct database writes are outside the API invariant.

Property and service versions also change during booking to serialize snapshots against property archive/edits and offer price/duration/publication changes. Editors with an old ETag must reload. Booking history stores snapshots, so later address/catalog edits do not rewrite it. There is no rescheduling endpoint yet: cancel before the appointment begins, then request another slot.

Free-text service area is displayed but not geocoded: the provider must confirm that the address is within their service coverage. No payment or cancellation fees. Human booking idempotency, geo matching, staff assignment/multiple capacity, recurring bookings, payments, disputes, reviews, and partner OAuth/idempotency/webhooks remain future features.

## Lifecycle and authorization

PendingProviderConfirmation → Confirmed → InProgress → Completed.

Pending → Declined, Expired, CancelledByCustomer, or CancelledByProvider. Confirmed → either cancellation, before scheduled start. Owner and active Staff can confirm/decline/start/complete/cancel for their organization. Customers can cancel their own pending/confirmed bookings before start. Only provider members can start work, at/after the scheduled start. Invalid/terminal transitions return 409. Declines and cancellations require a reason; reasons max 500 without control characters. Every transition records actor/status/reason/UTC time, atomically with recipient notices and encrypted queued email. GET history omits account ids while the audit table preserves them.

Existing bookings remain visible to their participants even after service archive/provider verification expiry. Staff deactivation immediately removes booking/address/notice access. Booking notices are separate from verification notices at `/api/v1/booking-notifications`, preventing changes to verification authorization. The current booking page shows the newest 20 notices; the endpoint supports pagination. Mark-read support is not implemented for booking notices.

## Expiry and environment

Pending deadline is the earlier of scheduled start or request time plus `Booking:PendingMinutes` (default 60; valid 5–1440). Expired pending requests stop blocking availability **immediately**, independently of worker timing, and cannot be confirmed. `Booking:ExpiryEnabled=true` runs a 30-second sweep that persists Expired history and recipient notifications. Compose enables it. The existing encrypted email queue/SMTP retry worker delivers booking emails using the same credentials as account/verification mail. No new secret keys are required.

Compose `.env`: `BOOKING_PENDING_MINUTES=60`. Host API: `Booking__PendingMinutes=60`, `Booking__ExpiryEnabled=true`, plus existing PostgreSQL/SMTP/DataProtection settings. Host expiry is opt-in; without it expired requests release capacity but their persisted status/notices wait for a later enabled sweep. API GET currently returns the persisted status; an elapsed pending deadline can therefore be visible briefly as Pending with an expired timestamp.

Incremental migrations: CustomerProperties, ProviderAvailabilityAndBookings, then BookingReservationConstraints. Review the combined SQL under `infrastructure/properties-availability-bookings.sql`. Run existing local-development instructions and apply migrations before testing with real PostgreSQL. Do not apply production migrations without reviewing backups and rollout.

## Postman walkthrough

1. Register/login and obtain a fresh CSRF token with cookie jar enabled. Create/approve/publish a fictional provider and service using the existing verification/catalog walkthrough.
2. As Owner GET `/api/v1/organizations/{organizationId}/availability`, copy ETag, then PUT with X-CSRF-Token and If-Match:

```json
{"timeZone":"Asia/Manila","bufferMinutes":15,"minimumNoticeMinutes":60,"weekly":[{"day":1,"startMinute":540,"endMinute":1020}],"exceptions":[]}
```

3. In a separate verified customer session POST `/api/v1/properties`:

```json
{"label":"Demo home","addressLine1":"123 Fictional Street","addressLine2":"","city":"Demo City","region":"Demo Region","postalCode":"1234","countryCode":"PH"}
```

4. GET `/api/v1/services/{serviceId}/availability?from=<future ISO timestamp>&until=<up to seven days later>`; encode offset `+` as `%2B`, or use `Z`. Select a returned startsAt.
5. POST `/api/v1/bookings` with `{"serviceId":"...","propertyId":"...","startsAt":"...Z"}` and CSRF. Save booking id/ETag; check immutable snapshot and expiry.
6. As active Owner/Staff POST `/api/v1/bookings/{bookingId}/decisions` with current booking ETag and `{"action":"confirm","reason":""}`. Other actions: decline, start, complete, cancel. Use a nonempty reason when declining/cancelling. Start is rejected before scheduled start.
7. GET `/api/v1/bookings` as customer, or `?organizationId=...` as provider; detail/history at `/bookings/{id}`. Check `/booking-notifications`. Try the same slot twice, stale ETag, another customer's property, a deactivated Staff member, and expired confirmation.

Automated tests cover ownership, CSRF/version checks, reservation overlaps, immutable snapshots, database concurrency rollback, immediate expiry release/persisted sweep, member revocation, public-slot privacy, valid lifecycle, schedule breaks/buffers/exceptions, and conservative DST handling. SQLite tests are not a substitute for real PostgreSQL concurrent-request and browser/SMTP acceptance. Those checks remain pending when Docker's engine is unavailable.
