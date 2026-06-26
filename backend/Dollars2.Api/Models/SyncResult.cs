namespace Dollars2.Api.Models;

public class SyncResult
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string Status { get; set; } = "";
    public int TransactionCount { get; set; }
    public string? ErrorMessage { get; set; }
}
