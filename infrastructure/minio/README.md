# Local MinIO source build

The old `minio/minio` image could not be pulled from Docker Hub, and the tested
Quay release references were unavailable too. Compose now builds the storage
service from official MinIO source revision
`7aac2a2c5b7c882e68c1ce017d8256be2feea27f` with Go 1.26.8.
Compilation is bounded to two Go workers to reduce local memory pressure.

Run `docker compose up --build -d` from the repository root. The first build
downloads the Go toolchain image and dependencies and may take several minutes.
Subsequent starts reuse the built image. Existing `.env` credentials, storage
ports, S3 endpoint, and `storage-data` volume are unchanged. Do not remove volumes
to troubleshoot an image pull or build problem.

Check `docker compose ps` and `http://localhost:9000/minio/health/ready`.
The container runs as uid/gid 10001. Pre-existing volumes from a root-running
MinIO image may need an explicitly reviewed permissions migration; do not erase
their contents or apply broad host permission changes.

This is for fictional local-development data only. The MinIO community repository
is archived and unmaintained; compiling it does not make its dependencies current
or provide a security-support policy. Select a maintained S3-compatible storage
provider before production, review its licensing/security, and validate the
private evidence and processed-image workflows against it. MinIO is AGPL-3.0;
source is at https://github.com/minio/minio and its license remains applicable.
Container base tags are pinned versions but not immutable digests; production
requires reviewed image digests and vulnerability scanning.

## Local startup validation (2026-10-01)

The storage, API, and frontend container images built successfully. All six
Compose services started; PostgreSQL, ClamAV, Mailpit, and storage passed their
container health checks. The frontend, API database-readiness endpoint, Mailpit,
storage readiness endpoint, and storage console returned HTTP 200.

The root `global.json` permits newer .NET 10 SDK feature bands (`latestFeature`)
so the current SDK container can build the application. This does not change
the target framework. Full registration, verification uploads, and booking
acceptance testing remain separate from these startup checks.
