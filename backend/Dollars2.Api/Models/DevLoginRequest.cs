using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class DevLoginRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
}
