using System.Text.Json;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RemovedTransaction = Going.Plaid.Entity.RemovedTransaction;
using PlaidAccount = Going.Plaid.Entity.Account;
using PlaidTransaction = Going.Plaid.Entity.Transaction;
using AccountBalance = Going.Plaid.Entity.AccountBalance;

namespace Dollars2.Tests;

// Regression test for the "empty account_id absorbs siblings" bug: when several stored accounts
// share one Plaid access token, a blank account_id would match (and import) every sibling's
// transactions. It must now fail that account instead — but a lone account on a token, where a blank
// account_id is unambiguous, must still be allowed.
public class PlaidProviderTests
{
    [Theory]
    [InlineData(2, "", true)]        // shared token, blank -> fail
    [InlineData(2, null, true)]      // shared token, null  -> fail
    [InlineData(2, "acct-1", false)] // shared token, set   -> ok
    [InlineData(1, "", false)]       // lone account, blank -> allowed
    [InlineData(1, null, false)]     // lone account, null  -> allowed
    [InlineData(1, "acct-1", false)] // lone account, set   -> ok
    public void Blank_account_id_only_fails_when_token_is_shared(int accountCount, string? accountId, bool expectError)
    {
        var error = PlaidProvider.SharedTokenMissingAccountIdError(accountCount, accountId);

        Assert.Equal(expectError, error is not null);
    }

    // Cursor convergence: the group reuses a stored cursor only when every account it can actually
    // sync agrees on one non-empty value.
    [Fact]
    public void ResolveGroupCursor_reuses_shared_non_empty_cursor()
    {
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"),
            ("acct-2", "cursor-x"),
        };

