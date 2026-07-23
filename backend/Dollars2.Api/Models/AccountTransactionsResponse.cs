namespace Dollars2.Api.Models;

public class AccountTransactionsResponse
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public List<TransactionResponse> Transactions { get; set; } = new();
}
