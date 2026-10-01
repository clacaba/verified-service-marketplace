# Local-development compatibility build. Upstream community MinIO is archived;
# this is not a maintained production storage distribution.
FROM golang:1.26.8-bookworm AS build
ARG MINIO_REVISION=7aac2a2c5b7c882e68c1ce017d8256be2feea27f
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2 GOFLAGS=-p=2
RUN go install -trimpath -ldflags="-s -w" github.com/minio/minio@${MINIO_REVISION}

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 minio && useradd --uid 10001 --gid minio --no-create-home minio \
    && mkdir /data && chown minio:minio /data
COPY --from=build /go/bin/minio /usr/local/bin/minio
USER 10001:10001
EXPOSE 9000 9001
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=12 \
    CMD curl --fail --silent http://localhost:9000/minio/health/ready || exit 1
ENTRYPOINT ["minio"]
