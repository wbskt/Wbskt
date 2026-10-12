using System.Net;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Integrations;

/// <summary>The HTTP plumbing every HTTP-based channel shares.</summary>
internal static class HttpChannelSupport
{
    /// <summary>
    /// The named client every integration call goes out on. It does not follow redirects, so a 3xx
    /// cannot carry a credential, or the request, somewhere its owner did not point it.
    /// </summary>
    public const string ClientName = "workflow-integrations";

    /// <summary>
    /// Sends <paramref name="request"/> after checking the address guard (unless <paramref name="guard"/>
    /// is null, for a target fixed by configuration). Returns the response, or the failure.
    /// </summary>
    public static async Task<(HttpResponseMessage? Response, ChannelFailure? Failure)> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        IOutboundAddressGuard? guard,
        string serviceName,
        CancellationToken ct)
    {
        if (guard is not null)
        {
            var decision = await guard.EvaluateAsync(request.RequestUri!, ct);
            if (!decision.Allowed)
            {
                return (null, new ChannelFailure(ChannelFailureKind.BlockedTarget, $"{serviceName} is at an address this engine may not call ({decision.Reason}).", false));
            }
        }

        try
        {
            return (await client.SendAsync(request, ct), null);
        }
        catch (HttpRequestException)
        {
            // Not the exception's message: it can quote the request URL, which carries the credential
            // for Telegram and Discord.
            return (null, new ChannelFailure(ChannelFailureKind.NetworkError, $"Could not reach {serviceName}.", true));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, new ChannelFailure(ChannelFailureKind.NetworkError, $"{serviceName} did not answer in time.", true));
        }
    }

    /// <summary>
    /// An HTTP error answer as a failure. 401 and 403 are the credential; 429 and 5xx are worth trying
    /// again; any other 4xx will fail the same way next time.
    /// </summary>
    public static ChannelFailure FromStatus(HttpStatusCode statusCode, string serviceName, string? detail = null)
    {
        int status = (int)statusCode;
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";
        return status is 401 or 403
            ? new ChannelFailure(ChannelFailureKind.AuthFailed, $"{serviceName} refused the credential ({status}){suffix}.", false, status)
            : new ChannelFailure(ChannelFailureKind.HttpError, $"{serviceName} answered {status}{suffix}.", status >= 500 || status == 429, status);
    }
}
