# Local development foundation

Status: Foundation, authentication, organizations, profile drafts, and private evidence uploads/submissions implemented. Administrative review/decisions and corrected drafts are implemented; explicit Owner publication and public profiles with precise simulated badges are implemented.

## Prerequisites

Use .NET SDK 10.0.103 (or a compatible patch), Node.js 22 LTS or 24 LTS, npm, and Docker Desktop with Compose. Docker is required for the complete local stack. No software is installed system-wide by this scaffold.

## Start the complete local stack

From the repository root:

```powershell
Copy-Item .env.example .env
npm install
docker compose up --build
```

Open http://localhost:3000. The page displays whether the API responds. API liveness: http://localhost:5080/health/live. Database readiness: http://localhost:5080/health/ready. MinIO console: http://localhost:9001. Local example credentials are in your ignored .env file. Private evidence uses the verification-evidence bucket created on first storage operation in Development. Do not enable anonymous access. ClamAV must be ready for uploads to succeed; first signature download may take time.

Use `docker compose down` to stop while preserving data volumes. This Compose configuration is for local development only.

## Run applications on the host

Start dependencies with `docker compose up -d postgres storage mailpit scanner`. In a PowerShell terminal, set the connection string using the values from .env:

```powershell
$env:ConnectionStrings__Marketplace = 'Host=localhost;Database=marketplace;Username=marketplace;Password=local-development-only'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Email__Enabled = 'true'
# Match these local-only credentials to your ignored .env values:
$env:Storage__AccessKey = 'marketplace-local'
$env:Storage__SecretKey = 'local-storage-development-only'
$env:Evidence__RetentionEnabled = 'true'
$env:ServiceImages__CleanupEnabled = 'true'
dotnet tool restore
dotnet ef database update --project apps/api
dotnet run --project apps/api --urls http://localhost:5080
```

In a second terminal, run `npm run dev`. The frontend performs a server-side API request; no browser JWT or database credential is exposed. Override `API_BASE_URL` only on the server when necessary.

## Verify

```powershell
dotnet build Marketplace.slnx
dotnet test Marketplace.slnx
npm run typecheck
npm run build
```

The tests distinguish liveness from readiness and use in-memory SQLite for authentication integration tests. PostgreSQL is the application database. Its initial Identity/session/email migration is included, with a reviewable SQL script at `infrastructure/initial-identity.sql`. Development Compose applies migrations on startup; host runs use the explicit migration command above. Production migrations must be applied separately. Real PostgreSQL and SMTP integration remain unverified on workstations without Docker.

## Try authentication

1. Start the stack and open http://localhost:3000/register.
2. Register as Customer or Provider with a password of at least 12 characters containing upper/lowercase letters, a number, and a symbol.
3. Open the local Mailpit inbox at http://localhost:8025 and follow the email verification link.
4. Click Confirm email address, then sign in at `/login`.
5. View your verification status at `/account`, resend verification if needed, and sign out.

Accounts can sign in before email verification to prepare onboarding drafts. Evidence submission requires verified email. Booking checks arrive with booking. Administrative registration is unavailable. Create organizations at /organizations and follow the profile and verification links.

Browser calls use an allowlisted Next.js BFF route, no-store responses, HttpOnly cookies, an Origin check, and a CSRF token fetched before each mutation. All authentication POSTs, including login and registration, require CSRF. Verification tokens expire after two hours and are single-use. Sessions expire after eight hours; logout revokes the current session in the database, preventing cookie replay. Login failures trigger account lockout; authentication traffic has a conservative 20-request/minute per-backend-IP limit. Requests proxied by the BFF currently share its IP, so trusted per-user/client rate partitioning is required before public deployment.

Mailpit captures local messages only. The retrying email worker is opt-in through `Email__Enabled`; it stores protected email bodies transactionally with registration and removes bodies after successful delivery. Use a single worker instance for this initial local implementation. Lease-based processing, production SMTP credentials/TLS, retention cleanup, and secure persistent Data Protection key management require a production configuration pass. Compose persists keys in a named volume; losing keys invalidates pending tokens and cookies. HTTP cookies are permitted only in Development/Testing; production cookies require HTTPS. Set `WEB_ORIGIN` and `Web__PublicOrigin` to the actual trusted frontend origin when changing ports or deploying.

## Architecture

One Next.js application initially hosts the public/customer/provider surfaces and will later host authorized admin routes. A second frontend can be extracted if operational requirements justify it. Module folders remain planning boundaries until actual module implementations are introduced. The backend is a modular monolith composition root with PostgreSQL integration and problem-details error handling.

Dependencies are pinned in package manifests and lockfiles. Container tags are explicit development defaults, not immutable digests; pin reviewed digests before production deployment.


## Try evidence submission

See [private verification evidence](private-verification-evidence.md) for the complete walkthrough, fictional evidence rules, retention defaults, short-lived private downloads, and remaining production hardening. Backend tests use SQLite and scanner/storage doubles; actual Docker/PostgreSQL/MinIO/ClamAV validation remains pending.


## Try administrator review

Use a separate registered and verified account. A trusted operator must grant Reviewer access using the command in [administrator review and revisions](administrator-review-and-revisions.md). The shared /login page then leads to /account → Administrator review queue. No default administrative account or credential is seeded. See that guide for revocation, claims, decisions, notifications, correction drafts, and renewal.



## Try public profiles

After approval, open the Owner profile editor, save any changes, and explicitly publish the approved profile. Browse `/providers` anonymously. See [public provider profiles](public-provider-profiles.md) for prerequisites and badge semantics. New database migration `PublicProviderPublication` defaults existing profiles to private; run the usual host migration command or rebuild the Development Compose API to apply it.



## Try service listings

Apply the ServiceCatalog migration through normal Development Compose startup or the host migration command above. Open /organizations -> Service catalog, save an offer, and publish it only after its verified provider profile is explicitly published. Browse /services anonymously. See [service catalog](service-catalog.md) for rules, validation, filters, and remaining image work.

## Try safe service images

Apply the ServiceImages migration with the usual host migration command, or rebuild the Development Compose stack. Select a saved listing in /catalog, upload a fictional JPEG/PNG with alt text and demo acknowledgment, and republish the offer to make the processed image visible publicly. Compose enables the separate image cleanup worker; host runs use ServiceImages__CleanupEnabled=true. Development creates a distinct private service-images bucket using local Storage credentials. Outside Development, configure dedicated ServiceImages__AccessKey and ServiceImages__SecretKey for that bucket and HTTPS. See [safe service images](service-images.md) for limits, workflows, and remaining production checks.
