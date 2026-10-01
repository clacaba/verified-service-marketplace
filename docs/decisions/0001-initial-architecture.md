# ADR 0001: Initial architecture

Date: 2026-10-01

Status: Baseline for master-plan decisions; runtime versions and hosting remain proposed.

## Context

The [master plan](../service-booking-marketplace-plan.md) requires an independent, API-first, multi-organization product and a complete provider-verification slice before marketplace expansion.

## Decisions

- Maintain this independent repository and product identity.
- Start with an ASP.NET Core modular monolith. Modules own their rules and persistence access; the API application composes them.
- Use Next.js, React, and TypeScript for customer/provider and administrative interfaces.
- Use PostgreSQL with Entity Framework Core. Organizational resources carry `organization_id`; queries and mutations enforce active membership and permissions. Customer resources and bookings also enforce owner/participant rules.
- Use ASP.NET Core Identity for human credentials. Proposed browser sessions use HttpOnly cookies, explicit CSRF defenses, expiry, and revocation. Partner applications use scoped OAuth client credentials, separately from human sessions.
- Store verification evidence in private object storage; issue short-lived access only after permission checks and audit document access.
- Publish notifications and partner events through a transactional outbox, processed by retryable background jobs.
- Expose versioned REST endpoints and signed partner webhooks. Preserve compatibility within a major API version.

## Runtime and hosting proposal

Target .NET 10 LTS and Next.js 16 with a supported Node.js LTS runtime. Pin exact SDK/package/container versions during scaffolding and verify compatibility. The inspected workstation has .NET SDK 10.0.103 and Node.js 20.18.0; these are observations, not production pins. Upgrade the frontend runtime to a supported LTS before scaffolding.

Use Docker Compose for local PostgreSQL and a private S3-compatible storage service. Docker was unavailable during the initial inspection; container startup has not been tested. Production hosting, email provider, region, backups, and retention remain open. Deployment is outside this document's scope.

References: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [Next.js 16 requirements](https://nextjs.org/docs/app/guides/upgrading/version-16).

## Consequences

The first feature can be implemented in one backend deployment while retaining module boundaries. Organization isolation requires explicit tests for every protected resource. Outbox processing adds operational complexity but prevents committed decisions from losing notifications. No proprietary code or real identity documents are imported.
