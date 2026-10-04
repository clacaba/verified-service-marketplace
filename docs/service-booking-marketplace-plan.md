# Service Booking Marketplace — Full-Stack Project Plan

## 1. Product concept

Build a web marketplace where customers can find, compare, and book trusted local service providers. A focused marketplace—such as home services—is easier to design, explain, and complete than a platform containing every possible service.

Example categories include home cleaning, plumbing, electrical work, appliance repair, tutoring, photography, personal training, and beauty services.

### Recommended initial scope

Start with a **verified home-services marketplace** for cleaners, plumbers, electricians, and appliance-repair providers. This niche naturally demonstrates provider verification, scheduling, location search, payments, messaging, reviews, and administrator workflows.

## 2. Terminology

| Term | Meaning and purpose |
| --- | --- |
| Customer | A user who searches for and books a service. |
| Provider | A person or business that offers services to customers. |
| Administrator | A trusted operator who manages users, verification, disputes, moderation, and platform settings. |
| Service | Work offered by a provider, such as house cleaning or plumbing repair. |
| Service category | A group used to organize related services. |
| Listing | The public presentation of a service, including its description, price, images, and conditions. |
| Availability | The dates and times when a provider can accept work. |
| Time slot | A specific period that a customer can select for a booking. |
| Booking | A customer's reservation or request for a service. |
| Quote | A provider's proposed price when the final cost cannot be determined in advance. |
| Verification | A process used to confirm a provider's identity, business, qualifications, or payout account. |
| Credential | Evidence of a qualification, professional license, certification, or business registration. |
| Background check | A check conducted by an authorized third party. It must not be described as completed if the demo only simulates it. |
| Payout | Money transferred to a provider after an eligible booking. |
| Platform fee | The amount retained by the marketplace from a transaction. |
| Review | A customer's rating and feedback after a completed booking. |
| Dispute | A reported disagreement involving a booking, payment, or service. |
| Audit log | A durable record of important actions and changes. |
| Notification | An in-app, email, SMS, or push message caused by an event. |
| Client company | An external business that integrates its own application with the marketplace API. |
| API client | A registered software application belonging to a client company. It receives credentials and approved permissions. |
| API scope | A named permission granted to an API client, such as `bookings:read` or `bookings:write`. |
| Webhook | An HTTPS notification sent to a client system when an event occurs. |
| OpenAPI | A machine-readable description of the API used for documentation, testing, and SDK generation. It is not the same as making an API publicly accessible. |

## 3. User roles and responsibilities

### Customer

Purpose: discover and book reliable services.

- Register, sign in, and verify an email address
- Optionally verify a phone number
- Manage a profile and service addresses
- Search and filter providers
- View provider profiles and verification badges
- Select a service and an available time
- Request a quote or create a direct booking
- Pay through a secure checkout
- Track booking progress
- Communicate with the provider
- Cancel according to platform policy
- Review a provider after completion
- Report a problem or open a dispute

### Provider

Purpose: advertise services, manage work, and receive payments.

- Register as an individual or business
- Create a professional profile
- Submit verification information and documents
- Add credentials and portfolio images
- Create services and configure prices
- Define service areas and availability
- Accept, decline, or reschedule bookings
- Prepare quotes
- Update work status
- Communicate with customers
- View earnings and payouts
- Respond to reviews

### Administrator

Purpose: maintain trust, safety, and platform operations.

- Manage users, categories, and listings
- Review provider-verification applications
- Approve, reject, or request additional information
- Suspend providers and services
- Review audit history
- Moderate reviews and reports
- Manage bookings and disputes
- Process or record refunds
- View platform reports
- Configure fees and marketplace settings

## 4. Recommended technology stack

### Frontend

- Next.js, React, and TypeScript
- Tailwind CSS and an accessible component library
- React Hook Form and Zod for forms and client-side validation
- TanStack Query for API state, caching, retries, and invalidation
- A typed API client generated from OpenAPI where practical

### Backend

- ASP.NET Core Web API and C#
- Entity Framework Core and PostgreSQL
- ASP.NET Core Identity
- Revocable HttpOnly cookie sessions for the first-party website; OAuth access tokens for future partner integrations
- FluentValidation
- SignalR for real-time messages and status updates
- Hangfire or Quartz.NET for background jobs
- Swagger/OpenAPI
- xUnit for automated tests

### Infrastructure

- Docker for repeatable local and hosted environments
- Private object storage, such as AWS S3, for images and documents
- Managed PostgreSQL for production
- GitHub Actions for automated tests and deployment
- Centralized logging and error monitoring
- Environment-specific configuration and secret management

Use a **modular monolith** initially. It provides clear module boundaries without adding premature microservice complexity.

## 5. System structure

```text
Next.js frontend             Third-party client applications
       |                                  |
       +------------------+---------------+
                          v
                 Versioned API boundary
       |
       +-- Identity and Access
       +-- Provider Management
       +-- Verification
       +-- Service Catalog and Search
       +-- Availability and Booking
       +-- Quotes
       +-- Payments and Payouts
       +-- Messaging and Notifications
       +-- Reviews and Disputes
       +-- Administration and Auditing
       |
       +-- PostgreSQL
       +-- Private Object Storage
       +-- External Integrations
```

## 6. Authentication and authorization

### Account flow

