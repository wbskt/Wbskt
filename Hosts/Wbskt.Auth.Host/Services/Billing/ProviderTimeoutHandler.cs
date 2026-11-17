namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Gives up on a call to the payment provider after <paramref name="timeout"/> with an
/// <see cref="HttpRequestException"/>, the way every other failure to reach it is reported. The
/// <c>HttpClient.Timeout</c> it replaces threw a <see cref="TaskCanceledException"/> that no caller
/// caught as a provider failure, so a slow provider surfaced as a 500.
/// </summary>
internal sealed class ProviderTimeoutHandler(TimeSpan timeout) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, timer.Token);

            // HttpClient reads the body after the handlers return, out of this timer's reach; read it here.
            await response.Content.LoadIntoBufferAsync(timer.Token);
            return response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timer.IsCancellationRequested)
        {
            response?.Dispose();
            throw new HttpRequestException($"The payment provider did not answer {request.Method} {request.RequestUri?.AbsolutePath} within {timeout.TotalSeconds:0} s.", new TimeoutException(ex.Message, ex));
        }
    }
}
