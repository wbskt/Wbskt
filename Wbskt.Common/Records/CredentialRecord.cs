namespace Wbskt.Common.Records;

public record CredentialRecord
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public required string IntegrationType { get; init; }
    public required string Name { get; init; }
    public required string EncryptedCredentials { get; init; }
    public DateTime LastModified { get; init; }
}