1. A user provides their name, email, password, and account type.
2. The backend validates the request and creates the account.
3. The backend sends an expiring email-verification link.
4. The user verifies the email address and signs in.
5. The backend issues an HttpOnly session cookie and records a revocable session in the database.
6. The browser sends its session cookie through the first-party BFF; mutations also require a current CSRF token.
7. The backend checks the user's role, permissions, and ownership of the requested resource.

### Suggested roles

- `Customer`
- `Provider`
- `VerificationReviewer`
- `SupportAgent`
- `Administrator`

Hiding a frontend button is not security. Every protected operation must be authorized by the backend.

Human users and third-party applications require different authentication flows. Interactive users authenticate with an authorization-code flow or the application's secure session flow. Server-to-server API clients use OAuth 2.0 client credentials. API keys may be supported for a limited developer sandbox, but should not replace scoped OAuth credentials for production integrations.

## 7. Provider verification

### Verification types

- Email verification
- Phone verification
- Identity verification
- Business verification
- Qualification or license verification
- Payout-account verification
- Optional third-party background check

### Verification statuses

```text
Draft
Submitted
UnderReview
AdditionalInformationRequired
Approved
Rejected
Expired
Suspended
```

### Workflow

1. The provider completes a professional profile.
2. The provider selects individual or business registration.
3. The provider uploads the required documents.
4. The backend stores private object references and document metadata.
5. The application enters `Submitted` and then `UnderReview`.
6. A reviewer approves, rejects, or requests more information.
7. The backend records the decision and reviewer in the audit log.
8. The provider receives a notification.
9. Approved providers receive badges that identify exactly what was verified.
10. The system monitors expiration dates and requests renewal.

Use precise badges such as **Identity verified**, **Business verified**, and **License verified** instead of presenting every provider with one ambiguous badge. Never expose document images or sensitive document numbers to customers.

### Portfolio-safe implementation

- Implement real email verification.
- Simulate phone OTP or use a sandbox provider.
- Upload only fictional verification documents in the public demo.
- Build a real administrator-review workflow.
- Label simulated identity and background checks clearly.
- Add a sandbox identity-verification provider only after the core workflow works.

## 8. Provider profiles and service listings

A provider profile should include:

- Display or business name
- Profile and cover images
- Biography
- Service categories and service area
- Experience and relevant credentials
- Verification badges
- Portfolio images
- Average rating and completed-booking count
- Cancellation policy and typical response time

A service listing should include:

- Title and description
- Category
- Pricing model and base price
- Estimated duration
- Included and excluded work
- Required customer information
- Service location type
- Images
- Active or inactive status

Supported pricing models can include fixed price, hourly rate, starting price, and custom quote.

## 9. Search and discovery

Customers should be able to filter by:

- Search text
- Category
- Location or distance
- Date and availability
- Price range
- Rating
- Verification status
- Service-delivery method
- Sort order

Use server-side filtering and pagination. PostgreSQL full-text search is sufficient for the first release; a dedicated search engine can be added later if justified by scale.

## 10. Availability and scheduling

Providers should configure:

- Weekly working hours
- Breaks and buffer time
- Blocked dates and holidays
- Minimum booking notice
- Supported booking durations
- Service-specific availability
- Time zone

The backend must detect scheduling conflicts and prevent double bookings. Store timestamps in UTC and convert them to the user's time zone for display.

## 11. Booking lifecycle

Recommended statuses:

```text
Draft
PendingProviderConfirmation
Confirmed
InProgress
Completed
CancelledByCustomer
CancelledByProvider
Declined
Disputed
Refunded
```

Typical journey:

```text
Select service and time
        |
        v
Submit booking request
        |
        v
Provider accepts or declines
        |
        v
Complete the booked work
        |
        v
Release eligible payment
        |
        v
Customer leaves a review
```

Every status change should record the previous status, new status, acting user, timestamp, and optional reason.

## 12. Quote workflow

1. The customer describes the work and may upload reference images.
2. The provider reviews the request.
3. The provider submits a price, description, and expiration date.
4. The customer accepts or rejects the quote.
5. An accepted quote becomes a booking.
6. The accepted version becomes immutable unless both parties approve a revision.

## 13. Payments and payouts

Use a payment platform with marketplace support, only in test mode for the portfolio.

1. The customer enters payment details through the payment provider's secure interface.
2. The application receives a token or external reference, never raw card details.
3. Payment is authorized or collected.
4. The backend records the external transaction reference.
5. After completion, the platform calculates its fee and provider earnings.
6. Funds become eligible for payout.
7. Refunds and disputes update internal transaction records.

Required records include payments, payment attempts, refunds, platform fees, provider earnings, payouts, and webhook events. Webhook processing must be idempotent so duplicate delivery cannot create duplicate transactions.

## 14. Reviews and ratings

Only customers associated with completed bookings may submit reviews. A review can contain a one-to-five-star rating, written feedback, optional images, a provider response, and a moderation status.

Prevent multiple reviews for one booking, self-reviews, and reviews for cancelled bookings. Providers must not be able to delete unfavorable reviews. Aggregate ratings should be recalculable from approved reviews.

## 15. Messaging and notifications

Create one private conversation for each booking. Support text, safe image attachments, timestamps, read indicators, real-time delivery, and reporting. Booking-scoped messaging is safer and easier to moderate than unrestricted messaging.

Notifications should cover:

