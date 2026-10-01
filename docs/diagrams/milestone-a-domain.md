# Milestone A domain model

Status: Conceptual design before migrations. ASP.NET Core Identity supplies credential and token mechanics; this diagram shows product relationships rather than its complete framework schema.

```mermaid
erDiagram
    USER ||--o{ MEMBERSHIP : holds
    ORGANIZATION ||--o{ MEMBERSHIP : grants
    ORGANIZATION ||--o| PROVIDER_PROFILE : owns
    PROVIDER_PROFILE ||--o{ PROVIDER_CATEGORY : selects
    CATEGORY ||--o{ PROVIDER_CATEGORY : classifies
    PROVIDER_PROFILE ||--o{ VERIFICATION_APPLICATION : submits
    VERIFICATION_APPLICATION ||--o{ DOCUMENT : includes
    VERIFICATION_APPLICATION ||--o{ VERIFICATION_DECISION : records
    VERIFICATION_APPLICATION ||--o{ VERIFICATION_CHECK : establishes
    USER ||--o{ VERIFICATION_DECISION : reviews
    USER ||--o{ NOTIFICATION : receives
    ORGANIZATION ||--o{ INVITATION : issues
    USER ||--o{ SESSION : authenticates
    ORGANIZATION ||--o{ AUDIT_EVENT : scopes
    VERIFICATION_DECISION ||--o{ OUTBOX_MESSAGE : emits
    USER {
        uuid id PK
        string normalized_email UK
        boolean email_confirmed
    }
    ORGANIZATION {
        uuid id PK
        string name
        string type
    }
    MEMBERSHIP {
        uuid organization_id PK,FK
        uuid user_id PK,FK
        string role
        boolean active
    }
    PROVIDER_PROFILE {
        uuid id PK
        uuid organization_id FK,UK
        string provider_type
        string display_name
        string biography
        string service_area
        bigint version
    }
    CATEGORY {
        uuid id PK
        string name UK
        boolean active
    }
    PROVIDER_CATEGORY {
        uuid provider_id PK,FK
        uuid category_id PK,FK
    }
    VERIFICATION_APPLICATION {
        uuid id PK
        uuid provider_id FK
        uuid organization_id FK
        int submission_version
        string status
        datetime submitted_at
        bigint version
    }
    DOCUMENT {
        uuid id PK
        uuid application_id FK
        uuid organization_id FK
        string evidence_type
        string private_object_key
        string content_type
        bigint size_bytes
    }
    VERIFICATION_DECISION {
        uuid id PK
        uuid application_id FK
        uuid reviewer_id FK
        string previous_status
        string new_status
        string reason
        datetime decided_at
    }
    VERIFICATION_CHECK {
        uuid id PK
        uuid application_id FK
        string check_type
        string status
        boolean simulated
        datetime expires_at
    }
    INVITATION {
        uuid id PK
        uuid organization_id FK
        string recipient_email
        string token_hash
        datetime expires_at
        datetime accepted_at
    }
    SESSION {
        uuid id PK
        uuid user_id FK
        datetime expires_at
        datetime revoked_at
    }
    NOTIFICATION {
        uuid id PK
        uuid user_id FK
        string event_type
        datetime created_at
        datetime read_at
    }
    AUDIT_EVENT {
        uuid id PK
        uuid organization_id FK
        uuid actor_id
        string action
        uuid resource_id
        datetime occurred_at
    }
    OUTBOX_MESSAGE {
        uuid id PK
        uuid decision_id FK
        string event_type
        datetime created_at
        datetime processed_at
        int attempts
    }
```

## Invariants

- Normalized email is unique; memberships are unique per user/organization.
- One provider profile per provider organization initially. An individual provider has the same ownership boundary as a business.
- An application/document organization must match its provider organization; use composite constraints where appropriate.
- Allow only one active submitted/under-review application per provider. Submission versions are immutable.
- A decision requires the application's current concurrency version. Write its state change, checks, audit event, notifications, and outbox records in one transaction.
- Approval identifies individual checks; public badges require approved, unexpired, unsuspended checks and current provider eligibility.
- Identity/business edits invalidate affected checks. Public projections exclude documents and storage references.
- Invitations are expiring and single-use. Sessions must support revocation; framework-managed cookies must honor this state.
- UTC timestamps are authoritative. Nullable timestamps represent events that have not occurred or checks without configured expiry.

Milestones B/C extend this model with customer properties, services, scheduling, bookings, reviews, partner clients, idempotency, and webhook deliveries. Their schema is not finalized here.
