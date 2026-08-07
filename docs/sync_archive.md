# Sync Archive

An append-only, versioned record of the raw JSON the bank sync providers (Plaid, SimpleFIN) actually
return — kept alongside, not inside, the MSSQL budget data.

## What It Is and Why

Every time a sync run touches an account, the provider's raw payload for each transaction, each
removed transaction, the account metadata, and any provider-reported error is written to a separate
DynamoDB table (`Dollars2SyncArchive`) as a new item. Nothing is ever overwritten and nothing expires.

Three reasons this exists:

- **Forensics.** "What did the bank actually send?" MSSQL only ever holds what `TransactionService`
  chose to keep — description/payee/memo truncated to `nvarchar(500)`, fields the app doesn't model
  dropped entirely. The archive holds the payload verbatim.
- **History.** Re-seeing the same transaction across sync runs — pending → posted, an amount
  correction, a description rewrite — normally overwrites the MSSQL row in place. The archive is the
  only place those intermediate states survive.
- **Future features.** Auto-categorization, merchant enrichment, and balance history (all currently
  `docs/out_of_scope.md`) would need the provider's original fields, not the subset MSSQL keeps
  today. The archive exists so that data isn't already gone by the time those features are built.

## Why DynamoDB, Not an MSSQL `nvarchar(max)` Column

This was a deliberate choice, not a reflex reach for a new technology, for three reasons:

- **The write path must never be able to affect a sync.** Archiving is best-effort
  (see [Best-Effort Writes](#best-effort-writes) below) — a DynamoDB outage must never fail or roll
  back the MSSQL transaction that lands a transaction in the budget. Putting the archive in a wholly
  separate store makes that a structural guarantee rather than a discipline every future change has to
  remember: there is no shared connection or shared transaction scope to accidentally couple through.
- **The access pattern is key-value and time-ordered, not relational.** Every read the app performs
  is either "every sighting of this one transaction, newest first" or "this one account's history,
  newest first" — a single-partition query, never a join. That's exactly what a partition key + sort
  key is for; modeling the same access pattern in MSSQL means a side table with a hand-rolled
  `(TransactionId, SyncedAt DESC)` index doing the same job DynamoDB's key schema gives for free.
- **Keeping high-churn archival writes out of the transactional log chain.** The sync window overlaps
  7 days by design, and a full resync re-fetches 180 days, so most transactions get archived many
  times with identical payloads — the archive absorbs a lot of blob churn. `docs/backups.md`'s hourly
  transaction-log backups exist to protect real financial data with point-in-time recovery; inflating
  that log chain with archival JSON that needs none of that guarantee would be pure cost.

## This Is dynamodb-local Everywhere — There Is No AWS Account

Production runs the exact same `amazon/dynamodb-local` container as local dev, on the same LAN-only
network as the existing Elasticsearch and Kibana services (`docker-compose.yml`). "DynamoDB" in a
self-hosted app otherwise reads as a cloud dependency — it deliberately is not one here:

- No AWS account exists anywhere in this feature. No IAM, no regions that mean anything, no billing,
  no egress from the home server.
- No AWS credentials are stored anywhere — not in `appsettings.json`, not in `.env`, not in
  `dotnet user-secrets`, not as `AWS_*` environment variables. The only configuration is the endpoint
  URL and the table name (`DynamoDbOptions`, `Data/DynamoDbOptions.cs`).
- The AWS SDK still refuses to sign a request without *some* credential object and *some* region, even
  though dynamodb-local validates neither and `-sharedDb` makes the region meaningless. That
  requirement is satisfied with hardcoded throwaway values at the DI registration in `Program.cs`:

  ```csharp
  // dynamodb-local ignores both of these, but the SDK refuses to sign a request without them.
  // Hardcoded rather than configured on purpose: there is no account behind this endpoint, so
  // there is nothing for a deployer to fill in and no secret to protect. Surfacing them as
  // config would imply otherwise and invite pointing this at real AWS.
  new AmazonDynamoDBClient(
      new BasicAWSCredentials("local", "local"),
      new AmazonDynamoDBConfig
      {
          ServiceURL = dynamoDbOptions.ServiceUrl,
          AuthenticationRegion = "us-east-1",
      });
  ```

  This is why the `CLAUDE.md` user-secrets convention doesn't apply here: that rule covers real
  credentials (the JWT secret, the SQL connection string). `"local"` / `"local"` authenticates
  nobody, and surfacing it as configuration would only invite someone to eventually point this at a
  real account — which is explicitly not the design.

## Table Schema

Single table, name from `DynamoDbOptions.TableName` (default `Dollars2SyncArchive`), created
idempotently at startup by `SyncArchiveTableInitializer` (an `IHostedService` — DynamoDB has no schema
beyond its key attributes, so there's nothing resembling the numbered SQL migration chain here).

```
Partition key   pk  (S)   USER#{userId}#ACCT#{accountId}
Sort key        sk  (S)   TXN#{providerTransactionId}#{syncedAt}
                          REMOVED#{providerTransactionId}#{syncedAt}
                          ACCTMETA#{syncedAt}
                          ERROR#{syncedAt}#{seq}
                          SKIPPED#{syncedAt}#{seq}
```

`syncedAt` is ISO-8601 UTC with a `Z` marker and millisecond precision
(`2026-08-03T06:00:00.000Z`), which sorts lexicographically in chronological order — that's what
makes the composite sort keys usable at all.

Every item also carries:

| attribute | type | notes |
|---|---|---|
| `pk` | S | `USER#{userId}#ACCT#{accountId}` |
| `sk` | S | see above |
| `syncedAt` | S | ISO-8601 UTC with `Z`; also the LSI sort key |
| `syncRunId` | S | GUID, identical for every item one `SyncConnectionAsync` call writes |
| `userId` | N | |
| `accountId` | N | |
| `sourceType` | S | `SimpleFIN` / `Plaid` |
| `itemType` | S | `Transaction` / `Removed` / `AccountMetadata` / `ProviderError` / `SkippedTransaction` |
| `providerTransactionId` | S | `Transaction` and `Removed` items only |
| `rawJson` | S | the payload, verbatim — absent on `Removed` items, whose whole payload is the id |

### Local Secondary Index

```
LSI name   LSI_SyncedAt
Sort key   syncedAt (S)
Projection ALL
```

The partition is already scoped to a single account, so an LSI — not a GSI — is the right primitive
for "this account's archive, newest first" (what the sync archive page needs). An LSI must be declared
at table-creation time and can never be added later, so it ships with the first table or not at all.

The cost: an LSI caps a single partition at **10GB**. Because the partition key already scopes to one
account (`USER#{userId}#ACCT#{accountId}`), that cap applies per-account rather than globally — at
this app's volume, decades away either way, but worth knowing before assuming it's a whole-table
limit.

Billing mode is `PAY_PER_REQUEST`. dynamodb-local ignores throughput settings entirely, so this has no
runtime effect — it's chosen only because there's no capacity to plan for and no AWS account behind
this table to plan it against.

## Versioning Model

Every sighting of a transaction writes a **new** item, keyed by `syncedAt`. Re-seeing the same
transaction never overwrites the previous version:

```
TXN#abc123#2026-08-01T06:00:00.000Z   {"pending": true,  ...}
TXN#abc123#2026-08-02T06:00:00.000Z   {"pending": true,  ...}
TXN#abc123#2026-08-03T06:00:00.000Z   {"pending": false, ...}   <- posted
```

No TTL. Items live forever. One `syncRunId` and one `syncedAt` are generated per
`BankSyncService.SyncConnectionAsync` invocation and shared by every account in that connection group,
which is what lets the sync archive page group items back into "one sync run" rows.

Accepted cost: the sync window overlaps 7 days by design, and a full resync re-fetches 180 days, so
most transactions get archived many times with identical payloads. That's the tradeoff of an
append-every-sighting schema, and it's fine at this app's volume.

## Plaid vs. SimpleFIN: A Fidelity Difference Worth Knowing

Both providers' archived `rawJson` is genuine wire JSON obtained via `JsonElement.GetRawText()` — but
*how* each gets there differs, and it's the single most surprising property of the archive:

- **SimpleFIN** (`Providers/SimplefinProvider.cs`) fetches the response body as a plain string and
  indexes it by transaction/account id with `JsonDocument`, pulling `.GetRawText()` for each object.
  The archived payload is exactly the bytes SimpleFIN sent, including fields the app's typed DTOs
  don't model.
- **Plaid** (`Providers/PlaidProvider.cs`) uses the `Going.Plaid` SDK, whose response objects are
  typed C# models with no route back to their own JSON — *unless* `ShowRawJson = true` is set on the
  request, which this app does. With that flag, `Going.Plaid` hands back the raw response body
  alongside the typed one, and the provider splits it into per-transaction raw JSON, paired
  **positionally** with the typed list (the page's `added[]`/`modified[]` arrays deserialize into
  `response.Added`/`response.Modified` in document order). If the raw array's length doesn't match
  the typed array's count, the provider logs a warning and archives empty strings rather than risk
  pairing the wrong bytes with the wrong transaction id.

So Plaid's archive is not a re-serialization of `Going.Plaid`'s typed models (an earlier draft of this
feature assumed it would have to be, and documented that as a known fidelity gap — that premise turned
out to be wrong once `ShowRawJson` was found). Both providers store genuine wire bytes. The real
distinction is architectural fragility, not data loss: SimpleFIN's raw text falls out of parsing its
own response naturally, while Plaid's raw JSON exists only because of an explicit SDK opt-in and a
positional pairing step that has to stay in sync with the typed deserialization it rides alongside.

## Best-Effort Writes

Archiving must never affect a sync. `BankSyncService.ArchiveBestEffortAsync` wraps every archive write
in an unfiltered try/catch:

```csharp
private async Task ArchiveBestEffortAsync(Account account, ProviderSyncResult syncResult, Guid syncRunId, DateTime syncedAt, CancellationToken cancel)
{
    try
    {
        await _syncArchiveRepo.ArchiveAsync(account, syncResult, syncRunId, syncedAt, cancel);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Could not archive sync payloads for account {AccountId} ({AccountName}) in sync run {SyncRunId}; the sync itself is unaffected", account.Id, account.Name, syncRunId);
    }
}
```

A DynamoDB outage loses that run's payloads silently and never blocks, fails, or rolls back the sync —
transactions still reach the budget. `SyncArchiveRepository.ArchiveAsync` itself *throws* on failure
rather than swallowing it; "best-effort" is the caller's policy, not the repository's, so an outage is
never silently reported as success. A 10-second timeout on the DynamoDB client keeps a stalled write
from hanging a sync indefinitely.

The same posture applies at startup: `SyncArchiveTableInitializer` logs an error and continues if
DynamoDB is unreachable when the app boots, rather than preventing the app from serving budgets.

Writes go through `BatchWriteItemAsync` in chunks of 25 (DynamoDB's batch limit), retrying
`UnprocessedItems` with backoff a bounded number of times before giving up.

## Reading the Archive

Two endpoints, both documented in full in `docs/backend.md`:

- `GET /api/transactions/{id}/raw-history` — every archived sighting of one transaction, newest
  first. Surfaced in the transaction edit dialog's **Raw History** tab (`docs/frontend.md`).
- `GET /api/accounts/{id}/sync-archive?before=&limit=` — one account's archive, keyset-paged and
  grouped into sync runs, newest first. The only read path that reaches account-metadata, removal,
  provider-error, and skipped-transaction items, since they have no transaction to hang off. Surfaced
  on the **Sync Archive** page (`docs/frontend.md`), reached from a synced account's transactions
  page.

Both reads return 503 `ARCHIVE_UNAVAILABLE` when DynamoDB can't be reached — unlike the best-effort
write path, a failed read has no useful fallback to offer.

## Inspecting the Table Directly

The stack ships a browser UI for this — `dynamodb-admin`, at `http://localhost:8001` (or the LAN host
on the home-server deploy), pointed at the same table.

For scripted or ad hoc access, the AWS CLI works against `http://localhost:8000` with any dummy
credentials — the CLI insists on *some* profile existing before it will run, even though dynamodb-local
never checks it:

```bash
aws configure set aws_access_key_id local --profile dynamodb-local
aws configure set aws_secret_access_key local --profile dynamodb-local
aws configure set region us-east-1 --profile dynamodb-local

aws dynamodb scan \
  --table-name Dollars2SyncArchive \
  --endpoint-url http://localhost:8000 \
  --profile dynamodb-local

aws dynamodb query \
  --table-name Dollars2SyncArchive \
  --endpoint-url http://localhost:8000 \
  --profile dynamodb-local \
  --key-condition-expression "pk = :pk" \
  --expression-attribute-values '{":pk": {"S": "USER#1#ACCT#3"}}'
```

## Where the Data Lives, and That Nothing Backs It Up

The archive's only copy is the `dynamodata` Docker named volume (`docker-compose.yml`). Unlike MSSQL
(`docs/backups.md` — nightly full backups, hourly transaction logs, an offsite NAS copy), there is no
backup job, snapshot, or restore procedure for this volume. `docker compose down -v` destroys the
archive permanently.

This is accepted, not an oversight: the archive is append-only with no TTL, and its contents can
largely be re-fetched from the providers on the next sync — that's a recovery procedure, not a backup,
and it only recovers what the provider still has (Plaid and SimpleFIN don't retain transactions
forever either). Because archiving is best-effort ([above](#best-effort-writes)), losing the volume
entirely degrades nothing else in the app — no budget data, sync status, or transaction depends on the
archive existing.