- Email verification
- Verification submission and decision
- New, accepted, declined, or cancelled bookings
- Booking reminders
- Quotes
- Payment success or failure
- New messages and reviews
- Credential expiration

Support in-app notifications and email first. Add SMS only when it adds clear value. Dispatch external notifications through background jobs so slow providers do not delay API responses.

## 16. External integrations

| Integration | Purpose | Initial approach |
| --- | --- | --- |
| Email provider | Verification, booking updates, and receipts | Recommended for MVP |
| SMS provider | Phone OTP and urgent reminders | Simulated or sandbox initially |
| Object storage | Profile images, service images, and private documents | Recommended for MVP |
| Payment provider | Checkout, refunds, and marketplace payouts | Test mode after core booking flow |
| Maps/geocoding | Address selection, distance, and service areas | Optional for MVP |
| Identity provider | Provider identity verification | Simulated admin review initially |
| Calendar API | External calendar synchronization | Later release |
| Error monitoring | Frontend and backend error tracking | Recommended |
| Product analytics | Usage measurement | Optional |
| Antivirus scanning | Uploaded-file scanning | Production enhancement |

## 17. Frontend pages

### Public pages

- Home
- Browse services and search results
- Service details
- Provider profile
- Categories
- Safety and verification explanation
- Registration and sign-in

### Customer dashboard

- Overview
- Current and previous bookings
- Booking details
- Messages
- Saved providers
- Reviews
- Addresses and payment methods
- Notifications and account settings

### Provider dashboard

- Setup checklist and verification center
- Provider profile
- Services
- Availability calendar
- Booking requests and active bookings
- Quotes
- Messages and reviews
- Earnings and payouts
- Notifications and account settings

### Administrator dashboard

- Platform overview
- Verification queue and verification details
- Users and providers
- Categories and listings
- Bookings
- Payments, refunds, and disputes
- Reviews and reports
- Audit logs
- Platform configuration

## 18. Frontend-to-backend integration

```text
React form
    |
    v
Client-side schema validation
    |
    v
Authenticated API request
    |
    v
Backend authorization and validation
    |
    v
Application service and business rules
    |
    v
Database or external integration
    |
    v
Standard API response
    |
    v
Frontend cache update and user feedback
```

The frontend renders the interface, gives immediate feedback, manages UI and server state, calls APIs, and presents loading and error states. The backend remains authoritative for security, validation, booking conflicts, prices, payments, file access, and audit history.

## 19. Suggested API areas

```text
/api/auth
/api/users
/api/customers
/api/providers
/api/provider-verifications
/api/services
/api/categories
/api/search
/api/availability
/api/bookings
/api/quotes
/api/payments
/api/payouts
/api/messages
/api/reviews
/api/notifications
/api/admin
/api/audit-logs
/api/webhooks
```

Example endpoints:

```http
POST   /api/auth/register
POST   /api/auth/login
GET    /api/providers/{id}
POST   /api/providers/me/verifications
POST   /api/services
GET    /api/services/search
GET    /api/providers/{id}/availability
POST   /api/bookings
PATCH  /api/bookings/{id}/status
POST   /api/bookings/{id}/quotes
POST   /api/payments/checkout
POST   /api/bookings/{id}/reviews
GET    /api/admin/verifications
POST   /api/admin/verifications/{id}/approve
POST   /api/admin/verifications/{id}/reject
```

## 20. Public API and third-party integrations

The marketplace must be an **API-first platform**. Its web frontend is one API consumer, while approved client companies can connect their own websites, mobile applications, property-management systems, or internal tools.

### API audiences

- **First-party API:** used by the marketplace frontend and administered by the same product team.
- **Partner API:** used by approved client companies under a commercial or integration agreement.
- **Public developer API:** documented for external developers, but still requires registration, credentials, scopes, and usage limits.
- **Internal service API:** used only by trusted backend components and never exposed as a public contract.

“Open API” means that external developers can register and integrate through a supported contract. It does not mean anonymous access. “OpenAPI” refers specifically to the API specification format.

### Client-company model

Each external company should have an organization and one or more separately managed API clients:

```text
Client Company / Organization
├── Development API client
├── Staging API client
└── Production API client
```

Every API client should have its own identifier, hashed or provider-managed secret, environment, scopes, status, rate-limit plan, allowed webhook endpoints, creation date, last-used date, and revocation history. Credentials must never be shared between environments.

### Authentication and authorization

Use OAuth 2.0 client credentials for machine-to-machine access:

```http
POST /oauth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&
client_id=example-client&
client_secret=example-secret&
scope=providers:read bookings:read bookings:write
```

The issued access token should contain the API client identity, owning `organization_id`, scopes, audience, issuer, and expiration. It should not impersonate a human user.

Suggested scopes:

```text
providers:read
services:read
availability:read
customers:read
customers:write
bookings:read
bookings:write
bookings:cancel
quotes:read
quotes:write
payments:read
webhooks:manage
```

Partner authorization must enforce both the scope and organization boundary. A client with `bookings:read` may read only bookings that its organization owns or is explicitly permitted to access.

### Public API conventions

