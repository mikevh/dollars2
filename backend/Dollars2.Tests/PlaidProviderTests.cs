using Dollars2.Api.Providers;
using RemovedTransaction = Going.Plaid.Entity.RemovedTransaction;
using PlaidAccount = Going.Plaid.Entity.Account;
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
}
