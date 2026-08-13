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
        });

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
        });

        Assert.Equal("COFFEE SHOP #123", mapped.Description);
        Assert.Equal("Blue Bottle", mapped.Payee);
        // Plaid amounts are positive for outflow; ours are negative for expenses.
        Assert.Equal(-12.50m, mapped.Amount);
    }

    // ---- Issue #259: raw response capture, end to end over a stubbed /transactions/sync ----

    private const string AddedTransaction =
        """{"transaction_id":"txn-1","account_id":"acct-1","amount":12.5,"date":"2026-07-15","original_description":"COFFEE SHOP #123","merchant_name":"Blue Bottle","pending":false,"unmodelled":{"personal_finance_category":["Food"]}}""";

    private const string SiblingTransaction =
        """{"transaction_id":"txn-2","account_id":"acct-2","amount":40.0,"date":"2026-07-16","original_description":"GAS STATION","merchant_name":"Shell","pending":false,"unmodelled":{"note":"sibling"}}""";

    private const string FirstAccount =
        """{"account_id":"acct-1","name":"Checking","balances":{"current":110.0,"available":100.0,"iso_currency_code":"USD"},"unmodelled":{"holder":"primary"}}""";

    private const string SecondAccount =
        """{"account_id":"acct-2","name":"Savings","balances":{"current":5000.0},"unmodelled":{"holder":"joint"}}""";

    private static PlaidProvider CreateProvider(string responseJson) =>
        CreateProvider(new StubHttpClientFactory(responseJson));

    private static PlaidProvider CreateProvider(IHttpClientFactory httpClientFactory)
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

        return new PlaidProvider(config, httpClientFactory, NullLoggerFactory.Instance);
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

    // hasMore defaults to false so the single-response stub — which answers every request identically —
    // drains in one page rather than looping forever.
    private static string SyncResponse(
        string accounts, string added, string modified = "", bool hasMore = false, string cursor = "cursor-next") =>
        $$"""
        {"accounts":[{{accounts}}],"added":[{{added}}],"modified":[{{modified}}],"removed":[],"next_cursor":"{{cursor}}","has_more":{{(hasMore ? "true" : "false")}},"request_id":"req-1"}
        """;

    [Fact]
    public async Task FetchTransactionsForConnectionAsync_captures_the_response_body_verbatim_for_the_archive()
    {
        var response = SyncResponse(FirstAccount, AddedTransaction);
        var provider = CreateProvider(response);

        var fetchResult = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var mapped = Assert.Single(fetchResult.Results[1].Upserts);
        Assert.Equal("txn-1", mapped.ProviderTransactionId);
        Assert.Equal(response, Assert.Single(fetchResult.RawResponseBodies));
    }

    [Fact]
    public async Task Upserts_are_attributed_per_account_across_a_shared_access_token()
    {
        var provider = CreateProvider(SyncResponse(
            $"{FirstAccount},{SecondAccount}",
            $"{AddedTransaction},{SiblingTransaction}"));

        var fetchResult = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1"), StoredAccount(2, accountId: "acct-2") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("txn-1", Assert.Single(fetchResult.Results[1].Upserts).ProviderTransactionId);
        Assert.Equal("txn-2", Assert.Single(fetchResult.Results[2].Upserts).ProviderTransactionId);
    }

    // The case that rules out correlating by transaction_id: Plaid can report the same transaction as
    // both added and modified, with different content — both versions must survive as separate upserts,
    // not be deduplicated down to one.
    [Fact]
    public async Task A_transaction_reported_as_both_added_and_modified_keeps_both_versions()
    {
        const string modifiedTransaction =
            """{"transaction_id":"txn-1","account_id":"acct-1","amount":13.75,"date":"2026-07-15","original_description":"COFFEE SHOP #123","merchant_name":"Blue Bottle","pending":false,"unmodelled":{"revision":2}}""";

        var provider = CreateProvider(SyncResponse(FirstAccount, AddedTransaction, modifiedTransaction));

        var fetchResult = await provider.FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var upserts = fetchResult.Results[1].Upserts;
        Assert.Equal(2, upserts.Count);
        Assert.All(upserts, u => Assert.Equal("txn-1", u.ProviderTransactionId));
        // Added entries come first, then modified — the ordering FetchTransactionsForConnectionAsync builds.
        Assert.Equal(-12.5m, upserts[0].Amount);
        Assert.Equal(-13.75m, upserts[1].Amount);
    }

    // ---- Issue #259: capture across a paged /transactions/sync stream ----

    // The same account as FirstAccount, as a later page reports it: a moved balance and a marker field.
    private const string FirstAccountLaterPage =
        """{"account_id":"acct-1","name":"Checking","balances":{"current":97.25,"available":87.25,"iso_currency_code":"USD"},"unmodelled":{"holder":"primary","page":2}}""";

    [Fact]
    public async Task A_transaction_added_on_one_page_and_modified_on_the_next_keeps_both_versions()
    {
        const string modifiedOnPageTwo =
            """{"transaction_id":"txn-1","account_id":"acct-1","amount":13.75,"date":"2026-07-15","original_description":"COFFEE SHOP #123","merchant_name":"Blue Bottle","pending":false,"unmodelled":{"revision":2}}""";

        var page1 = SyncResponse(FirstAccount, AddedTransaction, hasMore: true, cursor: "cursor-page-1");
        var page2 = SyncResponse(FirstAccountLaterPage, added: "", modified: modifiedOnPageTwo, cursor: "cursor-page-2");
        var pages = new QueuedHttpClientFactory(page1, page2);

        var fetchResult = await CreateProvider(pages).FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, pages.RemainingResponses);

        var upserts = fetchResult.Results[1].Upserts;
        Assert.Equal(2, upserts.Count);
        Assert.All(upserts, u => Assert.Equal("txn-1", u.ProviderTransactionId));
        Assert.Equal(-12.5m, upserts[0].Amount);
        Assert.Equal(-13.75m, upserts[1].Amount);

        // One raw response body archived per page.
        Assert.Equal(new[] { page1, page2 }, fetchResult.RawResponseBodies);
    }

    // The recorded balance is taken from the last page that carried an account snapshot, so a stream
    // whose snapshot moves between pages reports the account's latest state, not its first.
    [Fact]
    public async Task Balance_comes_from_the_last_page_that_carried_a_snapshot()
    {
        var pages = new QueuedHttpClientFactory(
            SyncResponse(FirstAccount, AddedTransaction, hasMore: true, cursor: "cursor-page-1"),
            SyncResponse(FirstAccountLaterPage, added: "", cursor: "cursor-page-2"));

        var fetchResult = await CreateProvider(pages).FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(97.25m, fetchResult.Results[1].Balance);
        // The advanced cursor from the final page is what gets persisted.
        Assert.Contains("cursor-page-2", fetchResult.Results[1].UpdatedConnectionDetailsJson!);
    }

    // A page with no account snapshot must not blank out the one an earlier page established — the
    // snapshot is only replaced when a page actually carries accounts.
    [Fact]
    public async Task A_later_page_without_an_account_snapshot_leaves_the_earlier_one_intact()
    {
        var pages = new QueuedHttpClientFactory(
            SyncResponse(FirstAccount, AddedTransaction, hasMore: true, cursor: "cursor-page-1"),
            SyncResponse(accounts: "", added: "", cursor: "cursor-page-2"));

        var fetchResult = await CreateProvider(pages).FetchTransactionsForConnectionAsync(
            new[] { StoredAccount(1, accountId: "acct-1") },
            since: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(110.0m, fetchResult.Results[1].Balance);
        // The page-1 transaction survives the empty page rather than being dropped by it.
        Assert.Equal("txn-1", Assert.Single(fetchResult.Results[1].Upserts).ProviderTransactionId);
    }
}