- Expose versioned routes such as `/api/v1/providers` and `/api/v1/bookings`.
- Publish an OpenAPI 3 specification and interactive documentation.
- Use consistent JSON naming, pagination, filtering, sorting, and error responses.
- Return a correlation/request ID for troubleshooting.
- Require an idempotency key for retryable create, payment, and cancellation commands.
- Support safe retries and idempotent webhook processing.
- Publish a compatibility and deprecation policy.
- Keep internal entity models separate from public request and response contracts.
- Never expose internal stack traces, storage paths, or confidential provider-verification data.

Recommended error shape:

```json
{
  "type": "https://developers.example.com/errors/booking-conflict",
  "title": "The selected time is unavailable",
  "status": 409,
  "code": "booking_conflict",
  "requestId": "01JEXAMPLE",
  "errors": []
}
```

### Initial partner endpoints

```http
GET    /api/v1/categories
GET    /api/v1/providers
GET    /api/v1/providers/{providerId}
GET    /api/v1/services
GET    /api/v1/providers/{providerId}/availability
POST   /api/v1/customers
GET    /api/v1/customers/{externalCustomerId}
POST   /api/v1/bookings
GET    /api/v1/bookings/{bookingId}
POST   /api/v1/bookings/{bookingId}/cancel
GET    /api/v1/bookings/{bookingId}/quotes
POST   /api/v1/webhook-endpoints
```

Support external identifiers so a client company can safely associate marketplace records with its own records without using email addresses as keys.

### Webhooks

Clients should not have to poll continuously. Deliver events such as:

```text
provider.verification.updated
booking.created
booking.confirmed
booking.rescheduled
booking.in_progress
booking.completed
booking.cancelled
quote.created
payment.succeeded
payment.failed
refund.completed
```

Sign each webhook using a per-client secret. Include an event ID, event type, creation time, API version, organization ID, and payload. Retry failed deliveries with exponential backoff and provide a delivery log plus manual replay. Consumers must deduplicate events by event ID.

### Developer experience

- Developer portal with registration and application management
- Sandbox environment containing fictional providers and bookings
- Copyable API examples and downloadable OpenAPI specification
- Postman collection and generated SDKs where useful
- Webhook endpoint registration and delivery logs
- Credential rotation and revocation
- Usage dashboard showing requests, failures, latency, and rate-limit consumption
- Changelog, version lifecycle, and migration guides

### API governance and operations

- Apply per-client and per-endpoint rate limits.
- Return standard rate-limit headers and `429 Too Many Requests` when exceeded.
- Maintain request logs without recording secrets or prohibited personal data.
- Track API usage for quotas and optional partner billing.
- Separate sandbox and production credentials and data.
- Require HTTPS and validate webhook URLs.
- Allow administrators to suspend a client without disabling its organization.
- Define service-level objectives and an incident communication process before offering production integrations.

## 21. Core database entities

### Users and access

- Users
- Roles
- UserRoles
- RefreshTokens
- EmailVerificationTokens

### Provider data

- ProviderProfiles
- ProviderDocuments
- ProviderVerifications
- VerificationReviews
- ProviderCredentials
- ServiceAreas

### Marketplace data

- Categories
- Services
- ServiceImages
- AvailabilityRules
- AvailabilityExceptions
- Addresses

### Transactions

- Bookings
- BookingStatusHistory
- Quotes
- QuoteRevisions
- Payments
- Refunds
- ProviderEarnings
- Payouts

### Communication and trust

- Conversations
- Messages
- Notifications
- Reviews
- ReviewReports
- Disputes
- AuditLogs

### API platform

- ApiClients
- ApiClientCredentials
- ApiScopes
- ApiClientScopes
- ApiUsageRecords
- ApiIdempotencyRecords
- WebhookEndpoints
- WebhookEvents
- WebhookDeliveries
- ExternalRecordMappings

## 22. Security requirements

- Use a trusted framework for password hashing.
- Expire email-verification and password-reset tokens.
- Use short-lived access tokens and refresh-token rotation.
- Enforce role, permission, and resource-ownership checks.
- Rate-limit login, reset, and OTP attempts.
- Validate file type, extension, size, and content.
- Store verification documents privately.
- Use signed, expiring URLs for authorized file access.
- Keep secrets out of source control.
- Validate payment-webhook signatures.
- Record sensitive operations in audit logs.
- Define personal-data retention and deletion rules.
- Never put real identity documents in a public demo.
- Store API client secrets only as secure hashes or through a managed identity provider.
- Use short-lived, audience-restricted tokens for API clients.
- Enforce API scopes together with organization-level data isolation.
- Sign webhooks and protect against replay attacks.
- Rate-limit by API client, organization, endpoint, and IP where appropriate.

## 23. Testing plan

### Backend tests

- Registration, sign-in, and token refresh
- Role and resource-ownership authorization
- Provider-verification transitions
- Booking conflict detection
- Booking-status transitions
- Price and platform-fee calculations
- Review eligibility
- Webhook signature checks and idempotency
- Database and API integration tests
- API-client authentication and scope enforcement
- Cross-organization isolation for partner clients
- Idempotency behavior and duplicate webhook delivery
- API version compatibility and rate limiting

### Frontend tests

- Form validation
- Loading, empty, success, and error states
- Role-based navigation
- Booking and verification workflows
- Administrator decisions
- Responsive layout and accessibility

### Main end-to-end demonstration

1. A provider registers and submits verification.
2. An administrator approves the provider.
3. The provider creates a service and availability.
4. A customer finds and books the service.
5. The provider accepts and completes the booking.
6. A test payment succeeds.
7. The customer leaves a review.
8. A registered partner receives signed booking-status webhooks.

