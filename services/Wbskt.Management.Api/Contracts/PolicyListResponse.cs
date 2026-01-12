namespace Wbskt.Management.Api.Contracts;

public record PolicyListResponse
{
    public required int TotalCount { get; init; }
    public required List<PolicyResponse> Items { get; init; }
}
