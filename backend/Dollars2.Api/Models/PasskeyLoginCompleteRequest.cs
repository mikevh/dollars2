using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class PasskeyLoginCompleteRequest
{
    [Required]
    public required string CredentialJson { get; set; }
}