This scenario should be the main portfolio demonstration and automated end-to-end test.

## 24. Development phases

### Phase 1 — Foundation

- Establish project architecture and database
- Implement authentication, roles, and profiles
- Define consistent API errors, validation, and logging
- Configure unit and integration testing

### Phase 2 — Provider onboarding

- Provider profile
- Secure document upload
- Verification workflow
- Administrator review
- Verification badges and audit history

### Phase 3 — Marketplace

- Categories and service listings
- Service images
- Search, filters, and pagination
- Public provider pages

### Phase 4 — Scheduling and booking

- Availability and time-zone handling
- Booking requests
- Conflict prevention
- Status history
- Customer and provider dashboards

### Phase 5 — Trust and communication

- Reviews
- Booking-scoped messaging
- Notifications
- Reports and moderation

### Phase 6 — Commerce

- Test payments
- Platform fees
- Refund records
- Provider earnings and test payouts
- Signed, idempotent payment webhooks

### Phase 7 — Portfolio readiness

- Responsive, accessible user interface
- Complete automated test coverage for critical paths
- Deployment and seeded demo accounts
- Architecture diagram and OpenAPI documentation
- Screenshots and a short demonstration video
- Professional repository README

### Phase 8 — Partner API

- Versioned public API contracts
- OAuth client-credentials authentication and scopes
- Client-company and application registration
- Idempotency, rate limits, and usage records
- Signed webhooks with retries and delivery logs
- Sandbox environment and developer portal
- Published OpenAPI specification, examples, and changelog

## 25. MVP boundary

The first publishable version should include:

- Customer, provider, and administrator roles
- Authentication and email verification
- Provider profiles
- Administrator-controlled provider verification
- Service listings
- Search and filters
- Availability and booking workflows
- In-app notifications
- Reviews
- Responsive dashboards
- Seeded fictional demonstration data
- Automated tests and API documentation

Payments, maps, SMS, live identity verification, and calendar synchronization can follow the MVP. A complete core experience is more valuable than many unfinished integrations.

The public API should initially expose read-only service discovery and the core booking lifecycle. Advanced partner payments, provider administration, and bulk operations should remain outside the first API release. A portfolio-ready public API must still demonstrate one sandbox client, scoped credentials, idempotent booking creation, rate limiting, OpenAPI documentation, and signed booking webhooks.

## 26. Decisions required before implementation

- Product name and visual identity
- Initial service niche
- Target country or region
- Whether providers may be individuals, businesses, or both
- Supported pricing models
- Booking, cancellation, and refund rules
- Required verification types per category
- Platform-fee policy
- Final technology and hosting choices
- Demo-data and document-retention policies
- Wireframes for the primary user journeys
- Which partner companies and use cases the first API supports
- Whether API access is approval-only, paid, or available through self-service registration
- API scopes, rate-limit plans, support policy, and version-lifecycle policy

## 27. Portfolio deliverables

The finished project should include:

- A deployed frontend and API
- A repository with clear setup instructions
- Seeded customer, provider, and administrator demo accounts
- An architecture diagram
- An entity-relationship diagram
- OpenAPI/Swagger documentation
- Automated unit, integration, and end-to-end tests
- Screenshots and a short demonstration video
- A discussion of important technical decisions and tradeoffs
- Clear disclosure that verification, payments, and background checks use test or simulated data where applicable
- A developer portal or documentation site for third-party clients
- Sandbox API credentials, an OpenAPI specification, and a Postman collection
- A working webhook demonstration with signing, retry, and delivery history

## 28. Confirmed project decisions

The following decisions are confirmed unless deliberately revised in a later architecture decision record:

- The product will be developed as a new project unrelated to NexusEnroll and LegalSynq.
- The implementation will live in a **new, independent Git repository**, not as a folder or nested repository inside NexusEnroll.
- LegalSynq may be used as an architectural reference, but its proprietary source code, schemas, names, secrets, and business logic must not be copied.
- The backend will begin as a modular monolith with explicit module boundaries.
- The platform will be multi-organization, using `organization_id` as the primary ownership and isolation boundary.
- `property_id` will identify a physical customer property or service location; it will not replace `organization_id`.
- The web application will be API-first, and approved external companies will be able to integrate through a versioned partner API.
- Partner access will use registered API clients, scoped credentials, rate limits, idempotency, and signed webhooks.
- The initial portfolio demonstration will use fictional identities, documents, payments, providers, customers, and properties.

## 29. Required planning artifacts before implementation

The broad product plan must be converted into the following buildable artifacts before full feature development begins.

### 29.1 Product brief

Confirm and document:

- Product name and one-sentence value proposition
- Initial service niche and categories
- Target country or region
- Customer and provider types
- Individual-provider and provider-company support
- Booking and pricing models
- Verification requirements
- Platform revenue model
- API partner use cases

Recommended initial definition:

> A home-services marketplace where customers and approved partner companies can find and book verified cleaners, plumbers, electricians, and appliance-repair providers.

### 29.2 Organization and ownership model

Use the following conceptual hierarchy:

```text
Organization
├── Organization members
├── API clients
├── Provider profiles and staff
├── Services
└── Partner-created bookings

Customer
└── Properties
    └── Service addresses
```

