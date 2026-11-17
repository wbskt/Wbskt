namespace Wbskt.Management.Host.Services.Clients;

public class AuthenticationForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthenticationForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A caller that names the token itself (a check outside any request) keeps it.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && request.Headers.Authorization is null)
        {
            var token = TokenOf(httpContext);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>The bearer token a request carries: its header, or the query SignalR puts it in.</summary>
    internal static string? TokenOf(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            return authHeader.Substring("Bearer ".Length).Trim();
        }
        
        // Fallback for SignalR/WebSocket transport
        return httpContext.Request.Query["access_token"];
    }
}
