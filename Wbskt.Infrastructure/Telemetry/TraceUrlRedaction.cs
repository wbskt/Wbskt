using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.Http;

namespace Wbskt.Infrastructure.Telemetry;

/// <summary>
/// Keeps the path of an outbound request out of its trace span for clients whose URLs carry a secret:
/// a Telegram call is <c>/bot&lt;token&gt;/sendMessage</c>, a Discord webhook's path is the credential,
/// and a workflow's Call URL step may point anywhere. The HttpClient instrumentation records
/// <c>url.full</c> with only the query redacted, so without this every such call put the secret in the
/// trace store (#957).
/// </summary>
/// <remarks>
/// A client opts in with <see cref="RedactUrlInTraces"/>, which marks each of its requests. The span of
/// a marked request keeps its scheme and host (<c>https://api.telegram.org/[redacted]</c>), which is
/// what a trace needs to show where the time went, and loses <c>url.path</c> and <c>url.query</c>. The
/// enrich callback rewrites the tags when the span starts; the processor writes them again when it
/// ends, in case anything set them in between.
/// </remarks>
public static class TraceUrlRedaction
{
    public static readonly HttpRequestOptionsKey<bool> RedactUrl = new("wbskt.redact-url");

    internal const string RedactedTag = "wbskt.url.redacted";
    private const string FullTag = "url.full";
    private const string PathTag = "url.path";
    private const string QueryTag = "url.query";

    /// <summary>Marks every request this client sends, so its span names the host and not the path.</summary>
    public static IHttpClientBuilder RedactUrlInTraces(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler(() => new MarkingHandler());

    /// <summary>The HttpClient instrumentation's settings: enrich marked requests' spans with the redacted URL.</summary>
    public static void Configure(HttpClientTraceInstrumentationOptions options) =>
        options.EnrichWithHttpRequestMessage = Enrich;

    internal static void Enrich(Activity activity, HttpRequestMessage request)
    {
        if (!request.Options.TryGetValue(RedactUrl, out bool redact) || !redact || request.RequestUri is not { IsAbsoluteUri: true } uri)
        {
            return;
        }

        string redacted = $"{uri.Scheme}://{uri.Authority}/[redacted]";
        activity.SetTag(RedactedTag, redacted);
        Apply(activity, redacted);
    }

    private static void Apply(Activity activity, string redacted)
    {
        activity.SetTag(FullTag, redacted);
        activity.SetTag(PathTag, null);
        activity.SetTag(QueryTag, null);
    }

    /// <summary>Writes the redaction again as a marked span ends, and drops the marker.</summary>
    internal sealed class Processor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            if (data.GetTagItem(RedactedTag) is string redacted)
            {
                Apply(data, redacted);
                data.SetTag(RedactedTag, null);
            }
        }
    }

    private sealed class MarkingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.Set(RedactUrl, true);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
