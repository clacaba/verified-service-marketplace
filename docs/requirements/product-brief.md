# Product brief

Status: Working baseline derived from the [master plan](../service-booking-marketplace-plan.md), sections 25–32.

## Product

Working name: **Verified Service Marketplace**.

A home-services marketplace where customers and approved partner companies can find and book verified cleaners, plumbers, electricians, and appliance-repair providers.

Customers are human account holders. Providers may be individuals or businesses; both operate through an organization. Partner organizations integrate their own applications. Platform administrators manage verification and moderation.

## Initial scope

1. Provider registration, profile creation, fictional verification evidence, administrator review, and precise public badges.
2. Categories, fixed-price service listings, search, availability, provider-confirmed bookings, notifications, and reviews.
3. Registered partner applications, scoped OAuth credentials, idempotent bookings, rate limits, and signed webhooks.

Fixed prices and provider confirmation are proposed MVP defaults. Quotes, hourly pricing, payments, payouts, maps, SMS, calendar integration, and real identity/background checks follow the core experience.

## Decisions still open

| Decision | Working treatment |
| --- | --- |
| Final name | Keep the repository's temporary name during development. |
| Country, currency, service areas | Configurable; do not infer from the developer's location. |
| Category-specific credentials | Configuration must be defined before publishing eligible services. |
| Revenue and platform fees | Deferred with payments. |
| Production hosting and retention | Record before deployment or collecting real documents. |
| Partner use cases | Start with discovery and provider-confirmed bookings; advanced partner operations are deferred. |

## First demonstration

Register a provider, save its profile, submit fictional documents, approve specific checks as an administrator, and view the public profile with simulated-check disclosures. Every protected operation enforces authorization and ownership in the API.
