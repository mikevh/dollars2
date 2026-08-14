using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class PasskeyRegistrationOptionsRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string RegistrationKey { get; set; }
}
