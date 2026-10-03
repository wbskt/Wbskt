using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Engine.Host.Controllers;

/// <summary>
/// The inbound event id a webhook delivery is deduplicated on. The dispatcher claims
/// <c>inbound-event:{id}</c> in dbo.IdempotencyKeys before starting anything, so two deliveries with
/// the same id start one run. Without an <c>Idempotency-Key</c> header every delivery gets a fresh
/// id, as before; with one, a sender's retry of the same delivery is dropped as a duplicate for as
/// long as the claim is kept (WorkflowEngineOptions.IdempotencyRetentionWindow, 24 hours by default).
/// </summary>
internal static class WebhookEventIds
{
    public const int MaxKeyLength = 255;

    public static bool IsValidKey(string key) =>
        key.Length is > 0 and <= MaxKeyLength && key.All(c => c is >= '\x21' and <= '\x7e');

    public static string For(Guid workspaceRef, string path, string? idempotencyKey, string? secret)
    {
        if (idempotencyKey is null)
        {
            return $"webhook:{workspaceRef}:{path}:{Guid.NewGuid()}";
        }

        // Hashed so a caller-chosen key of any length fits the 200-character claim column. The path
        // keeps one key from colliding across a workspace's webhooks, and the presented secret keeps
        // a caller who does not know it from claiming a key ahead of the real sender: the claim is
        // taken before the secret is checked, so without it a guessed key would block the delivery.
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{path}\n{secret}\n{idempotencyKey}"));
        return $"webhook:{workspaceRef}:key:{Convert.ToHexStringLower(digest)}";
    }
}
