# Organizations, ownership, and staff memberships

Implemented: provider organization creation, active membership summaries, owner-only member roster and invitations, verified-recipient invitation acceptance, and staff deactivation.

## Local walkthrough

1. Start the stack using the local development guide and sign in as a Provider.
2. Open `/organizations`, create an Individual or Business organization, and confirm your Owner role.
3. Invite another email as Staff. Find its invitation in Mailpit at http://localhost:8025.
4. Register that email, verify it, and sign in. Return to the invitation link and accept.
5. Confirm the Staff account can select its organization but cannot invite or deactivate members.
6. As Owner, deactivate the Staff member. Their existing session must immediately lose organization access.

## Authorization and safety

- An organization is a provider business/workspace; a membership connects an account to that workspace. A customer property is a separate concept, not an organization identifier.
- Owner permissions cover member management and future profile, verification, and booking management. Staff currently receive read permissions only; those future business endpoints are not yet implemented.
- All organization access checks use active database membership. Unrelated accounts receive 404; active Staff attempting Owner actions receive 403.
- Creation saves the organization, Owner membership, and audit event together.
- Invitations are Staff-only, expire after seven days, require a verified matching email, and can be consumed only once. Only a token hash is stored on the invitation; its queued email body is encrypted.
- Browser mutations require CSRF protection. Invitation tokens are never returned in the API response.
- Deactivation invalidates outstanding invitations for the removed member. Owner removal and ownership transfer are intentionally unsupported.
- Member lists default to 20 rows per page, with a maximum of 100.

## Verification status

Backend integration tests use SQLite and cover ownership, isolation, invitation acceptance, revocation, and CSRF. Frontend type checking and production build pass. Actual PostgreSQL migrations, SMTP delivery, and the complete browser journey still require Docker verification.
