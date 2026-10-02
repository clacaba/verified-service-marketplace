# Marketplace visual refresh

## Design direction

Warm ivory, forest green, and soft amber; editorial typography; an original code-native home illustration; stronger signup calls to action; and a split-layout login/registration experience. No external image downloads, additional frontend packages, invented reviews, user counts, or accreditation claims.

The landing-page service cards use existing service-search routes. Signup still uses the existing account type selector, email verification flow, password show/hide controls, strength feedback, required labels, and server validation. Public pages no longer incorrectly say that booking is unavailable. Dashboard changes are presentation-only.

Portfolio-demo disclosures and simulated-verification wording remain visible. No payment is collected. The marketing illustration is decorative, not a provider listing or booking confirmation.

## Verification

- TypeScript, production build, existing account/booking regression tests, and API-proxy smoke checks.
- `node scripts/test-visual-design.mjs`: server-rendered CTA routes and disclosures, connected/unavailable API states, CSS parsing, and presence of responsive/focus/reduced-motion rules. These checks are not visual layout validation.
- Browser connection failed during setup. No agent screenshot inspection or interactive browser validation is claimed.
- On 2026-10-02, the user reported local testing passed and approved committing and pushing the design refresh. This is user-reported acceptance, not independent confirmation of each checklist item by the agent.

## Local manual review

- Review `/`, `/register`, `/login`, `/services`, `/providers`, `/account`, and `/bookings`.
- Check desktop, tablet, and a narrow mobile viewport for overflow, readable typography, button spacing, and stacked cards.
- Follow category links and signup/sign-in links. Check provider/customer selection and registration validation.
- Test password visibility, strength feedback, focus outlines, required fields, submission errors, and successful sign-in.
- Confirm existing booking actions and permissions remain unchanged.
