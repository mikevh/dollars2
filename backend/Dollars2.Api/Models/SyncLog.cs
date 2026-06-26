namespace Dollars2.Api.Models;

public class SyncLog
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public DateTime SyncedAt { get; set; }
    public string Status { get; set; } = "";
    public int TransactionCount { get; set; }
    public string? ErrorMessage { get; set; }
}
