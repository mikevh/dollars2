using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class CreateGroupRequest
{
    [Required]
    [MaxLength(256)]
    public required string Name { get; set; }
}
