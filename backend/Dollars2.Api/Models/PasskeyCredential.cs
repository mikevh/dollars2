namespace Dollars2.Api.Models;

public class PasskeyCredential
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public required byte[] AttestationObject { get; set; }
    public required byte[] ClientDataJson { get; set; }
    public long SignCount { get; set; }
    public string? Transports { get; set; }
    public bool IsUserVerified { get; set; }
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
