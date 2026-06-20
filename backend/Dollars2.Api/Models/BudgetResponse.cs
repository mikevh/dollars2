namespace Dollars2.Api.Models;

public class BudgetResponse
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required List<BudgetGroupResponse> Groups { get; set; }
}

public class BudgetGroupResponse
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsIncome { get; set; }
    public int SortOrder { get; set; }
    public required List<LineItemResponse> LineItems { get; set; }
}

public class LineItemResponse
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal PlannedAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
}
