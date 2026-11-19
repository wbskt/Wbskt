using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Wbskt.Api.Client;

namespace Wbskt.Host.Tests.Clients;

/// <summary>
/// A .NET caller of the API can tell why it was refused and when to try again: the plan-limit numbers
/// and any other extension reach ApiProblem, and Retry-After reaches ApiException.
/// </summary>
public sealed class ApiExceptionTests
{
    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.example.test/api/v1/workspaces/x/clients")
    };

    [Fact]
    public async Task A_plan_limit_refusal_carries_the_plan_the_limit_and_the_count()
    {
        using var response = Response(HttpStatusCode.Conflict,
            """{"status":409,"code":"PLAN_CLIENT_LIMIT","detail":"Your plan allows 5 clients.","plan":"Free","limit":5,"current":5,"usedBy":["a"]}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => WbsktApiClient.EnsureSuccessAsync(response));

        Assert.Equal("PLAN_CLIENT_LIMIT", ex.Code);
        Assert.Equal("Free", ex.Problem!.Plan);
        Assert.Equal(5, ex.Problem.Limit);
        Assert.Equal(5, ex.Problem.Current);
        Assert.True(ex.Problem.Extensions!.ContainsKey("usedBy"));
        Assert.Null(ex.RetryAfter);
    }

    [Fact]
    public async Task A_503_says_when_to_try_again()
    {
        using var response = Response(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"UNAVAILABLE"}""");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<ApiException>(() => WbsktApiClient.EnsureSuccessAsync(response));

        Assert.Equal(TimeSpan.FromSeconds(5), ex.RetryAfter);
    }

    [Fact]
    public async Task A_retry_after_date_already_past_means_now()
    {
        using var response = Response(HttpStatusCode.TooManyRequests, """{"status":429,"code":"RATE_LIMITED"}""");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1));

        var ex = await Assert.ThrowsAsync<ApiException>(() => WbsktApiClient.EnsureSuccessAsync(response));

        Assert.Equal(TimeSpan.Zero, ex.RetryAfter);
    }
}
