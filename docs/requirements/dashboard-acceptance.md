# Dashboard and booking-screen acceptance checks

Implemented: customer/provider workspace selection, active membership roles, scoped booking totals and recent bookings, setup links, readable booking statuses, provider-time-zone schedules, list/detail layout, booking history, required cancellation/decline reasons, expiry-aware actions, reload-required conflict recovery, and request review / success links.

Backend authorization, CSRF and If-Match version checks remain authoritative and unchanged. Dashboard totals cover the selected workspace; recent rows are limited to five and sorted by scheduled date descending, not a count of upcoming appointments.

## Manual browser acceptance

On 2026-10-02, the user reported that local dashboard and booking testing passed. The checks below remain the regression checklist; this report does not establish that each individual edge case was independently tested by the agent.

- Customer: empty state, properties/service links, unverified-email guidance, booking request review and success detail link.
- Provider Owner: select each organization; confirm scoped booking totals and setup links.
- Staff: active memberships only; booking controls visible, availability edit link absent.
- Booking: decline/cancel reason required; expired requests offer no decisions; start work unavailable before scheduled time; customer cannot start/complete work.
- Conflict: after a failed decision, actions stay disabled until reload; successful updates refresh details and list.
- Mobile: dashboard cards and list/detail stack without horizontal scrolling; keyboard navigation retains visible focus and readable labels.
- Sign out, expired session, failed API requests and retry behavior.

The agent's verification consists of automated and HTTP checks, not interactive browser validation.

## Automated checks (passed)

- `npm run typecheck` and `npm run build`.
- `node scripts/test-booking-ui.mjs`: role/status/time action rules, cancellation reason validation, time-zone formatting, and server-rendered controls.
- `node scripts/test-account-validation.mjs`: existing account-form regression checks.
- `node scripts/smoke-verification-proxy.mjs`: existing page/API-proxy regression checks.
- Local web Docker image rebuilt without touching database volumes.
