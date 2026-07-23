using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Default <see cref="IOutboundAddressGuard"/>: allows only http/https targets that resolve to
/// public unicast addresses. Loopback, private (RFC1918/CGNAT), link-local, unique-local, multicast
/// and reserved ranges are blocked. Operators who genuinely need to call an internal endpoint can
/// set <c>WorkflowEngine:Webhook:AllowPrivateNetworkTargets = true</c> to disable the address check.
///
/// The host is resolved once here and the ranges checked; a determined attacker could still exploit
/// DNS rebinding in the window between this check and the socket connect. Closing that fully needs a
/// connect-time callback on the HTTP handler - this guard is the first, cheap line of defence.
/// </summary>
internal sealed class OutboundAddressGuard : IOutboundAddressGuard
{
    private readonly bool _allowPrivateTargets;

    public OutboundAddressGuard(IConfiguration configuration)
    {
        _allowPrivateTargets = configuration.GetValue<bool>("WorkflowEngine:Webhook:AllowPrivateNetworkTargets");
    }

    public async ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return OutboundAddressDecision.Block($"scheme '{uri.Scheme}' is not allowed");
        }

        if (_allowPrivateTargets)
        {
            return OutboundAddressDecision.Allow();
        }

        string host = uri.Host;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
            }
            catch (SocketException)
            {
                // Unresolvable now: let the HTTP send fail as a normal (retryable) network error
                // rather than converting a transient DNS blip into a hard block.
                return OutboundAddressDecision.Allow();
            }
        }

        foreach (IPAddress ip in addresses)
        {
            if (IsBlocked(ip))
            {
                return OutboundAddressDecision.Block($"target '{host}' resolves to a private or reserved address ({ip})");
            }
        }

        return OutboundAddressDecision.Allow();
    }

    private static bool IsBlocked(IPAddress address)
    {
        IPAddress ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(ip.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsBlockedV6(ip),
            _ => true // unknown family: block
        };
    }

    private static bool IsBlockedV4(byte[] b) => b[0] switch
    {
        0 => true,                                   // 0.0.0.0/8 (incl. unspecified)
        10 => true,                                  // 10.0.0.0/8 private
        100 => b[1] >= 64 && b[1] <= 127,            // 100.64.0.0/10 CGNAT
        127 => true,                                 // 127.0.0.0/8 loopback
        169 => b[1] == 254,                          // 169.254.0.0/16 link-local (incl. cloud metadata)
        172 => b[1] >= 16 && b[1] <= 31,             // 172.16.0.0/12 private
        192 => b[1] == 168,                          // 192.168.0.0/16 private
        >= 224 => true,                              // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved + broadcast
        _ => false
    };

    private static bool IsBlockedV6(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
        {
            return true;
        }

        byte[] b = ip.GetAddressBytes();
        if (Array.TrueForAll(b, x => x == 0))
        {
            return true; // :: unspecified
        }

        return (b[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
    }
}
