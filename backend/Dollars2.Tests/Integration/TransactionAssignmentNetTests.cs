using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the net-assignment read path (issue #71): <see
/// cref="TransactionAssignmentRepository.GetNetAssignedByLineItemIdAsync"/> sums every assignment
/// for a line item regardless of sign, so a positive (income) transaction assigned to an expense
/// item is no longer silently dropped. Soft-deleted transactions stay excluded.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransactionAssignmentNetTests
{
    private readonly MsSqlContainerFixture _fixture;

    public TransactionAssignmentNetTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Net_of_a_single_debit_is_negative_so_spent_reads_positive()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var seed = await SeedAsync(db, "net-debit@example.com");
            var repository = new TransactionAssignmentRepository(db);
            await AssignAsync(db, repository, seed, -50m);

            var net = await repository.GetNetAssignedByLineItemIdAsync(seed.LineItemId);

            Assert.Equal(-50m, net);
            Assert.Equal(50m, -net); // SpentAmount
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Net_of_a_single_credit_on_an_expense_item_is_positive_so_spent_reads_negative()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            // The reported case: a +$690.89 manual transaction assigned to an expense line item.
            var seed = await SeedAsync(db, "net-credit@example.com");
            var repository = new TransactionAssignmentRepository(db);
            await AssignAsync(db, repository, seed, 690.89m);

            var net = await repository.GetNetAssignedByLineItemIdAsync(seed.LineItemId);

            Assert.Equal(690.89m, net);
            Assert.Equal(-690.89m, -net); // SpentAmount
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Debit_and_credit_net_against_each_other()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var seed = await SeedAsync(db, "net-mixed@example.com");
            var repository = new TransactionAssignmentRepository(db);
            await AssignAsync(db, repository, seed, -50m);
            await AssignAsync(db, repository, seed, 20m);

            var net = await repository.GetNetAssignedByLineItemIdAsync(seed.LineItemId);

            Assert.Equal(-30m, net);
            Assert.Equal(30m, -net); // SpentAmount
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Income_item_nets_a_reversal_out_of_received()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            // Paycheck +625 followed by a -100 returned deposit → Received 525, not 625.
            var seed = await SeedAsync(db, "net-income@example.com");
            var repository = new TransactionAssignmentRepository(db);
            await AssignAsync(db, repository, seed, 625m);
            await AssignAsync(db, repository, seed, -100m);

            var net = await repository.GetNetAssignedByLineItemIdAsync(seed.LineItemId);

            Assert.Equal(525m, net); // ReceivedAmount
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Soft_deleted_transactions_are_excluded_on_both_signs()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var seed = await SeedAsync(db, "net-deleted@example.com");
            var repository = new TransactionAssignmentRepository(db);
            await AssignAsync(db, repository, seed, -50m);
            await AssignAsync(db, repository, seed, 20m);
            await AssignAsync(db, repository, seed, -500m, isDeleted: true);
            await AssignAsync(db, repository, seed, 900m, isDeleted: true);

            var net = await repository.GetNetAssignedByLineItemIdAsync(seed.LineItemId);

            Assert.Equal(-30m, net);
        }
        finally
        {
            db.Rollback();
        }
    }

    private sealed record Seed(int UserId, int LineItemId);

    private static async Task AssignAsync(
        DbSession db,
        TransactionAssignmentRepository repository,
        Seed seed,
        decimal amount,
        bool isDeleted = false)
    {
        var transactionId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, Date, Description, Payee, Memo, Amount, IsDeleted, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, '2026-07-15', 'Test', '', '', @amount, @isDeleted, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId = seed.UserId, amount, isDeleted },
            db.CurrentTransaction);

        await repository.CreateAsync(transactionId, seed.LineItemId, amount);
    }

    private static async Task<Seed> SeedAsync(DbSession db, string email)
    {
        var userId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);

        var budgetId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Budgets (UserId, [Year], [Month], CreatedAt, UpdatedAt)
              VALUES (@userId, 2026, 7, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId },
            db.CurrentTransaction);

        var groupId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO BudgetGroups (BudgetId, Name, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@budgetId, 'Group', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId },
            db.CurrentTransaction);

        var lineItemId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@groupId, 'Item', 300, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId },
            db.CurrentTransaction);

        return new Seed(userId, lineItemId);
    }
}