Define whether customers may belong to organizations, whether one user may belong to multiple organizations, whether providers may operate independently, and which records can participate in cross-organization workflows.

### 29.3 MVP requirements and acceptance criteria

Convert every MVP capability into testable user stories. For example:

```text
As a provider,
I want to submit identity and business documents,
so that an administrator can verify my profile.

Acceptance criteria:
- Only provider accounts can submit verification.
- Required documents depend on provider type.
- A submitted application cannot be edited while under review.
- A reviewer can approve, reject, or request more information.
- Every decision creates an audit entry.
- Approved providers receive precise verification badges.
```

The MVP backlog must cover authentication, organizations, provider onboarding, verification, catalog, availability, search, booking, notifications, reviews, administration, partner API access, and webhooks.

### 29.4 User journeys and wireframes

Prepare low-fidelity wireframes for these end-to-end journeys:

1. Customer registration and booking
2. Provider registration and verification
3. Administrator verification review
4. Provider service creation
5. Provider availability setup
6. Booking acceptance and completion
7. Customer review
8. Client-company and API-client registration
9. Partner-created API booking
10. Webhook registration and delivery inspection

### 29.5 Domain model and entity-relationship diagram

Define the relationships, required fields, ownership boundary, lifecycle, and invariants for:

- User and authentication session
- Organization and organization membership
- Customer profile, property, and address
- Provider profile, staff, verification, credential, and document
- Category, service, service area, and availability
- Booking, status history, quote, payment, and refund
- Review, notification, dispute, and audit log
- API client, scope, idempotency record, webhook endpoint, event, and delivery

### 29.6 Business-rule catalogue

Document authoritative rules for:

- Publishing and disabling services
- Provider-verification prerequisites
- Instant bookings versus provider approval
- Schedule-conflict prevention
- Booking changes, cancellation, and refunds
- Completion and dispute handling
- Review eligibility
- Verification expiration and renewal
- Partner-created customers and bookings
- Booking ownership and visibility
- Cross-organization access

### 29.7 API contract

Create the OpenAPI contract before implementing controllers. It must define:

- Versioned routes and resource models
- Request validation and response envelopes
- Pagination, filtering, and sorting
- Problem-details error format
- User and API-client authentication
- OAuth scopes and organization isolation
- Idempotency behavior
- Rate-limit behavior and headers
- Webhook schemas and signatures
- Compatibility and deprecation policy

### 29.8 Architecture decision records

Create short records explaining these decisions:

- Modular monolith before service extraction
- Next.js frontend and ASP.NET Core backend
- PostgreSQL and organization-based isolation
- OAuth 2.0 client credentials for partners
- Private object storage for documents
- Background jobs for notifications and webhooks
- Transactional outbox for reliable event publication
- REST APIs plus signed webhooks for partners
- Separate repository and independent product identity

## 30. New repository plan

The repository must be created outside the NexusEnroll working tree to keep source history, dependencies, deployment, issues, and documentation independent.

Recommended temporary repository name:

```text
verified-service-marketplace
```

The final name should be replaced with the chosen product name before the first public release.

Recommended structure:

```text
verified-service-marketplace/
├── apps/
│   ├── web/                    # Customer and provider Next.js application
│   ├── admin/                  # Administrative Next.js application
│   └── api/                    # ASP.NET Core API composition root
├── modules/
│   ├── identity/
│   ├── organizations/
│   ├── customers/
│   ├── providers/
│   ├── verification/
│   ├── catalog/
│   ├── availability/
│   ├── bookings/
│   ├── payments/
│   ├── notifications/
│   └── integrations/
├── tests/
│   ├── unit/
│   ├── integration/
│   └── end-to-end/
├── docs/
│   ├── requirements/
│   ├── architecture/
│   ├── decisions/
│   ├── api/
│   └── diagrams/
├── infrastructure/
├── scripts/
├── docker-compose.yml
└── README.md
```

Repository initialization should include:

- Git repository and appropriate `.gitignore`
- Root README with product purpose and development status
- Solution and frontend workspace scaffolding
- Formatting, linting, and editor configuration
- Local Docker Compose dependencies
- Environment-variable templates containing no secrets
- Pull-request and issue templates
- Continuous integration for build, lint, and tests
- Dependency and secret scanning
- Architecture-decision and requirements templates

Do not initialize the new repository as a nested Git repository inside NexusEnroll.

## 31. First implementation milestone

Build one complete vertical slice before expanding the catalog or introducing multiple independently deployed backend services:

```text
Provider registers
        |
        v
Creates a provider profile
        |
        v
Submits fictional verification documents
        |
        v
Administrator reviews the submission
        |
        v
Provider is approved
        |
        v
Public profile displays precise verification badges
```

The slice must include frontend pages, API endpoints, database migrations, authorization, private file storage, audit events, notifications, and automated tests.

After this milestone, implement service listings and the booking lifecycle. Extract a module into a separately deployed service only when there is a demonstrated operational, scaling, security, or ownership reason.

## 32. Recommended execution order

