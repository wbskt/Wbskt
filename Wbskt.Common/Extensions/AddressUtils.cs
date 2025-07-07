using System.Net;
using Microsoft.AspNetCore.Http;

namespace Wbskt.Common.Extensions;

public static class AddressUtils
{
    public static async Task<HostString[]> GetCurrentHostAddresses(ICollection<string>? addresses, bool isProduction = true)
    {
        var host = await GetIpAddress(isProduction);
        if (addresses == null || addresses.Count == 0)
        {
            throw new InvalidOperationException("server address feature is not initialized yet");
        }

        var ports = addresses.Select(a => new Uri(a).Port).ToArray();

        var hostStrings = new HostString[ports.Length];

        for (var i = 0; i < ports.Length; i++)
        {
            hostStrings[i] = new HostString(host, ports[i]);
        }

        return hostStrings;
    }

    public static async Task<string> GetIpAddress(bool isProduction)
    {
        if (isProduction)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri("https://ifconfig.me")
            };

            var result = await client.GetAsync("ip");
            if (result.IsSuccessStatusCode)
            {
                return await result.Content.ReadAsStringAsync();
            }

            return string.Empty;
        }

        var hostName = Dns.GetHostName();
        var hostEntry = await Dns.GetHostEntryAsync(hostName);
        var host = hostEntry.AddressList[1].MapToIPv4().ToString();
        return host;
    }
}
