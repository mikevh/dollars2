# Accounts

## Account Storage

- Accounts are per-user, stored in the database
- Each account has: user ID, account name, source type (Plaid, SimpleFIN, or Manual), connection details (JSON string)
- The JSON connection details column stores provider-specific configuration (e.g., Plaid access token + account ID, SimpleFIN credentials)

## V1 Setup

- Accounts are created directly in the database (no UI for account management in v1)
- No account editing, deactivation, or deletion UI in v1

## Transaction Association

- Every transaction (synced or manual) is associated with an account
- Account name is displayed as a small label on each transaction
- Manual transactions append "-manual" to the account label
