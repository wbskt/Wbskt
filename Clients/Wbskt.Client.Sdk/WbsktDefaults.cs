namespace Wbskt.Client.Sdk;

/// <summary>Where <see cref="WbsktClient(string, string)"/> connects when no URLs are given: the hosted Wbskt service.</summary>
public static class WbsktDefaults
{
    public const string ApiUrl = "https://api.dev.wbskt.com";
    public const string SocketUrl = "wss://ws.dev.wbskt.com";
}
