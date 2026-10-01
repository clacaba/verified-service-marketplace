# Administrator review, decisions, and corrected submissions

Status: Reviewer access, queue/details, claimed reviews, decisions, in-app notifications, queued owner emails, corrected drafts, and invalidated-approval renewal implemented. Public profiles/badges are next.

## Reviewer access and shared login

Reviewer is a separate platform permission, not a public registration account type or an organization Owner role. It is stored in the database, requires a verified email, and is checked for every privileged request. A reviewer uses the existing /login page; there are no default admin credentials.

To try it locally, register and verify a separate Customer account (for example reviewer@example.test) through the existing website and Mailpit. Then a trusted server operator explicitly grants access:

```powershell
# Complete stack already running, migrations applied:
docker compose exec api dotnet Marketplace.Api.dll --grant-reviewer reviewer@example.test

# Revoke using the same controlled channel:
docker compose exec api dotnet Marketplace.Api.dll --revoke-reviewer reviewer@example.test
```

For host development, use the API's configured database environment and run:

```powershell
dotnet run --project apps/api -- --grant-reviewer reviewer@example.test
```

The command changes an existing account only, rejects grants to unverified accounts, and records a platform access audit atomically. It does not create accounts/passwords or run a public provisioning endpoint. No account was granted privileges automatically during implementation. Refresh /account to see the review-queue link. Revocation immediately removes review and reviewer-document access without requiring logout.

Only trusted server/database operators can run these commands. Production needs authenticated operator attribution, MFA, access review, and deployment-specific administrative controls.

## Review workflow

1. As Provider, complete a profile, upload fictional evidence, and submit.
2. As Reviewer, open /admin/verifications. The default queue contains Submitted and UnderReview, ordered by submission time then ID, with bounded pagination.
3. Inspect the immutable submitted profile/evidence and download available private documents using session-bound grants.
4. Claim the review. This changes Submitted to UnderReview, assigns the reviewer, records an audit event, and grants a 30-minute lease.
5. Record Approved, Rejected, or AdditionalInformationRequired. The current assigned reviewer and exact ETag are required.
6. Providers see status/decision history and notifications at /notifications. Verified Owners receive a queued generic email linking to their private application.
7. After rejection/information request, the Owner creates a linked correction draft, revises the profile as needed, uploads fresh fictional evidence, and resubmits.

A review held by another active reviewer cannot be taken before its lease expires. An expired lease, or one held by a revoked/unverified reviewer, can be reclaimed through start-review. A reviewer cannot decide after lease expiry without reclaiming. There is no live reassignment UI or automatic approval.

## Authorization, validation, and concurrency

- Customer/Provider/Staff/Owner access alone cannot grant Reviewer permissions.
- Reviewers cannot review any organization where they have a current or historical membership. Such applications are hidden from their queue. Organization members can still read their own evidence using normal member permissions.
- Platform reviewers may inspect/download non-Draft applications only; provider drafts remain private.
- All writes require session + CSRF; the browser proxy also checks origin. Private responses are no-store.
- GET detail and successful mutations return a quoted UUID ETag. Missing If-Match is 428; stale writes are 412; invalid lifecycle/claim transitions are 409.
- Rejection and information requests require a trimmed provider-facing reason of 1–2,000 characters.
- Approval requires exactly the submitted required checks, no duplicates or invented checks, current evidence, active categories, and profile details matching the submitted snapshot. Changed configured evidence rules require a corrected submission before approval.
- Checks are always simulated=true; callers cannot override simulation. Expiry defaults to the earliest relevant evidence expiry and must be future/not exceed retained evidence.
- Approval is serialized with profile changes and document retention through concurrency tokens, so races cannot approve changed or expired evidence.

## Transactional decisions and notifications

Each decision commits application status/version, reviewer/time/prior-new status, reason, selected simulated checks, organization audit, recipient notifications, and encrypted email-queue records together. The shared email worker is the transactional delivery outbox; SMTP runs after commit and retries. A racing decision rolls all of these changes back, rather than sending an email for a rejected transaction.

In-app notifications are visible only to their recipient with an active membership in the source organization. Mark-read is recipient-only, idempotent, and CSRF protected. Removed staff lose notification access immediately. Queued emails contain no evidence, private decision reason, object keys, or access grants. Delivery is retryable, not an exactly-once SMTP guarantee; use the existing single-worker restriction and production delivery/lease hardening.

## Immutable history and renewal

Correction creates a NEW Draft linked by parentApplicationId and increments revisionNumber. The prior application, evidence, snapshot, and decision remain unchanged except its active flag/version. Only one active application per organization and one child per parent are allowed by database constraints. Revision creation is transactional. No old object key is shared or file cloned; fresh uploads are required.

Submission numbering is 1 for the initial submission, 2 for its first correction, and so on. Older drafts/submitted evidence cannot be unlocked or overwritten by a correction.

Material changes to display name, type, biography, area, or category selection revoke the organization's current checks. Historical approval remains an approval decision, not current eligibility. The Owner can create a fresh linked renewal draft when any required approved check is expired/revoked. An unchanged, fully current approval cannot be revised. Public eligibility and precise badge projection are the next feature; approval alone publishes nothing.

## Validation and limitations

Backend tests use SQLite and storage/scanner doubles. Tests cover explicit provisioning/revocation, queue authorization, CSRF, version/lease conflicts, self-review denial, decision validation, recipient privacy, renewal/check invalidation, immutable correction history, and a racing approval that rolls back checks/audit/notification/email together.

Frontend typecheck/build and an HTTP proxy smoke test cover the review/notification pages, allowlisted review/revision routes, PATCH forwarding, and earlier binary upload/download behavior. The PostgreSQL migration and incremental deployment SQL are included. Actual Docker/PostgreSQL/MinIO/ClamAV/Mailpit and interactive browser journeys still require verification.

Use fictional demo records only. Public badges, automatic expiry/suspension state transitions, production reviewer provisioning/MFA, distributed worker leases, notification cleanup, and partner event delivery are not implemented by this slice.
