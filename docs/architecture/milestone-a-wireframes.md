# Milestone A journeys and wireframes

Status: Low-fidelity planning sketches, not implemented pages.

## Provider onboarding

```text
Register → Verify email → Organization → Profile → Evidence → Submit → Decision

REGISTER                     PROVIDER PROFILE
Name [________________]      Organization [active organization ▼]
Email [_______________]      Display name [_____________________]
Password [____________]      Type [Individual / Business]
Account [Customer/Provider]  Biography [________________________]
[Create account]             Categories [Cleaning] [Plumbing] ...
                             Service area [_____________________]
                             [Save draft] [Continue]
```

Show field errors beside labeled controls. Registration leads to a check-email state and resend option with rate-limit feedback. Preserve non-sensitive form fields after a failed request. Unverified providers can save drafts and see why submission is unavailable.

```text
VERIFICATION CENTER
Demo: submit fictional documents only. Checks are simulated.
Required evidence: [configured list for type/categories]
[Choose document]  [Upload]  Filename / type / upload status
Identity evidence: [ready / missing / failed]
Business evidence: [ready / missing / not required]
[Save draft] [Submit application]

APPLICATION STATUS
Submitted / Under review / More information / Approved / Rejected
Submitted time | Version | Reviewer-facing decision history
Provider-facing reason [_________________________________]
[Edit requested evidence] [Resubmit] (when permitted)
```

Submission shows a confirmation with evidence summary. Under-review versions cannot be edited. Handle oversized/unsupported files, upload interruption, access expiry, and stale updates without losing draft metadata. Never show another organization's evidence.

## Administrative review

```text
VERIFICATION QUEUE
Status [Submitted / Under review ▼]  [Refresh]
Provider | Type | Submitted | Status | [Review]
[Previous] Page n [Next]

REVIEW APPLICATION
Provider summary | Organization | Submission version
Evidence [Open authorized document] [expiry indication]
Checks [Identity] [Business] [Category credential]
Expiry [optional per check] | Simulated check disclosure
Reason [________________________________________________]
[Approve selected checks] [Request information] [Reject]
```

Review requires administrative authorization, explicit selected checks for approval, and reasons for rejection/information requests. A stale decision presents the current version and requires rereading before retry. Include loading, empty-queue, missing-evidence, document-access failure, and decision-success states.

## Public provider profile

```text
Provider name | Service area | Categories
Biography
Identity verified [Simulated demo check] [Meaning]
Business verified [Simulated demo check] [Meaning]
```

Hide expired/revoked badges. Unapproved profiles are unavailable publicly. Do not expose document metadata, object keys, or sensitive identifiers. Badge meanings must be readable without hover; use keyboard-accessible controls and status text beyond color.

Marketplace and partner journeys are planned after this first slice and will receive separate wireframes.
