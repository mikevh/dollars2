# Authentication & Users

## V1 Authentication

- No passwords in v1 — login succeeds with a matching email address found in the database
- User records are manually added to the database
- No open self-registration
- Proper auth with passkeys planned for a later version

## JWT

- 30-day token expiration
- Refresh tokens to get new JWTs without re-logging in
- JWT secret stored in appsettings.json

## Multi-User

- Each user has completely separate budgets, transactions, and accounts
- No shared/joint budgets between users
- All data is scoped to a user — no cross-user data access
