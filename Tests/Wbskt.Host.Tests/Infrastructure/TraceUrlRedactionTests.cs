using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// An integration's request URL carries its secret (a Telegram bot token, a Discord webhook's path), and
/// the HttpClient instrumentation records <c>url.full</c> with only the query redacted (#957). A real
/// request against a local listener, through the same instrumentation settings the hosts use.
/// </summary>
public sealed class TraceUrlRedactionTests
{
    private const string Secret = "123456:ABCdefSecretBotToken";

    [Fact]
    public async Task A_marked_clients_span_names_the_host_and_not_the_secret_path()
    {
        var spans = await SendAsync(marked: true);

        var span = Assert.Single(spans);
        var tags = span.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString());
        Assert.DoesNotContain(tags.Values, v => v is not null && v.Contains(Secret, StringComparison.Ordinal));
        Assert.EndsWith("/[redacted]", tags["url.full"]);
        Assert.False(tags.ContainsKey("url.path"));
        Assert.False(tags.ContainsKey(TraceUrlRedaction.RedactedTag));
        Assert.DoesNotContain(Secret, span.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_clients_keep_their_path()
    {
        var spans = await SendAsync(marked: false);

        var span = Assert.Single(spans);
        Assert.Contains("/bot", span.GetTagItem("url.full")?.ToString());
    }

    private static async Task<List<Activity>> SendAsync(bool marked)
    {
        var spans = new List<Activity>();
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var answering = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.Close();
        });

        using (var tracing = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation(TraceUrlRedaction.Configure)
            .AddProcessor(new TraceUrlRedaction.Processor())
            .AddProcessor(new Capture(spans))
            .Build())
        {
            var services = new ServiceCollection();
            var client = services.AddHttpClient("integrations");
            if (marked)
            {
                client.RedactUrlInTraces();
            }

            await using var provider = services.BuildServiceProvider();
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("integrations");
            using var response = await http.PostAsync($"http://127.0.0.1:{port}/bot{Secret}/sendMessage?chat=1", null);
            await answering;
            tracing.ForceFlush();
        }

        // The instrumentation sees every HttpClient in the process, so keep only this test's own request.
        return spans.Where(s => s.GetTagItem("server.port") is int p && p == port).ToList();
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private sealed class Capture(List<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (spans)
            {
                spans.Add(data);
            }
        }
    }
}
