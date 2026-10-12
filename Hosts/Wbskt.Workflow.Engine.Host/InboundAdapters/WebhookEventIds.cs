using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// The inbound event id a webhook delivery is deduplicated on. The dispatcher claims
/// <c>inbound-event:{id}</c> in dbo.IdempotencyKeys before starting anything, so two deliveries with
/// the same id start one run. Without an <c>Idempotency-Key</c> header the id is the bus message's,
/// so a redelivery of the same message starts no second run; with one, a sender's retry of the same
/// delivery is dropped as a duplicate for as long as the claim is kept
/// (WorkflowEngineOptions.IdempotencyRetentionWindow, 24 hours by default).
/// </summary>
internal static class WebhookEventIds
{
    public static string For(Guid workspaceRef, string path, string? idempotencyKey, string? secretHash, string messageId)
    {
        if (idempotencyKey is null)
        {
            return $"webhook:{workspaceRef}:{path}:{messageId}";
        }

        // Hashed so a caller-chosen key of any length fits the 200-character claim column. The path
        // keeps one key from colliding across a workspace's webhooks, and the presented secret's hash keeps
        // a caller who does not know it from claiming a key ahead of the real sender: the claim is
        // taken before the secret is checked, so without it a guessed key would block the delivery.
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{path}\n{secretHash}\n{idempotencyKey}"));
        return $"webhook:{workspaceRef}:key:{Convert.ToHexStringLower(digest)}";
    }
}
