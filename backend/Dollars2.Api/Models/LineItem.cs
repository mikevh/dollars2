namespace Dollars2.Api.Models;

public class LineItem
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public required string Name { get; set; }
    public decimal PlannedAmount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public int? PreviousLineItemId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
