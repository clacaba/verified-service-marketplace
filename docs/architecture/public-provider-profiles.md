# Public provider profiles and precise badges

Status: API, explicit publication, directory/detail frontend, migration, and automated tests implemented. Real PostgreSQL/Docker and interactive browser validation remain pending.

## Try the workflow

1. Start the local stack and register a Provider; verify the email through Mailpit.
2. Create an organization, complete its profile, upload fictional evidence, and submit.
3. A separate operator-provisioned verified Reviewer claims and approves the submission, selecting every required check.
4. Approval alone leaves the profile private. The Owner opens /provider-profile?organizationId=... and clicks Publish approved profile after saving all edits.
5. Open /providers or /providers/{providerId} without signing in. The profile ID comes from the private profile response, not the organization ID.
6. Unpublish from the same Owner editor. Material edits clear publication, revoke checks, and require a new review and explicit republication.

## API contract

- GET /api/v1/providers?page=1&pageSize=20&categoryId={optionalUUID}: anonymous paginated discovery; max pageSize 100, max page 100000, alphabetical displayName/ID ordering.
- GET /api/v1/providers/{providerId}: anonymous allowlisted public detail, or indistinguishable 404 for private/ineligible/missing profiles.
- POST /api/v1/organizations/{organizationId}/provider-profile/publication: active Owner only, cookie session, X-CSRF-Token, exact quoted profile If-Match; JSON { "published": true } or false. Returns { id, publishedAt } and new ETag.
- Missing If-Match 428; stale/wildcard 412; Staff 403; outsider 404; missing prerequisites 409; invalid CSRF 400.
- Unpublication remains allowed when checks have expired. No new schema exposes emails, private documents, filenames, storage keys, hashes, application IDs, reviewer identities/reasons, membership lists, or organization IDs.
- Publication is persisted via nullable ProviderProfile.PublishedAt, default null for existing profiles. Migration: PublicProviderPublication; SQL: infrastructure/public-provider-publication.sql.

## Visibility and badges

Eligibility is evaluated in database queries before pagination, not from cached approval flags: complete biography/area/categories, active categories, verified active Owner, active Approved application, and every current policy-required check from that application's approved decision. Public GETs require PublishedAt too. A follow-up eligibility check fails closed when verification changes during projection.

Individual providers need Identity; Business providers additionally need BusinessRegistration; configured trade categories additionally need TradeQualification. Each badge names only that evidence review, labels simulation, and gives UTC expiration. There is no universal trust score, real background-check badge, licensing certification, or safety guarantee.

Expired/revoked checks, inactive categories, withdrawn Owner verification/membership, or a replacement application hide the public profile on subsequent requests. Expiry needs no background cleanup job. If the profile is unchanged and consent remains set, a subsequent valid renewal approval may restore visibility; material edits always clear consent. Automatic safety suspension administration is not implemented; future suspension must revoke eligibility server-side.

Responses and Next server fetches use no-store, public links disable prefetch, and the pages disclose fictional data. Already loaded browser pages are not remotely erased; refresh to recheck. Booking will independently revalidate provider eligibility when implemented.

## Authentication boundary

Website sessions remain cookie-based and revocable. OAuth Client Credentials, JWT/opaque partner access tokens, delegated OIDC/PKCE, and administrator MFA are future milestones, not delivered here. See section 34 of the master plan.

## Validation

Automated tests cover publication consent, exact public response fields, authorization/CSRF/ETags, expired/revoked checks, active category and Owner verification, active application, material edits, paging/filtering, and demo wording. Proxy smoke exercises real Next server pages and route allowlisting against a mock backend, not a complete browser journey. PostgreSQL SQL execution, real email delivery, MinIO/ClamAV, and interactive frontend acceptance still require Docker and browser testing.