1. Choose the final product and repository name.
2. Approve the product brief and initial service categories.
3. Finalize organization, provider, customer, property, and API-client definitions.
4. Write the MVP user stories and acceptance criteria.
5. Create the entity-relationship diagram.
6. Create low-fidelity wireframes for the primary journeys.
7. Draft and review the versioned OpenAPI contract.
8. Record the initial architecture decisions.
9. Create the new repository outside NexusEnroll.
10. Scaffold the frontend, backend, tests, documentation, and local infrastructure.
11. Implement authentication and the provider-verification vertical slice.
12. Add the service catalog, availability, and booking lifecycle.
13. Add the partner API, sandbox client, and signed webhooks.
14. Complete portfolio documentation, deployment, and demonstration materials.



## 34. Authentication strategy and public-profile delivery

### Authentication decisions

- Website: retain ASP.NET Core Identity and database-backed revocable cookie sessions. JWT is not a replacement for this browser session design.
- Production: HTTPS, Secure/HttpOnly/SameSite cookies, CSRF protection, explicit trusted origins, secure persistent Data Protection keys, and session expiration/revocation tests.
- Reviewers/administrators: require MFA before public production deployment. MFA is planned, not implemented by this milestone.
- Partner backends: OAuth 2.0 Client Credentials with scoped, short-lived, audience-restricted access tokens from an established identity provider. Do not build a custom OAuth authorization server.
- User-delegated integrations: OpenID Connect for login and OAuth Authorization Code with PKCE for delegated access, only when this capability is needed.
- JWT is an optional token format; validate issuer, audience, signature, lifetime, and permitted algorithms if selected. Opaque access tokens are also possible.
- API keys are a deferred sandbox-only option, not a second production partner authentication system. Never embed client secrets in browser code.
- Every request must enforce organization boundaries and permissions; partner requests additionally enforce scopes. Properties are resources within organizations, not the isolation boundary.

Acceptance criteria for the partner/security milestones: deny expired/invalid/wrong-audience tokens, insufficient scopes, cross-organization access, and revoked sessions; require MFA for privileged access; cover credential rotation and revocation using the selected identity provider. These future capabilities must not be advertised as already implemented.

### Public provider profiles and precise badges (current milestone)

- Approval does not automatically publish personal profile information. An active Owner explicitly publishes/unpublishes the saved profile using CSRF and its exact current ETag.
- Public eligibility requires a complete profile, active categories, a verified active Owner, the active Approved application, and all current policy-required checks from that approval.
- Public GET /api/v1/providers supports bounded pagination and category filtering; GET /api/v1/providers/{providerId} returns only the allowlisted public fields. Ineligible/private profiles return 404.
- Material profile changes revoke checks and clear publication consent. Renewed approval requires explicit republication after such edits.
- Expired/revoked checks, inactive categories, or a replacement application hide the profile on subsequent requests, without relying on cleanup jobs or cached approval flags.
- Badges name only the check performed (identity evidence, business-registration evidence, trade-qualification evidence), state simulation explicitly, and show expiration. Never imply a background check, legal certification, guaranteed safety, or service quality.
- Evidence, filenames, hashes, storage keys, reviewer identities/reasons, memberships, emails, and internal organization IDs remain private.
- Existing loaded pages may remain visible until refreshed; responses and server-side fetches are no-store. Booking must independently recheck eligibility when implemented.
- Automated API tests, frontend build/typecheck, and proxy smoke tests are required. Real Docker/PostgreSQL and interactive browser validation remain a separate pending acceptance check.


## 35. Service catalog implementation decisions

Core service listings and initial public discovery are implemented. Owners manage listings; Staff read only. Saved offers require title, description, active provider category, integer centavo price (PHP only), duration in 15-minute increments, and service area.

Lifecycle: Draft -> Published -> Draft, with terminal Archived. Edits reset Published offers to Draft. Publish requires an explicitly published and currently eligible provider profile; every public read rechecks verification, category membership, and status. Changes and audit records save atomically with exact ETag/CSRF protections. Archive preserves history for future booking references; hard deletion/restoration is not implemented.

Public /services pages and /api/v1/services endpoints support bounded paging, literal text and area search, category/provider filters, centavo price ranges, and deterministic title/price sorts. All returned offers already require current verification. Badge-specific filtering, full-text/fuzzy/geographic search, multi-currency, availability, booking, payments, and reviews remain deferred. Safe service images are implemented in section 36.

CATALOG-01 now includes the core text/pricing lifecycle and the safe image workflow in section 36; never reuse private evidence as public images. Real Docker/PostgreSQL migration and interactive browser testing also remain pending. See docs/architecture/service-catalog.md for exact validation, permissions, APIs, and walkthrough.


## 36. Safe public service-image uploads

Delivered: active Owner uploads/removes one JPEG/PNG at a time with CSRF and exact listing ETag; Staff views only. Files are at most 5 MiB (6 MiB multipart), 4096 pixels per side/12 MP, static only, and at most five per listing. Required alt text and demo acknowledgment prohibit intentional private evidence reuse.

Scan input and processed output; decode actual pixels, reject animation, resize within 1600 pixels, copy into a new metadata-free image, and encode PNG. Only processed bytes go into a distinct private service-images bucket with validated random keys; originals/filenames/metadata are discarded. Dedicated image credentials are required outside Development.

Image mutations return the listing to Draft; explicit republication is required. Archive hides all images and queues deletion. Member-only previews and anonymous public image delivery are distinct; public delivery rechecks current listing/provider eligibility and deletion after storage retrieval, verifies size/hash, and uses no-store/nosniff/sandbox headers.

