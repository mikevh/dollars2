namespace Dollars2.Api.Models;

public class Account
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string? ConnectionDetailsJson { get; set; }
    public bool IncludeInBudget { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
