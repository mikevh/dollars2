using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class AuthRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
}
