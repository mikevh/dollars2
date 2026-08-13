# Sync Archive

An append-only record of the raw HTTP response bodies the bank sync providers (Plaid, SimpleFIN)
actually return — kept alongside, not inside, the MSSQL budget data.

## What It Is and Why

Every time a sync run fetches from a provider, the raw response body of each upstream HTTP call —
one for SimpleFIN, one per page for Plaid — is written to a separate DynamoDB table
(`Dollars2SyncArchive`) as a new item, verbatim and unparsed. Nothing is ever overwritten and
nothing expires.

Three reasons this exists:

- **Forensics.** "What did the bank actually send?" MSSQL only ever holds what `TransactionService`
  chose to keep — description/payee/memo truncated to `nvarchar(500)`, fields the app doesn't model
  dropped entirely. The archive holds the whole response verbatim.
- **History.** Re-seeing the same transaction across sync runs — pending → posted, an amount
  correction, a description rewrite — normally overwrites the MSSQL row in place. The archive is the
  only place those intermediate states survive, inside whichever response body happened to carry them.
- **Future features.** Auto-categorization, merchant enrichment, and balance history (all currently
  `docs/out_of_scope.md`) would need the provider's original fields, not the subset MSSQL keeps
  today. The archive exists so that data isn't already gone by the time those features are built.

