using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// Every error a host answers with is Problem Details carrying <c>code</c> and <c>traceId</c> (#305),
/// including the ones no controller writes: a fault, a bearer challenge, a rate-limit rejection, an
/// unknown route. These run the shared pipeline on a real listener.
/// </summary>
public sealed class ProblemDetailsPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Mock.Of<IEventBus>());
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NobodyHandler>("Test", null);
        builder.Services.AddDefaultDenyAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectWithProblem();
            options.AddFixedWindowLimiter("one", o =>
            {
                o.PermitLimit = 1;
                o.Window = TimeSpan.FromMinutes(1);
            });
        });

        _app = builder.Build();
        _app.UseWbsktPipeline();
        _app.MapGet("/fault", HttpContext () => throw new InvalidOperationException("Server=db;Password=hunter2")).AllowAnonymous();
        _app.MapGet("/signed-in", () => "hello");
        _app.MapGet("/limited", () => "ok").AllowAnonymous().RequireRateLimiting("one");
        await _app.StartAsync();

        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_fault_is_a_500_problem_that_does_not_leak_the_exception()
    {
        var (response, problem) = await GetAsync("/fault");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_ERROR", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("hunter2", problem.GetRawText());
    }

    [Fact]
    public async Task The_bearer_challenge_is_a_401_problem()
    {
        var (response, problem) = await GetAsync("/signed-in");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_UNAUTHORIZED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_rate_limited_request_is_a_429_problem_that_says_when_to_retry()
    {
        await _client.GetAsync("/limited");
        var (response, problem) = await GetAsync("/limited");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("RATE_LIMITED", problem.GetProperty("code").GetString());
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_path_no_endpoint_serves_is_still_a_problem()
    {
        // Default-deny reaches it before routing gives up, so an anonymous caller is challenged.
        var (response, problem) = await GetAsync("/nothing-here");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_UNAUTHORIZED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public void A_model_validation_failure_is_validation_failed_with_the_field_messages()
    {
        var factory = _app.Services.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.InvalidModelStateResponseFactory;
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("name", "The name is required.");

        var result = factory(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState));

        Assert.Equal(Problems.ValidationFailed, ProblemAssert.Is(result, StatusCodes.Status400BadRequest));
        var problem = Assert.IsType<ValidationProblemDetails>(((ObjectResult)result).Value);
        Assert.Equal(["The name is required."], problem.Errors["name"]);
        Assert.Equal("The name is required.", problem.Extensions[Problems.MessageKey]);
    }

    private async Task<(HttpResponseMessage Response, JsonElement Problem)> GetAsync(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(Problems.ContentType, response.Content.Headers.ContentType?.MediaType);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        return (response, problem);
    }

    /// <summary>Authenticates no one, so every protected endpoint is challenged.</summary>
    private sealed class NobodyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
