# Authentication & Users

## V1 Authentication

- Passkey-only — no passwords, ever. Login requires a WebAuthn passkey registered against the
  user's account
- User records are manually added to the database; an admin sets `Users.RegistrationKey` (a
  one-time shared secret, direct SQL `UPDATE`) to authorize that user to register a passkey
- No open self-registration
- **Registration** (`POST api/auth/passkey/register/options` then `POST
  api/auth/passkey/register/complete`): client submits the email + registration key it was given
  out-of-band; server validates the key, runs the WebAuthn attestation ceremony via ASP.NET Core
  Identity's `IPasskeyHandler<User>` (called directly, not through `SignInManager`'s cookie-based
  sign-in), stores the resulting credential, and blanks `RegistrationKey`. Re-registering with a
  new key deletes any prior credentials for that user first — lost-passkey recovery presumes the
  old credential is compromised
- **Login** (`POST api/auth/passkey/login/options` then `POST api/auth/passkey/login/complete`):
  client submits its email, server runs the WebAuthn assertion ceremony scoped to that user's
  credentials, and on success issues a JWT + refresh token exactly as before — passkey login is a
  drop-in replacement for the old email-only credential check, not a new token scheme
- Ceremony state (WebAuthn's attestation/assertion state) round-trips in a short-lived,
  Data-Protected, HttpOnly cookie between the options and completion calls — never returned to the
  client in the response body

## JWT

- 30-day token expiration
- Refresh tokens to get new JWTs without re-logging in
- JWT secret supplied out-of-band, never committed: `dotnet user-secrets` locally, an environment
  variable in the container deploy. `appsettings.json` carries only the `<dotnet user secret>`
  placeholder

## Multi-User

- Each user has completely separate budgets, transactions, and accounts
- No shared/joint budgets between users
- All data is scoped to a user — no cross-user data access
