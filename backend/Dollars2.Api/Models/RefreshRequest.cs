using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class RefreshRequest
{
    [Required]
    public required string RefreshToken { get; set; }
}
