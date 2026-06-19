namespace Dollars2.Api.Models;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
