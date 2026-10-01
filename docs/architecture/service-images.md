# Safe service-listing images

Status: Owner upload/removal, static image processing, separate private storage, authorized delivery, cleanup, frontend display, and automated tests implemented. Real PostgreSQL/MinIO/ClamAV and interactive browser validation remain pending.

## Rules and meaning

A service image is a fictional, non-sensitive photo illustrating a service offer. It is not an identity document, verification evidence, profile avatar, or certificate. Upload requires active Owner membership, X-CSRF-Token, and the exact current listing If-Match. Staff can view saved processed images privately but cannot upload/remove them.

- One file per request, maximum 5 MiB; multipart request maximum 6 MiB.
- Declared MIME and detected content must match JPEG or PNG. SVG, PDF, GIF, WebP, MIME spoofing, broken images, and animation are rejected.
- Maximum source dimension 4096 per side and 12,000,000 pixels. PNG animation-control/frame chunks are rejected before decoding; frame-count validation also runs after decoding.
- Actual ImageSharp identification and pixel decoding, not signature checks alone. Decode with skipped metadata and bounded frames; resize within 1600 × 1600 without upscaling.
- Copy decoded pixels into a fresh image and re-encode PNG. No original file, filename, EXIF/GPS, ICC, XMP, IPTC, or PNG text is saved or returned. EXIF-based orientation is deliberately not retained; rotate photos correctly before upload.
- Scan both original input and processed output with ClamAV. Scanner/processor/storage failure is fail-closed; malware rejection is 400 and unavailable dependencies return 503.
- Two concurrent decoding operations per process, bounded ImageSharp allocator/parallelism, and a 30-second cooperative processing timeout. Upload rate limiting shares the existing per-account evidence policy (30/minute). These bounds are not a substitute for production worker/container isolation and gateway concurrency controls.
- Maximum five current images per listing. Required altText is 1–150 trimmed characters for accessibility. demoAcknowledged=true confirms fictional non-sensitive service imagery.
- Upload/removal changes the listing to Draft, increments its concurrency version, and records an audit event atomically. Republish separately. Archiving soft-deletes all image records and queues their processed objects for deletion; archived images cannot be retrieved.

## API and Postman

Private base: /api/v1/organizations/{organizationId}/services/{serviceId}.

| Method | Suffix | Result |
| --- | --- | --- |
| POST | /images | Multipart file, altText, demoAcknowledged=true; 200 updated listing + ETag |
| DELETE | /images/{imageId} | 200 updated Draft listing + ETag |
| GET | /images/{imageId}/content | Active member-only processed PNG, including Draft listings |

Upload/removal require listing If-Match and CSRF. Missing/stale preconditions return 428/412. Staff mutations return 403; outsider, deactivated member, wrong organization/service/image relationships, or removed images return 404. Archived mutations or a sixth image return 409. Invalid file/form is 400; excessive size is 413. Recheck Owner membership after storage and before metadata commit.

In Postman, use Body -> form-data: file (File), altText (Text), demoAcknowledged (Text, true). Do not manually set multipart Content-Type; let Postman generate its boundary. Keep session cookies and CSRF header; get listing ETag first and use the returned new ETag afterward.

Public GET /api/v1/services/{serviceId}/images/{imageId}/content needs no login, but the image must belong to that published, currently eligible listing. Expired/revoked checks, unpublished profiles/listings, category removal, archive, or image deletion make it unavailable on subsequent requests. Storage objects are never directly public.

Service summaries now include images with id, altText, width, height, size, createdAt, contentType=image/png, and a relative URL (member-only in private responses, eligibility-gated in public responses). No object key, hash, original filename, external URL, or evidence ID is exposed. Listing inputs cannot attach arbitrary image identifiers.

After object retrieval, recheck deletion and either current membership or public eligibility. Verify stored size and SHA-256 integrity. Return image/png with no-store, nosniff, sandbox CSP, no-referrer, and fixed inline filename. No ranges. Use normal img elements rather than Next image optimization/CDN caches that could bypass live visibility checks. Already downloaded pixels cannot be recalled from someone who saved them.

## Storage and cleanup

ServiceImages bucket defaults to service-images and must differ from Storage:Bucket (verification-evidence). IServiceImageStore is distinct from IEvidenceStore. It reuses private S3 transport mechanics, never evidence objects; processed random keys are validated against processed/{UUID-N}.png. Buckets have no anonymous policy; Development creates the dedicated bucket on first operation.

Development uses existing Storage endpoint/credentials. Optional ServiceImages:Endpoint overrides the endpoint. Outside Development, configure dedicated ServiceImages:AccessKey and ServiceImages:SecretKey scoped only to the image bucket, HTTPS, encryption, private ACL/ownership, and production IAM policy review. Use ignored environment files, .NET user-secrets, or a managed secret store, never committed secrets.

Persist a deletion reservation before writing any object. Successful metadata/version/audit commit removes it atomically. Failed, revoked-owner, cancelled, or stale uploads leave a reservation due after one hour. Removal/archive records immediate deletion jobs in the same transaction as hiding metadata. Cleanup retries with backoff; it never touches verification evidence.

ServiceImages:CleanupEnabled defaults false for host runs; set ServiceImages__CleanupEnabled=true. Development Compose sets it true. Worker runs every minute, at most 100 jobs per sweep. This initial worker assumes one cleanup instance; durable distributed leases, dead-letter alerts, and abandoned Draft listing retention remain production follow-ups. Soft-deleted metadata remains for audit/history, but deleted stored bytes are not recoverable without separate storage backups.

Persistence: ServiceImages table has a restricted listing FK, UUID ID, alt text, random key, integrity hash, normalized size/dimensions, creation/deletion timestamps. ServiceImageDeletions is the durable cleanup queue. ServiceImages migration and infrastructure/service-images.sql are included.

## Try the UI

Apply migrations/rebuild Development Compose, open /organizations -> Service catalog, select a saved listing, and upload a JPEG/PNG with its accessible description and explicit demo acknowledgment. Save text edits first. Inspect the private processed preview, then republish and browse /services. Removing photos or archiving hides them immediately and queues cleanup.

## Dependency and validation

Pinned SixLabors.ImageSharp 3.1.12 (latest stable 3.1 patch available when implemented); restore reports no package vulnerability warnings. Review its bundled Six Labors Split License and commercial-use requirements before changing distribution/use: https://sixlabors.com/posts/license-changes/. No license purchase or paid account was created. Future upgrades need codec/security regression checks.

Processing choices follow the maintainer's identification, metadata, frame, allocator and request-limit guidance: https://docs.sixlabors.com/articles/imagesharp/security.html. Explicit PNG animation rejection does not rely on metadata behavior.

Automated tests use actual image decoding/encoding and isolated scanner/store doubles: metadata removal, resizing, MIME spoofing, malformed/animated/oversized files, image count, membership deactivation, CSRF/ETag, eligible public access, response privacy, integrity, archive/deletion, orphan reservation and retry. Next production-server smoke verifies binary multipart/PNG forwarding, size/origin checks, route allowlists, and image-enabled pages. It is not an interactive browser or real MinIO/ClamAV run.

Next: customer properties and provider availability, then booking. Provider profile/avatar uploads, cropping/reordering/edit-alt commands, multiple renditions, moderation/takedown UI, and production image-processing isolation remain separate follow-ups.
