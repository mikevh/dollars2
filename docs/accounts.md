# Accounts

## Account Storage

- Accounts are per-user, stored in the database
- Each account has: user ID, account name, source type (Plaid, SimpleFIN, or Manual), connection details (JSON string), an "include in budget" flag
- The JSON connection details column stores provider-specific configuration (e.g., Plaid access token + account ID, SimpleFIN credentials)

## Include in Budget

- Each account carries an `IncludeInBudget` flag (`bit`, default `1`)
- When `0`, the account's transactions are hidden entirely from the budget transaction pane — they never appear in the New / Tracked / Deleted / Pending tabs or their counts, so they can't be assigned to a line item and never affect budget totals
- The account otherwise behaves normally: it still syncs on the backend, still appears in the `/accounts` view, and its transactions remain viewable on the per-account transactions page. Account balances are unaffected
- Manual transactions (no account) are always included regardless of any account's flag
- V1 sets the flag directly in the database like all other account setup — no toggle UI

## V1 Setup

- Accounts are created directly in the database (no UI for account management in v1)
- No account editing, deactivation, or deletion UI in v1

## Transaction Association

- Every transaction (synced or manual) is associated with an account
- Account name is displayed as a small label on each transaction
- Manual transactions append "-manual" to the account label
