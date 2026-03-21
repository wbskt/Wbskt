namespace Wbskt.Client.Windows.Models;

public record ClientSettings(
    string BaseApiUrl,
    string BaseSocketUrl,
    string DeviceName,
    string? PolicyPin = null
);
