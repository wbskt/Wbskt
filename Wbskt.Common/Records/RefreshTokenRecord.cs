namespace Wbskt.Common.Records;

public record RefreshTokenRecord
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public required string Token { get; init; }
    public DateTime Expires { get; init; }
    public DateTime Created { get; init; }
    public required string CreatedByIp { get; init; }
    public DateTime? Revoked { get; init; }
    public string? RevokedByIp { get; init; }
    public string? ReplacedByToken { get; init; }
}
