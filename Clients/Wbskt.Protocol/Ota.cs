using System.Text.Json.Serialization;

namespace Wbskt.Protocol;

/// <summary>
/// The payload of <see cref="MessageTypes.OtaUpdate"/>. It carries a link to the firmware, not the
/// firmware: an image is 1–2 MB and a message at most 64 KB.
/// </summary>
/// <param name="Url">Where the device downloads the image. Always <c>https</c>.</param>
/// <param name="Sha256">The image's SHA-256, 64 hex digits; the device checks it before booting the image.</param>
/// <param name="Version">The version the image reports once installed.</param>
/// <param name="Size">The image's size in bytes, so a device can refuse one too big for its partition before downloading.</param>
public sealed record OtaUpdatePayload(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("version")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Version = null,
    [property: JsonPropertyName("size")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? Size = null);

/// <summary>The payload of <see cref="MessageTypes.OtaStatus"/>.</summary>
/// <param name="CommandId">The <see cref="MessageTypes.OtaUpdate"/> this is about.</param>
/// <param name="State">One of <see cref="OtaStates"/>.</param>
/// <param name="Progress">While downloading: percent, 0–100.</param>
/// <param name="Version">The version being installed, or installed.</param>
/// <param name="Error">Why the update was rejected or failed.</param>
public sealed record OtaStatusPayload(
    [property: JsonPropertyName("commandId")] string? CommandId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("progress")] int? Progress = null,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("error")] string? Error = null);

/// <summary>The states a device reports in <see cref="OtaStatusPayload.State"/>, in the order they happen.</summary>
public static class OtaStates
{
    /// <summary>The device will install it.</summary>
    public const string Accepted = "accepted";

    /// <summary>The image is downloading; <see cref="OtaStatusPayload.Progress"/> says how far.</summary>
    public const string Downloading = "downloading";

    /// <summary>The device restarted into the new firmware and it connected. Sent by the new firmware.</summary>
    public const string Installed = "installed";

    /// <summary>The update went wrong partway, and the device runs its previous firmware.</summary>
    public const string Failed = "failed";

    /// <summary>The device would not start it: OTA off, a bad URL or digest, too big, or another under way.</summary>
    public const string Rejected = "rejected";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Accepted, Downloading, Installed, Failed, Rejected };
}
