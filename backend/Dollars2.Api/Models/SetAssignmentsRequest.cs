namespace Dollars2.Api.Models;

public class SetAssignmentsRequest
{
    public List<AssignmentEntry> Assignments { get; set; } = new();
}

public class AssignmentEntry
{
    public int LineItemId { get; set; }
    public decimal Amount { get; set; }
}
