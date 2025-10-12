namespace Wbskt.Common.Records;

public record ServerRecord
{
    public int Id { get; init; }
    public required string PublicDomainName { get; init; }
    public int Status { get; init; }
    public DateTime LastModified { get; init; }
}