        Assert.Equal("cursor-x", PlaidProvider.ResolveGroupCursor(group));
    }

    [Fact]
    public void ResolveGroupCursor_forces_full_resync_when_cursors_diverge()
    {
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"),
            ("acct-2", "cursor-y"),
        };

        Assert.Null(PlaidProvider.ResolveGroupCursor(group));
    }

    [Fact]
    public void ResolveGroupCursor_forces_full_resync_when_a_syncable_account_has_no_cursor()
    {
        // A newly added account (empty cursor) alongside an established one must backfill.
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"),
            ("acct-2", ""),
        };

        Assert.Null(PlaidProvider.ResolveGroupCursor(group));
    }

    // Regression test for the cursor-divergence resync storm: a persistently misconfigured account
    // (blank account_id on a shared token) can never advance its cursor, so it must be excluded from
    // the convergence decision — otherwise it forces a full resync of its healthy siblings every sync.
    [Fact]
    public void ResolveGroupCursor_ignores_a_misconfigured_account_so_healthy_siblings_converge()
    {
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"), // healthy, advanced
            (null, ""),             // blank account_id on a shared token -> unsyncable, stale cursor
        };

        Assert.Equal("cursor-x", PlaidProvider.ResolveGroupCursor(group));
    }

    [Fact]
    public void ResolveGroupCursor_reuses_cursor_for_a_lone_account_with_blank_account_id()
    {
        // A single account on a token is unambiguous, so a blank account_id is still syncable.
        var group = new List<(string?, string?)>
        {
            (null, "cursor-x"),
        };

        Assert.Equal("cursor-x", PlaidProvider.ResolveGroupCursor(group));
    }

    [Fact]
    public void ResolveGroupCursor_forces_full_resync_when_no_account_is_syncable()
    {
        var group = new List<(string?, string?)>
        {
            (null, "cursor-x"),
            (null, "cursor-y"),
        };

        Assert.Null(PlaidProvider.ResolveGroupCursor(group));
    }

    // Full resync (issue #84): a user-initiated resync forces a null cursor so /transactions/sync
    // re-streams the whole Item, ignoring whatever cursor is stored.
    [Fact]
    public void SelectGroupCursor_forces_null_cursor_on_full_resync_even_when_converged()
    {
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"),
            ("acct-2", "cursor-x"),
        };

        Assert.Null(PlaidProvider.SelectGroupCursor(fullResync: true, group));
    }

    [Fact]
    public void SelectGroupCursor_reuses_converged_cursor_on_a_normal_sync()
    {
        var group = new List<(string?, string?)>
        {
            ("acct-1", "cursor-x"),
            ("acct-2", "cursor-x"),
        };

        Assert.Equal("cursor-x", PlaidProvider.SelectGroupCursor(fullResync: false, group));
    }

    // Regression test for the "removed transactions dropped" bug: at the pinned API version, removed
    // items carry no account_id, so filtering them by account_id skipped every soft-delete for an
    // account that had a specific account_id. Removed ids must be collected regardless of account_id
    // (the DB soft-delete is scoped by account.Id), and blank ids dropped.
    [Fact]
    public void CollectRemovedTransactionIds_keeps_ids_without_account_id_and_drops_blanks()
    {
        var removed = new[]
        {
            new RemovedTransaction { TransactionId = "txn-1", AccountId = null },
            new RemovedTransaction { TransactionId = "txn-2", AccountId = "acct-1" },
            new RemovedTransaction { TransactionId = "", AccountId = null },
            new RemovedTransaction { TransactionId = null, AccountId = null },
        };

        var ids = PlaidProvider.CollectRemovedTransactionIds(removed);

        Assert.Equal(new[] { "txn-1", "txn-2" }, ids);
    }

    // Balance capture: the stored account's current balance is pulled from the Item snapshot by
    // matching account_id; a lone account with a blank account_id falls back to the sole snapshot
    // account; anything ambiguous or absent yields null.
    private static PlaidAccount SnapshotAccount(string accountId, decimal? current) => new()
    {
        AccountId = accountId,
        Balances = new AccountBalance { Current = current },
    };

    [Fact]
    public void ExtractCurrentBalance_matches_by_account_id()
    {
        var snapshot = new[]
        {
            SnapshotAccount("acct-1", 100.25m),
            SnapshotAccount("acct-2", 999.99m),
        };

        Assert.Equal(100.25m, PlaidProvider.ExtractCurrentBalance(snapshot, "acct-1"));
    }

    [Fact]
    public void ExtractCurrentBalance_falls_back_to_the_sole_account_when_account_id_is_blank()
    {
        var snapshot = new[] { SnapshotAccount("acct-1", 42.00m) };

        Assert.Equal(42.00m, PlaidProvider.ExtractCurrentBalance(snapshot, null));
    }

    [Fact]
    public void ExtractCurrentBalance_is_null_when_blank_account_id_is_ambiguous()
    {
        var snapshot = new[]
        {
            SnapshotAccount("acct-1", 1m),
            SnapshotAccount("acct-2", 2m),
        };

        Assert.Null(PlaidProvider.ExtractCurrentBalance(snapshot, ""));
    }

    [Fact]
    public void ExtractCurrentBalance_is_null_when_no_account_matches()
    {
        var snapshot = new[] { SnapshotAccount("acct-1", 1m) };

        Assert.Null(PlaidProvider.ExtractCurrentBalance(snapshot, "acct-missing"));
    }

    [Fact]
    public void ExtractCurrentBalance_is_null_when_provider_reports_no_current_balance()
    {
        var snapshot = new[] { SnapshotAccount("acct-1", null) };

        Assert.Null(PlaidProvider.ExtractCurrentBalance(snapshot, "acct-1"));
    }

    // Issue #93: Plaid puts no bound on original_description or merchant name, and the columns are
    // nvarchar(500). Over-length text must be clamped on the way in — unclamped it raises SqlException
    // 8152 on write, which fails the whole account's sync on every run while it is in the window.
    [Fact]
    public void MapTransaction_truncates_over_length_description_and_payee()
    {
        var longText = new string('x', 600);
        var mapped = PlaidProvider.MapTransaction(new PlaidTransaction
        {
            TransactionId = "t1",
            Date = new DateOnly(2026, 7, 15),
            Amount = 12.50m,
            OriginalDescription = longText,
            MerchantName = longText,
        }, rawJson: "");

        Assert.Equal(TransactionText.MaxLength, mapped.Description.Length);
        Assert.Equal(TransactionText.MaxLength, mapped.Payee.Length);
    }

    [Fact]
    public void MapTransaction_leaves_text_within_the_column_width_unchanged()
    {
        var mapped = PlaidProvider.MapTransaction(new PlaidTransaction
        {
            TransactionId = "t1",
            Date = new DateOnly(2026, 7, 15),
            Amount = 12.50m,
            OriginalDescription = "COFFEE SHOP #123",
            MerchantName = "Blue Bottle",
        }, rawJson: "");

        Assert.Equal("COFFEE SHOP #123", mapped.Description);
        Assert.Equal("Blue Bottle", mapped.Payee);
        // Plaid amounts are positive for outflow; ours are negative for expenses.
        Assert.Equal(-12.50m, mapped.Amount);
    }

    // Issue #168: RawJson is passed through untouched, deliberately exempt from the TransactionText
    // clamping the free-text columns get — the archive's whole point is keeping the payload intact,
    // and it is never written to an nvarchar(500) column.
    [Fact]
    public void MapTransaction_passes_the_raw_json_through_unclamped()
    {
        var raw = $$"""{"transaction_id":"t1","note":"{{new string('x', 600)}}"}""";

        var mapped = PlaidProvider.MapTransaction(new PlaidTransaction
        {
            TransactionId = "t1",
            Date = new DateOnly(2026, 7, 15),
            Amount = 1m,
        }, raw);

        Assert.Equal(raw, mapped.RawJson);
    }

    // Account metadata selection mirrors ExtractCurrentBalance's matching rule, so the archived account
    // object and the recorded balance can never come from two different accounts.
    private static PlaidProvider.RawPlaidAccount RawAccount(string accountId) =>
        new(accountId, $$"""{"account_id":"{{accountId}}"}""");

    [Fact]
    public void ExtractRawAccountMetadata_matches_by_account_id()
    {
        var snapshot = new[] { RawAccount("acct-1"), RawAccount("acct-2") };

        Assert.Equal("""{"account_id":"acct-1"}""", PlaidProvider.ExtractRawAccountMetadata(snapshot, "acct-1"));
    }

    [Fact]
    public void ExtractRawAccountMetadata_falls_back_to_the_sole_account_when_account_id_is_blank()
    {
        var snapshot = new[] { RawAccount("acct-1") };

        Assert.Equal("""{"account_id":"acct-1"}""", PlaidProvider.ExtractRawAccountMetadata(snapshot, null));
    }

    [Fact]
    public void ExtractRawAccountMetadata_is_null_when_blank_account_id_is_ambiguous()
    {
        var snapshot = new[] { RawAccount("acct-1"), RawAccount("acct-2") };

        Assert.Null(PlaidProvider.ExtractRawAccountMetadata(snapshot, ""));
    }

    [Fact]
    public void ExtractRawAccountMetadata_is_null_when_no_account_matches()
    {
        var snapshot = new[] { RawAccount("acct-1") };

        Assert.Null(PlaidProvider.ExtractRawAccountMetadata(snapshot, "acct-missing"));
    }

    [Fact]
    public void ExtractRawAccountMetadata_is_null_when_the_snapshot_is_empty()
    {
        Assert.Null(PlaidProvider.ExtractRawAccountMetadata(Array.Empty<PlaidProvider.RawPlaidAccount>(), "acct-1"));
    }

    // ---- Issue #168: raw payload capture, end to end over a stubbed /transactions/sync ----

    // These objects carry `unmodelled` fields that Going.Plaid's entities do not have properties for.
    // That is the whole point: re-serializing the deserialized DTO would silently drop them, so their
    // survival is what proves the archive holds the bytes Plaid actually sent.
    private const string AddedTransaction =
        """{"transaction_id":"txn-1","account_id":"acct-1","amount":12.5,"date":"2026-07-15","original_description":"COFFEE SHOP #123","merchant_name":"Blue Bottle","pending":false,"unmodelled":{"personal_finance_category":["Food"]}}""";

    private const string SiblingTransaction =
        """{"transaction_id":"txn-2","account_id":"acct-2","amount":40.0,"date":"2026-07-16","original_description":"GAS STATION","merchant_name":"Shell","pending":false,"unmodelled":{"note":"sibling"}}""";

    private const string FirstAccount =
        """{"account_id":"acct-1","name":"Checking","balances":{"current":110.0,"available":100.0,"iso_currency_code":"USD"},"unmodelled":{"holder":"primary"}}""";

    private const string SecondAccount =
        """{"account_id":"acct-2","name":"Savings","balances":{"current":5000.0},"unmodelled":{"holder":"joint"}}""";

    private static PlaidProvider CreateProvider(string responseJson)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plaid:Enabled"] = "true",
                ["Plaid:ClientId"] = "test-client-id",
                ["Plaid:Secret"] = "test-secret",
                ["Plaid:Environment"] = "Sandbox",
            })
            .Build();

        return new PlaidProvider(config, new StubHttpClientFactory(responseJson), NullLoggerFactory.Instance);
    }

    private static Account StoredAccount(int id, string? accountId) => new()
    {
        Id = id,
        UserId = 1,
        Name = $"acct{id}",
        SourceType = "Plaid",
        ConnectionDetailsJson = JsonSerializer.Serialize(new PlaidConnectionDetails
        {
            AccessToken = "access-token",
            AccountId = accountId,
            Cursor = "cursor-current",
        }),
    };

    // has_more is false so the stub — which answers every request identically — drains in one page.
    private static string SyncResponse(string accounts, string added, string modified = "") =>
        $$"""
        {"accounts":[{{accounts}}],"added":[{{added}}],"modified":[{{modified}}],"removed":[],"next_cursor":"cursor-next","has_more":false,"request_id":"req-1"}
        """;

    [Fact]
    public async Task RawJson_is_the_transaction_object_exactly_as_Plaid_sent_it()
    {
        var provider = CreateProvider(SyncResponse(FirstAccount, AddedTransaction));

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal("txn-1", mapped.ProviderTransactionId);
        Assert.Equal(AddedTransaction, mapped.RawJson);
    }

    [Fact]
    public async Task AccountMetadataJson_is_the_account_object_exactly_as_Plaid_sent_it()
    {
        var provider = CreateProvider(SyncResponse(FirstAccount, AddedTransaction));

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(FirstAccount, results[1].AccountMetadataJson);

        using var parsed = JsonDocument.Parse(results[1].AccountMetadataJson!);
        // A field Going.Plaid's Account entity does not model survives — this is the account object,
        // not a re-serialization of the DTO.
        Assert.Equal("primary", parsed.RootElement.GetProperty("unmodelled").GetProperty("holder").GetString());
    }

    [Fact]
    public async Task ErrorsJson_is_empty_rather_than_null_because_Plaid_reports_none()
    {
        var provider = CreateProvider(SyncResponse(FirstAccount, AddedTransaction));

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(results[1].ErrorsJson);
        Assert.Empty(results[1].SkippedTransactionsJson);
    }

    [Fact]
    public async Task Raw_capture_is_correlated_per_account_across_a_shared_access_token()
    {
        var provider = CreateProvider(SyncResponse(
            $"{FirstAccount},{SecondAccount}",
            $"{AddedTransaction},{SiblingTransaction}"));

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1"), StoredAccount(2, accountId: "acct-2") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AddedTransaction, Assert.Single(results[1].Upserts).RawJson);
        Assert.Equal(FirstAccount, results[1].AccountMetadataJson);

        Assert.Equal(SiblingTransaction, Assert.Single(results[2].Upserts).RawJson);
        Assert.Equal(SecondAccount, results[2].AccountMetadataJson);
    }

    // The case that rules out correlating by transaction_id: Plaid can report the same transaction as
    // both added and modified, and the two objects differ. An id-keyed lookup could only hold one of
    // them, so one of the two upserts would be archived with the other's bytes.
    [Fact]
    public async Task A_transaction_reported_as_both_added_and_modified_keeps_each_version_of_its_bytes()
    {
        const string modifiedTransaction =
            """{"transaction_id":"txn-1","account_id":"acct-1","amount":13.75,"date":"2026-07-15","original_description":"COFFEE SHOP #123","merchant_name":"Blue Bottle","pending":false,"unmodelled":{"revision":2}}""";

        var provider = CreateProvider(SyncResponse(FirstAccount, AddedTransaction, modifiedTransaction));

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var upserts = results[1].Upserts;
        Assert.Equal(2, upserts.Count);
        Assert.All(upserts, u => Assert.Equal("txn-1", u.ProviderTransactionId));
        // Added entries come first, then modified — the ordering FetchTransactionsForConnectionAsync builds.
        Assert.Equal(AddedTransaction, upserts[0].RawJson);
        Assert.Equal(modifiedTransaction, upserts[1].RawJson);
    }

    // ---- Issue #168: the raw-page reader's degradation contract ----
    //
    // The archive is best-effort. Whatever is wrong with a page body, the reader must hand back exactly
    // as many entries as there are deserialized transactions — the caller zips the two, so a short list
    // would silently drop transactions from the sync itself. Empty capture is recoverable; a skewed
    // pairing that files one transaction's bytes under another's id is not.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")] // valid JSON, but not the response object
    public void ReadRawPage_degrades_to_empty_capture_without_losing_entries(string? rawJson)
    {
        var page = PlaidProvider.ReadRawPage(rawJson, addedCount: 2, modifiedCount: 1, NullLogger.Instance);

        Assert.Equal(new[] { "", "" }, page.Added);
        Assert.Equal(new[] { "" }, page.Modified);
        Assert.Empty(page.Accounts);
    }

    [Fact]
    public void ReadRawPage_refuses_to_pair_an_array_whose_length_disagrees_with_the_deserialized_one()
    {
        var page = PlaidProvider.ReadRawPage(
            SyncResponse(FirstAccount, AddedTransaction),
            // The deserialized view claims two added transactions; the body carries one. Correlating
            // positionally here would attach the wrong bytes, so nothing is captured for the array.
            addedCount: 2,
            modifiedCount: 0,
            NullLogger.Instance);

        Assert.Equal(new[] { "", "" }, page.Added);
        // A well-formed account snapshot in the same body is still captured.
        Assert.Equal(FirstAccount, Assert.Single(page.Accounts).Json);
    }

    [Fact]
    public void ReadRawPage_reads_each_object_verbatim_and_keeps_account_ids()
    {
        var page = PlaidProvider.ReadRawPage(
            SyncResponse($"{FirstAccount},{SecondAccount}", AddedTransaction, SiblingTransaction),
            addedCount: 1,
            modifiedCount: 1,
            NullLogger.Instance);

        Assert.Equal(AddedTransaction, Assert.Single(page.Added));
        Assert.Equal(SiblingTransaction, Assert.Single(page.Modified));
        Assert.Equal(new[] { "acct-1", "acct-2" }, page.Accounts.Select(a => a.AccountId));
        Assert.Equal(FirstAccount, page.Accounts[0].Json);
    }
}
