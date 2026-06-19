namespace Dollars2.Api.Models;

public class BudgetGroup
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public required string Name { get; set; }
    public bool IsIncome { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
