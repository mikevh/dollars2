using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BudgetService
{
    private readonly DbSession _dbSession;
    private readonly BudgetRepository _budgetRepo;
    private readonly BudgetGroupRepository _groupRepo;
    private readonly LineItemRepository _lineItemRepo;
    private readonly TransactionAssignmentRepository _assignmentRepo;
    private readonly AccountRepository _accountRepo;
    private readonly AccountBalanceRepository _balanceRepo;

    public BudgetService(DbSession dbSession, BudgetRepository budgetRepo, BudgetGroupRepository groupRepo, LineItemRepository lineItemRepo, TransactionAssignmentRepository assignmentRepo, AccountRepository accountRepo, AccountBalanceRepository balanceRepo)
    {
        _dbSession = dbSession;
        _budgetRepo = budgetRepo;
        _groupRepo = groupRepo;
        _lineItemRepo = lineItemRepo;
        _assignmentRepo = assignmentRepo;
        _accountRepo = accountRepo;
        _balanceRepo = balanceRepo;
    }

    public async Task<DollarsApiResponse<BudgetResponse>> GetBudgetAsync(int userId, int year, int month)
    {
        var budget = await _budgetRepo.GetByMonthAsync(userId, year, month);
        if (budget is null)
        {
            return DollarsApiResponse<BudgetResponse>.Fail("Budget not found.", "BUDGET_NOT_FOUND");
        }

        return DollarsApiResponse<BudgetResponse>.Success(await BuildBudgetResponseAsync(budget));
    }

    public async Task<DollarsApiResponse<BudgetResponse>> CreateBudgetAsync(int userId, int year, int month)
    {
        var now = DateTime.UtcNow;
        if (year < now.Year || (year == now.Year && month < now.Month))
        {
            return DollarsApiResponse<BudgetResponse>.Fail("Cannot create a budget for a past month.", "PAST_MONTH");
        }

        var existing = await _budgetRepo.GetByMonthAsync(userId, year, month);
        if (existing is not null)
        {
            return DollarsApiResponse<BudgetResponse>.Fail("Budget already exists for this month.", "BUDGET_EXISTS");
        }

        var previous = await _budgetRepo.GetPreviousAsync(userId, year, month);
        if (previous is null && !(year == now.Year && month == now.Month))
        {
            var prevYear = month == 1 ? year - 1 : year;
            var prevMonth = month == 1 ? 12 : month - 1;
            return DollarsApiResponse<BudgetResponse>.Fail($"Budget for {prevYear}/{prevMonth} must exist first.", "PREVIOUS_BUDGET_REQUIRED");
        }

        _dbSession.BeginTransaction();
        try
        {
            var budgetId = await _budgetRepo.CreateAsync(userId, year, month);

            if (previous is not null)
            {
                await CopyBudgetStructureAsync(previous.Id, budgetId);
            }
            else
            {
                var groupId = await _groupRepo.CreateAsync(budgetId, "Income", 0);
                await _lineItemRepo.CreateAsync(groupId, budgetId, "Paycheck", 0, 0, isIncome: true);
            }

            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        var budget = (await _budgetRepo.GetByMonthAsync(userId, year, month))!;
        return DollarsApiResponse<BudgetResponse>.Success(await BuildBudgetResponseAsync(budget));
    }

    public async Task<DollarsApiResponse<BudgetGroupResponse>> CreateGroupAsync(int budgetId, string name, int userId)
    {
        var budget = await GetBudgetByIdAndVerifyOwnerAsync(budgetId, userId);
        if (budget is null)
        {
            return DollarsApiResponse<BudgetGroupResponse>.Fail("Budget not found.", "BUDGET_NOT_FOUND");
        }

        var maxSort = await _groupRepo.GetMaxSortOrderAsync(budgetId);
        var groupId = await _groupRepo.CreateAsync(budgetId, name, maxSort + 1);
        var group = (await _groupRepo.GetByIdAsync(groupId))!;

        return DollarsApiResponse<BudgetGroupResponse>.Success(new BudgetGroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            SortOrder = group.SortOrder,
            LineItems = new List<LineItemResponse>()
        });
    }

    public async Task<DollarsApiResponse<BudgetGroupResponse>> UpdateGroupAsync(int id, string name, int userId)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group is null)
        {
            return DollarsApiResponse<BudgetGroupResponse>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        if (!await VerifyGroupOwnershipAsync(group, userId))
        {
            return DollarsApiResponse<BudgetGroupResponse>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        await _groupRepo.UpdateAsync(id, name);
        group.Name = name;

        var lineItems = await _lineItemRepo.GetByGroupIdAsync(id);
        var lineItemResponses = new List<LineItemResponse>();
        foreach (var item in lineItems)
        {
            lineItemResponses.Add(await MapLineItemAsync(item));
        }
        return DollarsApiResponse<BudgetGroupResponse>.Success(new BudgetGroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            SortOrder = group.SortOrder,
            LineItems = lineItemResponses
        });
    }

    public async Task<DollarsApiResponse<bool>> DeleteGroupAsync(int id, int userId)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group is null)
        {
            return DollarsApiResponse<bool>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        if (!await VerifyGroupOwnershipAsync(group, userId))
        {
            return DollarsApiResponse<bool>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        if (await _groupRepo.HasLineItemsAsync(id))
        {
            return DollarsApiResponse<bool>.Fail("Cannot delete a group that has line items.", "GROUP_HAS_LINE_ITEMS");
        }

        await _groupRepo.DeleteAsync(id);
        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<bool>> ReorderGroupsAsync(int budgetId, int[] ids, int userId)
    {
        var budget = await GetBudgetByIdAndVerifyOwnerAsync(budgetId, userId);
        if (budget is null)
        {
            return DollarsApiResponse<bool>.Fail("Budget not found.", "BUDGET_NOT_FOUND");
        }

        var groups = await _groupRepo.GetByBudgetIdAsync(budgetId);
        var validIds = groups.Select(g => g.Id).ToHashSet();
        if (ids.Length != validIds.Count || ids.Any(id => !validIds.Contains(id)))
        {
            return DollarsApiResponse<bool>.Fail("Must include all group IDs for this budget.", "INVALID_GROUP_IDS");
        }

        _dbSession.BeginTransaction();
        try
        {
            for (int i = 0; i < ids.Length; i++)
            {
                await _groupRepo.UpdateSortOrderAsync(ids[i], i);
            }
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<LineItemResponse>> CreateLineItemAsync(int groupId, string name, decimal plannedAmount, bool isIncome, int userId)
    {
        if (!Money.IsWholeCents(plannedAmount))
        {
            return DollarsApiResponse<LineItemResponse>.Fail(Money.SubCentMessage, Money.SubCentCode);
        }

        var group = await _groupRepo.GetByIdAsync(groupId);
        if (group is null)
        {
            return DollarsApiResponse<LineItemResponse>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        if (!await VerifyGroupOwnershipAsync(group, userId))
        {
            return DollarsApiResponse<LineItemResponse>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        var maxSort = await _lineItemRepo.GetMaxSortOrderAsync(groupId);
        var itemId = await _lineItemRepo.CreateAsync(groupId, group.BudgetId, name, plannedAmount, maxSort + 1, isIncome);
        var item = (await _lineItemRepo.GetByIdAsync(itemId))!;

        return DollarsApiResponse<LineItemResponse>.Success(await MapLineItemAsync(item));
    }

    public async Task<DollarsApiResponse<LineItemResponse>> UpdateLineItemAsync(int id, string name, decimal plannedAmount, string? notes, int userId)
    {
        if (!Money.IsWholeCents(plannedAmount))
        {
            return DollarsApiResponse<LineItemResponse>.Fail(Money.SubCentMessage, Money.SubCentCode);
        }

        var item = await _lineItemRepo.GetByIdAsync(id);
        if (item is null)
        {
            return DollarsApiResponse<LineItemResponse>.Fail("Line item not found.", "LINE_ITEM_NOT_FOUND");
        }

        if (!await VerifyLineItemOwnershipAsync(item, userId))
        {
            return DollarsApiResponse<LineItemResponse>.Fail("Line item not found.", "LINE_ITEM_NOT_FOUND");
        }

        await _lineItemRepo.UpdateAsync(id, name, plannedAmount, notes);
        item.Name = name;
        item.PlannedAmount = plannedAmount;
        item.Notes = notes ?? "";

        return DollarsApiResponse<LineItemResponse>.Success(await MapLineItemAsync(item));
    }

    public async Task<DollarsApiResponse<bool>> DeleteLineItemAsync(int id, int userId)
    {
        var item = await _lineItemRepo.GetByIdAsync(id);
        if (item is null)
        {
            return DollarsApiResponse<bool>.Fail("Line item not found.", "LINE_ITEM_NOT_FOUND");
        }

        var group = await _groupRepo.GetByIdAsync(item.GroupId);
        if (group is null || !await VerifyGroupOwnershipAsync(group, userId))
        {
            return DollarsApiResponse<bool>.Fail("Line item not found.", "LINE_ITEM_NOT_FOUND");
        }

        _dbSession.BeginTransaction();
        try
        {
            // The count and the delete must share this transaction: CountIncomeInBudgetAsync takes
            // an update lock held until commit, so a second concurrent delete of a different income
            // item blocks here instead of also reading the pre-delete count — without that, two
            // near-simultaneous deletes of separate income items could both see "2 remain" and both
            // proceed, leaving zero.
            if (item.IsIncome)
            {
                var incomeCount = await _lineItemRepo.CountIncomeInBudgetAsync(group.BudgetId);
                if (incomeCount <= 1)
                {
                    _dbSession.Rollback();
                    return DollarsApiResponse<bool>.Fail("Cannot delete the last income line item.", "CANNOT_DELETE_LAST_INCOME");
                }
            }

            await _lineItemRepo.ClearPreviousLinkAsync(id);
            await _assignmentRepo.DeleteByLineItemIdAsync(id);
            await _lineItemRepo.DeleteAsync(id);
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<bool>> ReorderLineItemsAsync(int groupId, int[] ids, int userId)
    {
        var group = await _groupRepo.GetByIdAsync(groupId);
        if (group is null)
        {
            return DollarsApiResponse<bool>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        if (!await VerifyGroupOwnershipAsync(group, userId))
        {
            return DollarsApiResponse<bool>.Fail("Group not found.", "GROUP_NOT_FOUND");
        }

        var lineItems = await _lineItemRepo.GetByGroupIdAsync(groupId);
        var validIds = lineItems.Select(li => li.Id).ToHashSet();
        if (ids.Length != validIds.Count || ids.Any(id => !validIds.Contains(id)))
        {
            return DollarsApiResponse<bool>.Fail("Must include all line item IDs for this group.", "INVALID_LINE_ITEM_IDS");
        }

        _dbSession.BeginTransaction();
        try
        {
            for (int i = 0; i < ids.Length; i++)
            {
                await _lineItemRepo.UpdateSortOrderAsync(ids[i], i);
            }
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        return DollarsApiResponse<bool>.Success(true);
    }

    private async Task<BudgetResponse> BuildBudgetResponseAsync(Budget budget)
    {
        var groups = (await _groupRepo.GetByBudgetIdAsync(budget.Id)).ToList();
        var lineItems = (await _lineItemRepo.GetByBudgetIdAsync(budget.Id)).ToList();

        var lineItemIds = lineItems.Select(i => i.Id).ToList();
        var netByLineItemId = lineItemIds.Count > 0
            ? (await _assignmentRepo.GetNetAssignedByLineItemIdsAsync(lineItemIds)).ToDictionary(n => n.LineItemId, n => n.Amount)
            : new Dictionary<int, decimal>();

        var rolloverLineItemIds = lineItems.Where(i => !i.IsIncome).Select(i => i.Id).ToList();
        var rolloverByLineItemId = rolloverLineItemIds.Count > 0
            ? (await _lineItemRepo.GetRolloverBatchAsync(rolloverLineItemIds)).ToDictionary(r => r.LineItemId, r => r.Amount)
            : new Dictionary<int, decimal>();

        var lineItemsByGroupId = lineItems.ToLookup(i => i.GroupId);
        var groupResponses = groups.Select(group => new BudgetGroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            SortOrder = group.SortOrder,
            LineItems = lineItemsByGroupId[group.Id]
                .Select(item => MapLineItem(item, netByLineItemId.GetValueOrDefault(item.Id), rolloverByLineItemId.GetValueOrDefault(item.Id)))
                .ToList()
        }).ToList();

        var accounts = (await _accountRepo.GetByUserIdAsync(budget.UserId)).ToList();
        var includedIds = accounts.Where(a => a.IncludeInBudget).Select(a => a.Id).ToList();
        var latestBalances = includedIds.Count > 0
            ? await _balanceRepo.GetLatestPerAccountAsync(includedIds)
            : Enumerable.Empty<AccountBalance>();

        return new BudgetResponse
        {
            Id = budget.Id,
            Year = budget.Year,
            Month = budget.Month,
            AccountBalanceTotal = ComputeAccountBalanceTotal(accounts, latestBalances),
            Groups = groupResponses
        };
    }

    // Sum of the latest captured balance for each IncludeInBudget account. Accounts with no stored
    // balance contribute 0; excluded accounts never affect the total.
    public static decimal ComputeAccountBalanceTotal(IEnumerable<Account> accounts, IEnumerable<AccountBalance> latestBalances)
    {
        var balanceByAccount = latestBalances
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => g.First().Balance);

        decimal total = 0;
        foreach (var account in accounts)
        {
            if (!account.IncludeInBudget)
            {
                continue;
            }
            if (balanceByAccount.TryGetValue(account.Id, out var balance))
            {
                total += balance;
            }
        }
        return total;
    }

    private async Task CopyBudgetStructureAsync(int sourceBudgetId, int targetBudgetId)
    {
        var sourceGroups = await _groupRepo.GetByBudgetIdAsync(sourceBudgetId);

        foreach (var sourceGroup in sourceGroups)
        {
            var newGroupId = await _groupRepo.CreateAsync(targetBudgetId, sourceGroup.Name, sourceGroup.SortOrder);
            var sourceItems = await _lineItemRepo.GetByGroupIdAsync(sourceGroup.Id);

            foreach (var sourceItem in sourceItems)
            {
                await _lineItemRepo.CreateAsync(newGroupId, targetBudgetId, sourceItem.Name, sourceItem.PlannedAmount, sourceItem.SortOrder, sourceItem.IsIncome, sourceItem.Id, sourceItem.Notes);
            }
        }
    }

    private async Task<Budget?> GetBudgetByIdAndVerifyOwnerAsync(int budgetId, int userId)
    {
        var budget = await _budgetRepo.GetByIdAsync(budgetId);
        if (budget is null || budget.UserId != userId)
        {
            return null;
        }
        return budget;
    }

    private async Task<bool> VerifyGroupOwnershipAsync(BudgetGroup group, int userId)
    {
        var budget = await _budgetRepo.GetByIdAsync(group.BudgetId);
        return budget is not null && budget.UserId == userId;
    }

    private async Task<bool> VerifyLineItemOwnershipAsync(LineItem item, int userId)
    {
        var budget = await _budgetRepo.GetByIdAsync(item.BudgetId);
        return budget is not null && budget.UserId == userId;
    }

    private async Task<LineItemResponse> MapLineItemAsync(LineItem item)
    {
        var net = await _assignmentRepo.GetNetAssignedByLineItemIdAsync(item.Id);

        decimal rollover = 0;
        if (!item.IsIncome)
        {
            rollover = await _lineItemRepo.GetRolloverAsync(item.Id);
        }

        return MapLineItem(item, net, rollover);
    }

    // One net figure drives both columns: spending is the negation (debits read positive), and
    // received is the net itself. No Math.Abs — that is what erased credits on expense items.
    private static LineItemResponse MapLineItem(LineItem item, decimal net, decimal rollover)
    {
        return new LineItemResponse
        {
            Id = item.Id,
            Name = item.Name,
            PlannedAmount = item.PlannedAmount,
            IsIncome = item.IsIncome,
            SpentAmount = -net,
            ReceivedAmount = net,
            RolloverAmount = rollover,
            SortOrder = item.SortOrder,
            Notes = item.Notes
        };
    }
}
