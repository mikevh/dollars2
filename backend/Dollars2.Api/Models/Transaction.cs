namespace Dollars2.Api.Models;

public class Transaction
{
    public int Id { get; set; }
    public int? AccountId { get; set; }
    public int UserId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPending { get; set; }
    public bool IsManual { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
