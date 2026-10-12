using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure;

/// <summary>
/// The one error body every host answers with (#305): RFC 9457 Problem Details, with the
/// application's <c>code</c> and the request's <c>traceId</c> as extensions. A Result's error, an
/// escaped exception, a model-validation failure, an authentication challenge, a rate-limit rejection
/// and the engine's own refusals all come out in this shape.
/// </summary>
/// <remarks>
/// <c>message</c> repeats <c>detail</c> for one release: the body before this one was
/// <c>{ code, message, type }</c> and the console shows <c>message</c>. Remove it once the console
/// reads <c>detail</c>.
/// </remarks>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    public const string ValidationFailed = "VALIDATION_FAILED";

    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";
    public const string MessageKey = "message";

    public static ProblemDetails Create(int status, string code, string? detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail
        };
        problem.Extensions[CodeKey] = code;
        problem.Extensions[MessageKey] = detail;
        return problem;
    }

    public static ProblemDetails From(Error error, int status) => Create(status, error.Code, error.Message);

    /// <summary>The application code a problem carries, or null.</summary>
    public static string? CodeOf(ProblemDetails problem) =>
        problem.Extensions.TryGetValue(CodeKey, out var code) ? code?.ToString() : null;

    /// <summary>The handle that ties a response to its log lines: the trace when there is one, else the request.</summary>
    public static string? TraceId(HttpContext? http) => Activity.Current?.Id ?? http?.TraceIdentifier;

    /// <summary>The code for a problem nothing more specific named: a bare status from the framework.</summary>
    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "BAD_REQUEST",
        StatusCodes.Status401Unauthorized => "AUTH_UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "AUTH_FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        StatusCodes.Status500InternalServerError => "INTERNAL_ERROR",
        StatusCodes.Status503ServiceUnavailable => "UNAVAILABLE",
        _ => $"HTTP_{status}"
    };

    /// <summary>Writes a problem straight to the response, for middleware that has no action result to return.</summary>
    public static Task WriteAsync(HttpContext http, int status, string code, string? detail)
    {
        var problem = Create(status, code, detail);
        problem.Extensions[TraceIdKey] = TraceId(http);
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(problem, options: null, contentType: ContentType);
    }

    /// <summary>
    /// Problem Details for everything the framework answers by itself, and the model-validation
    /// response. <c>UseWbsktPipeline</c> adds the status code pages when this is registered, which
    /// give a body to the statuses written without one: the bearer challenge (401) and forbid (403),
    /// the rate limiter's 429, an unmatched route.
    /// </summary>
    public static IServiceCollection AddWbsktProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            int status = problem.Status ?? context.HttpContext.Response.StatusCode;
            string? firstFieldError = (problem as ValidationProblemDetails)?.Errors.Values.SelectMany(messages => messages).FirstOrDefault();
            problem.Extensions.TryAdd(CodeKey, problem is ValidationProblemDetails ? ValidationFailed : CodeFor(status));
            problem.Extensions[TraceIdKey] = TraceId(context.HttpContext);
            problem.Extensions.TryAdd(MessageKey, firstFieldError ?? problem.Detail ?? problem.Title);
        });

        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more fields are invalid."
            };
            string? first = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault();
            problem.Extensions[CodeKey] = ValidationFailed;
            problem.Extensions[TraceIdKey] = TraceId(context.HttpContext);
            problem.Extensions[MessageKey] = first ?? problem.Title;

            return new BadRequestObjectResult(problem) { ContentTypes = { ContentType } };
        });

        return services;
    }

    /// <summary>
    /// A 429 that says when to come back: <c>Retry-After</c> from the limiter's lease when it knows
    /// (the window limiters do), and the Problem Details body from the status code pages.
    /// </summary>
    public static RateLimiterOptions RejectWithProblem(this RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };
        return options;
    }
}