There is no read path for any of this inside the app — the archive is pure write, inspected only ad
hoc (see [Inspecting the Table Directly](#inspecting-the-table-directly)). It previously had two GET
endpoints and a UI surfaced them; both were removed as unused before this schema existed.

## Why DynamoDB, Not an MSSQL `nvarchar(max)` Column

This was a deliberate choice, not a reflex reach for a new technology:

- **The write path must never be able to affect a sync.** Archiving is best-effort
  (see [Best-Effort Writes](#best-effort-writes) below) — a DynamoDB outage must never fail or roll
  back the MSSQL transaction that lands a transaction in the budget. Putting the archive in a wholly
  separate store makes that a structural guarantee rather than a discipline every future change has to
  remember: there is no shared connection or shared transaction scope to accidentally couple through.
- **Keeping high-churn archival writes out of the transactional log chain.** The sync window overlaps
  7 days by design, and a full resync re-fetches 180 days, so a given response's contents get
  re-archived many times across runs. `docs/backups.md`'s hourly transaction-log backups exist to
  protect real financial data with point-in-time recovery; inflating that log chain with archival
  JSON that needs none of that guarantee would be pure cost.

The schema is now a plain key-value log with no query pattern behind it (the app never reads it
back), so the case for DynamoDB specifically — rather than any other blob store — rests on the two
points above, not on an access-pattern win.

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
Partition key   pk  (S)   {sourceType}#{instant}
                          {sourceType}#{instant}#{page}
```

No sort key — the whole item identity lives in `pk`.

- `{sourceType}` is `SimpleFIN` or `Plaid`.
- `{instant}` is ISO-8601 UTC with a `Z` marker and millisecond precision
  (`2026-08-03T06:00:00.000Z`), stamped once per connection-level fetch (`BankSyncService.SyncConnectionAsync`).
- `{page}` is a zero-padded index (`0000`, `0001`, ...), present only when a fetch captured more
  than one raw response body. SimpleFIN always fetches in one call, so its key never carries a page
  segment. Plaid pages through `/transactions/sync`, and without a sort key two pages sharing the
  same `{instant}` would otherwise collide on the same `pk` — silently overwriting one page's
  archive, or making DynamoDB reject the whole `BatchWriteItem` (it rejects a batch containing two
  items with the same key rather than rejecting them individually). The page index makes every
  item's key unique regardless.

Every item carries just:

| attribute | type | notes |
|---|---|---|
| `pk` | S | see above |
| `rawJson` | S | the upstream HTTP response body, verbatim |

No `userId`, `accountId`, `sourceType`, or `syncRunId` attributes — deliberately, in favor of maximum
simplicity over scan-time filterability. A connection-level fetch can cover several stored accounts
(a shared Plaid Item or SimpleFIN access URL), so per-account attribution was never a single ownership
fact per item anyway.

Billing mode is `PAY_PER_REQUEST`. dynamodb-local ignores throughput settings entirely, so this has no
runtime effect — it's chosen only because there's no capacity to plan for and no AWS account behind
this table to plan it against.

### Migrating an Existing Table

DynamoDB has no `ALTER TABLE` for key schema, and `SyncArchiveTableInitializer` only creates the
table when it's absent — it will not touch a table that already exists under the old
`pk`/`sk`/LSI schema. Deploying this schema change requires manually dropping the existing
`Dollars2SyncArchive` table (via `dynamodb-admin` at `http://localhost:8001`, or the AWS CLI — see
[Inspecting the Table Directly](#inspecting-the-table-directly)) before the new code runs, so the
initializer recreates it with the schema above. Until that's done, writes against the stale table
fail validation and are swallowed by the best-effort catch (archiving silently stops working, logged
as a warning) — the sync itself is unaffected either way. This is a one-time step tied to this
schema change, not an ongoing operational concern; see [Where the Data Lives](#where-the-data-lives-and-that-nothing-backs-it-up)
for why losing the old table's contents is acceptable.

## Versioning Model

Every connection-level fetch writes **new** items, keyed by provider and instant. A later sync never
overwrites an earlier one's archived response:

```
SimpleFIN#2026-08-01T06:00:00.000Z
SimpleFIN#2026-08-02T06:00:00.000Z
SimpleFIN#2026-08-03T06:00:00.000Z
```

No TTL. Items live forever.

Accepted cost: the sync window overlaps 7 days by design, and a full resync re-fetches 180 days, so
most response bodies carry mostly-identical data across runs. That's the tradeoff of an
append-every-fetch schema, and it's fine at this app's volume — the same tradeoff the previous
per-transaction schema accepted, just now paid once per fetch instead of once per transaction.

## Both Providers Capture the Same Way

Both providers hand back genuine wire JSON with no re-serialization involved:

- **SimpleFIN** (`Providers/SimplefinProvider.cs`) reads the response body as a plain string and
  archives it directly — it's already the exact bytes SimpleFIN sent, before any parsing happens.
- **Plaid** (`Providers/PlaidProvider.cs`) uses the `Going.Plaid` SDK, whose response objects are
  typed C# models with no route back to their own JSON — *unless* `ShowRawJson = true` is set on the
  request, which this app does. With that flag, `Going.Plaid` hands back each page's response body on
  `response.RawJson`, which is archived directly, one item per page.

Neither provider slices, indexes, or re-pairs anything to build the archived payload — that
per-transaction correlation step (and its attendant fragility, when a raw array's length disagreed
with the typed one) existed only under the old per-transaction-item schema and was removed along
with it.

## Best-Effort Writes

Archiving must never affect a sync. `BankSyncService.ArchiveBestEffortAsync` wraps every archive write
in an unfiltered try/catch:

```csharp
private async Task ArchiveBestEffortAsync(string sourceType, IReadOnlyList<Account> accounts, IReadOnlyList<string> rawResponseBodies, DateTime syncedAt, CancellationToken cancel)
{
    try
    {
        await _syncArchiveRepo.ArchiveAsync(sourceType, rawResponseBodies, syncedAt, cancel);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Could not archive sync payloads for {SourceType} accounts {AccountIds}; the sync itself is unaffected", sourceType, string.Join(", ", accounts.Select(a => a.Id)));
    }
}
```

Called once per connection-level fetch (`BankSyncService.SyncConnectionAsync`), right after the
provider call and before any account in the group is persisted — so a failed account still gets that
run's raw response archived, which is precisely the case the archive exists to explain.

A DynamoDB outage loses that run's payloads silently and never blocks, fails, or rolls back the sync —
transactions still reach the budget. `SyncArchiveRepository.ArchiveAsync` itself *throws* on failure
rather than swallowing it; "best-effort" is the caller's policy, not the repository's, so an outage is
never silently reported as success. A 10-second timeout on the DynamoDB client keeps a stalled write
from hanging a sync indefinitely.

The same posture applies at startup: `SyncArchiveTableInitializer` logs an error and continues if
DynamoDB is unreachable when the app boots, rather than preventing the app from serving budgets.

Writes go through `BatchWriteItemAsync` in chunks of 25 (DynamoDB's batch limit), retrying
`UnprocessedItems` with backoff a bounded number of times before giving up — though in practice a
connection-level fetch rarely produces more than a handful of raw response bodies, so chunking is a
safety margin more than a routine path.

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

aws dynamodb get-item \
  --table-name Dollars2SyncArchive \
  --endpoint-url http://localhost:8000 \
  --profile dynamodb-local \
  --key '{"pk": {"S": "SimpleFIN#2026-08-03T06:00:00.000Z"}}'
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
