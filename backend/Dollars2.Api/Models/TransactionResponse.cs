namespace Dollars2.Api.Models;

public class TransactionResponse
{
    public int Id { get; set; }
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public string Payee { get; set; } = "";
    public string Memo { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPending { get; set; }
    public bool IsManual { get; set; }
    public List<TransactionAssignmentResponse> Assignments { get; set; } = new();
}

public class TransactionAssignmentResponse
{
    public int Id { get; set; }
    public int LineItemId { get; set; }
    public string LineItemName { get; set; } = "";
    public decimal Amount { get; set; }
}
