using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Models.Admin;

namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// SHA-256 of a staff request's JSON without its idempotency key: what the staff member sent, so a retry
/// with the same key can be told apart from a different change that reuses it.
/// </summary>
public static class StaffRequestHash
{
    public static byte[] Of(IStaffRequest request)
    {
        var node = JsonSerializer.SerializeToNode(request, request.GetType())!.AsObject();
        node[nameof(IStaffRequest.Staff)]!.AsObject().Remove(nameof(StaffChange.IdempotencyKey));
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(node));
    }
}
