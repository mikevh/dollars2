namespace Dollars2.Api.Models;

public class SyncStatusResponse
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public DateTime? LastSyncedAt { get; set; }
    public string? LastStatus { get; set; }
    public int? LastTransactionCount { get; set; }
    public string? LastErrorMessage { get; set; }
}
