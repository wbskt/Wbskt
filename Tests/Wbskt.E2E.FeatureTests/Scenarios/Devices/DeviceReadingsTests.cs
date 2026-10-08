using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// Numbers a device reports in its state are kept as a history, which the management API returns
/// summarised per bucket of time for a chart, or as CSV.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceReadingsTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_READ_01_ReportedNumbers_AreKeptAsAHistory()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var deviceName = $"e2e-readings-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        await using (var device = new WbsktClient(
                         new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null),
                         new InMemoryClientStorage(clientRef, secret)))
        {
            await device.StartAsync();
            await device.ReportStateAsync(new Dictionary<string, object?> { ["temp"] = 4.5, ["door"] = "open" });
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await device.ReportStateAsync(new Dictionary<string, object?> { ["temp"] = 6.5 });

            // Ingestion is asynchronous (socket host → bus → management host), so wait for both.
            var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("o");
            var to = DateTimeOffset.UtcNow.AddHours(1).ToString("o");
            var url = Readings(workspace, clientRef, $"?name=temp&bucket=1d&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

            JsonElement[] buckets = [];
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var response = await fixture.SendAsync(HttpMethod.Get, url, token);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                buckets = body.RootElement.GetProperty("buckets").EnumerateArray().Select(b => b.Clone()).ToArray();
                if (buckets.Sum(b => b.GetProperty("count").GetInt64()) >= 2)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            buckets.Sum(b => b.GetProperty("count").GetInt64()).Should().Be(2, "both reports carried a temperature");
            buckets.Min(b => b.GetProperty("min").GetDouble()).Should().Be(4.5);
            buckets.Max(b => b.GetProperty("max").GetDouble()).Should().Be(6.5);
            buckets.Should().AllSatisfy(b => b.GetProperty("start").GetString().Should().EndWith("Z"));
        }

        // Strings are state, not readings: only the temperatures are exported.
        var csv = await fixture.SendAsync(HttpMethod.Get, Readings(workspace, clientRef, "/csv"), token);
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("name,deviceTime,receivedAt,value,late");
        lines.Skip(1).Select(l => l.Split(',')[3]).Should().Equal("4.5", "6.5");
        lines.Skip(1).Should().AllSatisfy(l => l.Should().StartWith("temp,").And.EndWith(",false"));
    }

    [SkippableFact]
    public async Task DEV_READ_02_BadQueries_AreRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "readings-queries");

        (await fixture.SendAsync(HttpMethod.Get, Readings(workspace, clientRef, ""), token)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a chart is of one variable");
        (await fixture.SendAsync(HttpMethod.Get, Readings(workspace, clientRef, "?name=temp&bucket=1s"), token)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a day in seconds is too many buckets");

        // An unknown device and another workspace's device get the same answer, so a guessed
        // reference does not tell whether it names a real device.
        (await fixture.SendAsync(HttpMethod.Get, Readings(workspace, Guid.NewGuid(), "?name=temp"), token)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        var (otherToken, otherWorkspace) = await fixture.RegisterAndLoginUserAsync();
        (await fixture.SendAsync(HttpMethod.Get, Readings(otherWorkspace, clientRef, "?name=temp"), otherToken)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    private static string Readings(Guid workspace, Guid clientRef, string rest) =>
        ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/clients/{clientRef}/readings{rest}");
}
