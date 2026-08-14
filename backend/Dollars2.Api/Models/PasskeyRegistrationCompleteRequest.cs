using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class PasskeyRegistrationCompleteRequest
{
    [Required]
    public required string CredentialJson { get; set; }
}
