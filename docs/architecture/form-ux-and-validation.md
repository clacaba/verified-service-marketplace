# Form UX and frontend validation

Implemented 2026-10-02.

- Login and registration have keyboard-accessible password visibility buttons
  with eye icons, explicit Show/Hide labels, and pressed state. Toggling does not
  submit the form. Passwords remain masked by default.
- Registration includes confirmation matching and live requirement feedback:
  red/Weak, yellow/Medium, green/Strong. Text and a checklist convey the same
  information without relying on color.
- Strength is a local requirement-completion guide, not an entropy estimate or
  compromised-password check. It does not send passwords to another service.
- Registration enforces the existing API password policy: 12–128 characters,
  ASCII uppercase/lowercase letters, an ASCII digit, and a nonalphanumeric
  character. Login does not impose new registration requirements on passwords.
- Name, email, account type, password, and confirmation errors appear inline
  after blur or submission. Submission focuses the first invalid field, while
  busy forms prevent duplicate requests. Confirmation is not sent to the API.
- Required labels end in a red `*`. Optional draft biography/area, address line
  2, search filters, and review expiry remain optional. The reviewer reason
  marker follows the selected decision's conditional requirement.
- Authentication cards, field spacing, mobile layout, focus indicators, and
  success/error messages are improved without changing backend authorization.

## Checks

Run `node scripts/test-account-validation.mjs`, `npm run typecheck`, and
`npm run build` from the repository root. All passed for this change. The
regression script exercises policy boundaries, matching, login differences,
and server-rendered controls; it does not replace browser interaction tests.

After rebuilding the local web container, open `/register` and `/login`:

1. Confirm passwords are masked initially and eye buttons toggle each field.
2. Enter `abc`, `LongPassword123`, then `ValidPassword!123` to observe Weak,
   Medium, and Strong. Use different, unique credentials for real accounts.
3. Blur missing/invalid fields and mismatched confirmation; submit and confirm
   errors focus the first invalid field without an API registration request.
4. Check keyboard navigation, narrow/mobile layout, screen-reader labels,
   password-manager filling, and required markers on the remaining forms.

The user reported that local manual UI checks passed on 2026-10-02. This is
user-reported acceptance, not an automated browser-test result. Further visual
or interaction issues can be recorded as they are discovered.