Deletion reservations persist before object writes. Failed/stale/revoked/cancelled uploads and removed/archived images are cleaned by a separate retrying worker; never delete evidence through image cleanup. Compose enables the worker; host runs opt in with ServiceImages__CleanupEnabled=true. Multi-worker leases, production resource isolation/credential/IAM/encryption review, abuse moderation, and real Docker/PostgreSQL/MinIO/ClamAV/browser acceptance remain pending.

Catalog editor uploads/removes and previews processed images; public directory/detail pages display them with accessible alt text without an optimization cache that bypasses eligibility. API contract, migration, reviewable SQL, tests and walkthrough are included. See docs/architecture/service-images.md.

Next milestone: customer properties and provider availability before booking. Booking is still not implemented; profile/avatar image uploads are a separate feature.

## 37. Customer properties, provider availability, and booking implementation

Delivered: private customer-owned property create/list/read/edit/archive with validated structured addresses, exact ETag/CSRF, retained records, and /properties UI. Any human account may act as a customer; booking requires verified email. property_id remains a physical resource identifier, never an organization/tenant boundary. Provider members see addresses only through authorized bookings.

Delivered: Owner-configured IANA timezone, weekly windows, dated replacement exceptions/closures, breaks through multiple windows, before/after buffers, minimum notice, 30-day horizon, and public UTC slot discovery. One shared capacity per provider organization across services/staff. The initial /availability editor uses JSON windows with examples. Ambiguous DST times and appointments crossing an offset change are conservatively unavailable; existing reservations survive schedule edits.

Delivered: pending requests reserve capacity; server captures immutable price/duration/address/timezone/buffer snapshots. Database schedule-version compare-and-swap and transaction rollback prevent overlapping concurrent reservations. Property/service version changes serialize snapshots against edits. Pending expiry is configurable and capacity releases immediately at deadline. Compose enables a retrying expiry sweep for persisted state/history/notifications. Owner and active Staff can manage booking lifecycle; customers cancel their own pending/confirmed requests before start. Decisions require exact ETag/CSRF and record actor, time, status, reason atomically with private booking notices and encrypted queued email.

Delivered pages: /properties, /availability, service detail booking request, /bookings with customer/provider views, decisions, private address snapshot, history, and latest notices. Booking and provider-member access is rechecked independently of public verification, so existing bookings remain visible when checks expire while revoked staff loses access immediately.

See docs/architecture/properties-availability-bookings.md and docs/api/milestone-b.openapi.json for environment settings, exact APIs, concurrency protocol, and Postman walkthrough. Real Docker/PostgreSQL concurrent-request, SMTP, and interactive browser validation remain pending; automated SQLite/proxy/build results do not claim those checks. Preserve the same serialization protocol in all future reservation-writing paths.

Deferred: visual calendar UX, geographic service-area validation, human booking idempotency/rescheduling, multi-capacity/staff assignment, booking notice read tracking, payments/disputes, and partner OAuth/idempotency/webhooks. Completed-booking reviews are implemented in section 38.

## 38. Customer ratings and completed-booking reviews

Verified-email customers can publish exactly one 1–5 rating and written review for their own Completed booking. Active provider-organization members cannot self-review; all other booking statuses are rejected. Public-feedback acknowledgement is required, and users are warned not to include personal information. Reviews are immutable in this release; providers have no deletion/hiding/editing capability.

The booking review panel reads server-authoritative eligibility. Eligible published provider profiles show paginated customer feedback, total review count, and calculated average ratings; empty profiles have no invented rating. Public responses use a generic customer label and omit booking/customer/property/organization identifiers. Provider visibility is checked before and after reading reviews. Hidden reviews are excluded from totals, but moderation tools are not yet implemented.

Cookie sessions, CSRF, exact frontend origin checks, bounded paging, safe plain-text rendering, database-enforced one-review-per-booking uniqueness, and additive migrations are included. See docs/architecture/customer-reviews.md and docs/api/customer-reviews.openapi.json for contracts and acceptance checks. Reviews publish immediately for the fictional portfolio demo; review images, editing, reporting, production abuse controls, and administrator moderation remain later milestones. Provider replies are implemented in section 39. Interactive browser validation remains a separate user acceptance step.

## 39. Provider responses to customer reviews

Active Owners and Staff with verified email can publish one immutable public reply to a Published review of their organization's Completed booking. Booking customers cannot respond to their own reviews, including after being invited as Staff. Responses require validated written feedback and public-publication acknowledgement. Providers cannot modify or delete the customer's review/rating, and reply creation does not affect rating averages or review counts.

The database enforces one reply per review. Reply and private organization audit event persist atomically; competing submissions return 409 without losing the winning response or leaving a losing audit event. Cookie/CSRF/exact proxy-origin rules are preserved. Participant review reads include server-authoritative canReply/replyReason; public replies expose only feedback, timestamp and the generic Provider response label, never responder identity or internal identifiers.

Booking review panels and public provider profiles display replies in the current visual style. Hidden reviews or ineligible/unpublished provider profiles hide their public responses; authorized participants retain history. See docs/architecture/provider-review-replies.md, docs/api/provider-review-replies.openapi.json and the additive migration SQL. Reply editing/deletion, reporting/moderation tools, production abuse controls and notifications remain deferred. Manual browser and real-stack authorized submission validation remain separate acceptance steps.
