using Dollars2.Api.Providers;

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
}
