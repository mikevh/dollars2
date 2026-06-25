namespace Dollars2.Api.Models;

public class TransactionCountsResponse
{
    public int New { get; set; }
    public int Tracked { get; set; }
    public int Deleted { get; set; }
    public int Pending { get; set; }
}
