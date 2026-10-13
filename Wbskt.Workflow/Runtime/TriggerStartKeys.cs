using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The key one inbound event starts one trigger registration under: the registration's idempotency
/// claim and the run's <c>dbo.Runs.TriggerStartKey</c>. The event id is hashed because it can be longer
/// than the 200-character columns (a webhook path is part of it). A registration belongs to one
/// workspace, so the key is scoped to it even where event ids are not.
/// </summary>
internal static class TriggerStartKeys
{
    public static string For(string inboundEventId, long registrationId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(inboundEventId));
        return $"trigger-start:{registrationId}:{Convert.ToHexStringLower(digest)}";
    }
}
