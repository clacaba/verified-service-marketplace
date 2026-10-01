# Verified Service Marketplace

An API-first, multi-organization home-services marketplace connecting customers
and partner companies with verified service providers.

## Project status

Planning and initial development.

## Planned technology

- Next.js, React, and TypeScript
- ASP.NET Core and C#
- PostgreSQL and Entity Framework Core
- OAuth 2.0 partner API
- OpenAPI and signed webhooks

## Documentation

- [Full product and architecture plan](docs/service-booking-marketplace-plan.md)

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
