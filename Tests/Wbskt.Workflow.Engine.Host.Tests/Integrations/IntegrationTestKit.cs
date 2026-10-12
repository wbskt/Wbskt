using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Integrations;

namespace Wbskt.Workflow.Engine.Host.Tests.Integrations;

/// <summary>Master keys, ciphers and JSON for the integration tests.</summary>
internal static class IntegrationTestKit
{
    public const string BotToken = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    public static string Key(byte fill) => Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    public static IntegrationOptions Options(byte activeKeyId = 1, bool insecure = false, params (string Id, string Value)[] keys) => new()
    {
        Keys = (keys.Length == 0 ? [("1", Key(1))] : keys).ToDictionary(k => k.Id, k => k.Value),
        ActiveKeyId = activeKeyId,
        AllowInsecureTransport = insecure
    };

    public static IntegrationCipher Cipher(IntegrationOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    public static JsonElement ToJson(object value) => JsonSerializer.SerializeToElement(value);

    public static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
