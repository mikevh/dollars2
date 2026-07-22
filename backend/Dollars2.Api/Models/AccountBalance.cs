namespace Dollars2.Api.Models;

public class AccountBalance
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime UpdatedOn { get; set; }
}
