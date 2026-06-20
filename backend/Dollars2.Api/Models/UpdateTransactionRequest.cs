namespace Dollars2.Api.Models;

public class UpdateTransactionRequest
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
