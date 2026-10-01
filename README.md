# Verified Service Marketplace

An API-first, multi-organization home-services marketplace connecting customers
and partner companies with verified service providers.

## Project status

Planning baseline and initial application foundation are available. The Next.js
frontend connects to the ASP.NET Core API; PostgreSQL readiness checks and local
Docker configuration are included. Registration, email verification, login,
logout, and database-backed sessions are implemented. Organizations, owner/staff memberships, invitations, and membership authorization
are implemented. Private provider profile drafts, active categories, and owner-only
editing with version-conflict protection are available. Private evidence uploads, malware scanning, session-bound downloads, and immutable
verification submissions are implemented for fictional demo files. Administrator
review and verification decisions are next.

## Local development

See [local development instructions](docs/architecture/local-development.md) for
prerequisites, Docker startup, host commands, and verification.

```powershell
Copy-Item .env.example .env
npm install
docker compose up --build
```

Open http://localhost:3000. Docker Desktop is required for this command.
Register at `/register`, inspect verification emails at http://localhost:8025,
and sign in at `/login`. See the local development guide for database migrations
and authentication configuration.

## Planned technology

- Next.js, React, and TypeScript
- ASP.NET Core and C#
- PostgreSQL and Entity Framework Core
- OAuth 2.0 partner API
- OpenAPI and signed webhooks

## Documentation

- [Full product and architecture plan](docs/service-booking-marketplace-plan.md)
- [Product brief](docs/requirements/product-brief.md)
- [MVP requirements and acceptance criteria](docs/requirements/mvp-requirements.md)
- [Implementation checklist](docs/requirements/implementation-checklist.md)
- [Initial architecture decisions](docs/decisions/0001-initial-architecture.md)
- [Milestone A domain diagram](docs/diagrams/milestone-a-domain.md)
- [Milestone A wireframes](docs/architecture/milestone-a-wireframes.md)
- [Milestone A API contract draft](docs/api/milestone-a-contract.md)
- [Milestone A OpenAPI specification](docs/api/milestone-a.openapi.json)
- [Private verification evidence](docs/architecture/private-verification-evidence.md)
- [Provider profile drafts](docs/architecture/provider-profile-drafts.md)
- [Local development foundation](docs/architecture/local-development.md)

Product requirements, architecture decisions, API contracts, and diagrams will
be maintained under `docs/`.

## Repository layout

```text
apps/             Deployable frontend and backend applications
modules/          Backend business modules
tests/            Unit, integration, and end-to-end tests
docs/             Requirements, architecture, decisions, APIs, and diagrams
infrastructure/   Local and hosted infrastructure definitions
scripts/          Development and automation scripts
```

## Safety

Do not commit credentials, connection strings, API keys, real identity documents,
or production customer information. Public demonstrations must use fictional data
and sandbox integrations.
