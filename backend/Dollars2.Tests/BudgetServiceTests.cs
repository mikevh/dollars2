using Dollars2.Api.Models;
using Dollars2.Api.Services;

namespace Dollars2.Tests;

public class BudgetServiceTests
{
    private static Account Account(int id, bool includeInBudget) => new()
    {
        Id = id,
        UserId = 1,
        Name = $"acct{id}",
        SourceType = "SimpleFIN",
        IncludeInBudget = includeInBudget,
    };

    private static AccountBalance Balance(int accountId, decimal balance) => new()
    {
        Id = accountId,
        AccountId = accountId,
        Balance = balance,
    };

    [Fact]
    public void Sums_latest_balances_for_included_accounts_only()
    {
        var accounts = new[]
        {
            Account(1, includeInBudget: true),
            Account(2, includeInBudget: true),
            Account(3, includeInBudget: false), // excluded → ignored even though it has a balance
        };
        var balances = new[]
        {
            Balance(1, 100.50m),
            Balance(2, 250m),
            Balance(3, 9999m),
        };

        var total = BudgetService.ComputeAccountBalanceTotal(accounts, balances);

        Assert.Equal(350.50m, total);
    }

    [Fact]
    public void Included_account_without_a_balance_contributes_zero()
    {
        var accounts = new[]
        {
            Account(1, includeInBudget: true),
            Account(2, includeInBudget: true), // no balance row
        };
        var balances = new[] { Balance(1, 400m) };

        var total = BudgetService.ComputeAccountBalanceTotal(accounts, balances);

        Assert.Equal(400m, total);
    }

    [Fact]
    public void No_included_accounts_yields_zero()
    {
        var accounts = new[] { Account(1, includeInBudget: false) };
        var balances = new[] { Balance(1, 500m) };

        var total = BudgetService.ComputeAccountBalanceTotal(accounts, balances);

        Assert.Equal(0m, total);
    }
}
