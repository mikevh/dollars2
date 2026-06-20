namespace Dollars2.Api.Models;

public class TransactionAssignment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int LineItemId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
