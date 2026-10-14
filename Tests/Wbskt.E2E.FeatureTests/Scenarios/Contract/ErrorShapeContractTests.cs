using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Contract;

/// <summary>
/// Every error status a caller can meet comes back as the same Problem Details body (#305): media
/// type <c>application/problem+json</c>, the <c>status</c> repeated, a <c>title</c>, the application
/// <c>code</c> a caller branches on and the <c>traceId</c> that finds the request in the host's logs.
/// One theory row per status, each provoked for real on a running host; a validation failure also
/// names the refused fields under <c>errors</c>, and a 429 says when to come back in <c>Retry-After</c>.
///
/// <para>
/// Two rows do not run in the main pass:
/// </para>
/// <list type="bullet">
/// <item><c>RateLimited429</c> spends the auth host's per-IP credential budget, which every scenario
/// shares, so it is gated on <c>E2E_RATE_LIMIT_TESTS=1</c> like <c>RateLimitingTests</c>. CI runs it
/// alone in its rate-limit pass, against an auth host restarted with the production limit. The
/// per-user <c>Api</c> limiter would not do instead: it is raised to 100,000 a minute in Development.</item>
/// <item><c>Unavailable503</c> has no route that answers 503 on demand against the Development hosts:
/// the 503s are an unreachable auth host or broker, an engine that is not the leader, and the internal
/// and engine API keys left unset, which only Production refuses. The row stays, skipped, so the
/// status is listed with the others and gains a case when one becomes provokable.</item>
/// </list>
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class ErrorShapeContractTests(ServicesFixture fixture)
{
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>One provoked failure: the response, what it must say, and what else to check on it.</summary>
    private sealed record Provoked(HttpResponseMessage Response, HttpStatusCode Status, string Code, Action<HttpResponseMessage, JsonElement>? Also = null);

    private static readonly Dictionary<string, Func<ServicesFixture, Task<Provoked>>> Cases = new()
    {
        // A create route whose body breaks its data annotations: the model-validation response.
        ["Validation400"] = async f =>
        {
            var user = await f.CreateUserAsync();
            var response = await f.Auth.As(user.Token).SendRawAsync(HttpMethod.Post, "/api/workspaces", new { name = "" });
            return new Provoked(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED", (_, problem) =>
            {
                problem.TryGetProperty("errors", out var errors).Should().BeTrue("a validation failure names the fields it refused");
                errors.ValueKind.Should().Be(JsonValueKind.Object, "errors maps each field to its messages");
                var fields = errors.EnumerateObject().ToList();
                fields.Should().Contain(field => string.Equals(field.Name, "name", StringComparison.OrdinalIgnoreCase), "the empty name is what was refused");
                foreach (var field in fields)
                {
                    field.Value.ValueKind.Should().Be(JsonValueKind.Array, $"errors.{field.Name} is a list of messages");
                    field.Value.EnumerateArray().ToList().Should().NotBeEmpty($"errors.{field.Name} says why").And
                        .OnlyContain(message => message.ValueKind == JsonValueKind.String && message.GetString()!.Length > 0, $"errors.{field.Name} holds messages");
                }
            });
        },

        // No token on a signed-in route: the bearer challenge, given its body by the status code pages.
        ["Unauthenticated401"] = async f =>
        {
            var response = await f.Management.As(null).GetPoliciesRawAsync(Guid.NewGuid());
            return new Provoked(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
        },

        // Signed in, naming a workspace that belongs to someone else.
        ["Forbidden403"] = async f =>
        {
            var owner = await f.CreateIsolatedWorkspaceAsync();
            var stranger = await f.CreateUserAsync();
            var response = await f.Management.As(stranger.Token).GetPoliciesRawAsync(owner.WorkspaceRef);
            return new Provoked(response, HttpStatusCode.Forbidden, "WORKSPACE_FORBIDDEN");
        },

        // In one's own workspace, a device that does not exist.
        ["NotFound404"] = async f =>
        {
            var ws = await f.CreateIsolatedWorkspaceAsync();
            var response = await f.Management.As(ws.Token).SendRawAsync(HttpMethod.Get, $"/api/workspaces/{ws.WorkspaceRef}/clients/{Guid.NewGuid()}");
            return new Provoked(response, HttpStatusCode.NotFound, "CLIENT_NOT_FOUND");
        },

        // A second role of the same name in one tenant.
        ["Conflict409"] = async f =>
        {
            var admin = await f.CreateUserAsync();
            var tenant = await f.GetTenantRefAsync(admin.Token);
            var name = $"e2e-contract-{Guid.NewGuid():N}";
            await f.CreateRoleAsync(admin.Token, tenant, name);
            var response = await f.Auth.As(admin.Token).CreateRoleRawAsync(tenant, new CreateRoleRequest(name, null));
            return new Provoked(response, HttpStatusCode.Conflict, "AUTH_ROLE_CONFLICT");
        },

        // Failed logins from this address past the credential limit. Opt-in: see the class summary.
        ["RateLimited429"] = async f =>
        {
            Skip.IfNot(E2EConfig.RateLimitTestsEnabled,
                "Exhausts the shared per-IP credential budget. Set E2E_RATE_LIMIT_TESTS=1 and run it alone against an auth host with the production limit (CI's rate-limit pass does).");

            for (var attempt = 0; attempt < E2EConfig.AuthPermitLimit + 3; attempt++)
            {
                var response = await f.LoginRawAsync($"nobody-{Guid.NewGuid():N}@test.local", "wrong-password");
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new Provoked(response, HttpStatusCode.TooManyRequests, "RATE_LIMITED", (r, _) =>
                    {
                        r.Headers.RetryAfter.Should().NotBeNull("a 429 says when to come back");
                        var retryAfter = r.Headers.RetryAfter!.Delta ?? (r.Headers.RetryAfter.Date - DateTimeOffset.UtcNow);
                        retryAfter.Should().NotBeNull("Retry-After is a number of seconds or a date");
                        retryAfter!.Value.Should().BePositive("Retry-After points to a moment still ahead");
                    });
                }

                response.Dispose();
            }

            throw new InvalidOperationException(
                $"{E2EConfig.AuthPermitLimit + 3} failed logins never met a 429. Is the auth host running with RateLimiting__Authentication__PermitLimit={E2EConfig.AuthPermitLimit}?");
        },

        // Nothing answers 503 on demand against the Development hosts: see the class summary.
        ["Unavailable503"] = _ =>
        {
            Skip.If(true,
                "No route answers 503 deterministically here: the 503s are an unreachable auth host or broker, a non-leader engine, " +
                "and an unset Internal:ApiKey or Engine:InboundApiKey, which only Production refuses.");
            throw new InvalidOperationException("unreachable");
        },
    };

    /// <summary>One row per error status.</summary>
    public static TheoryData<string> ErrorCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Cases.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ErrorCases))]
    public async Task CONTRACT_ERR_01_EveryErrorStatus_IsAProblemWithCodeAndTraceId(string errorCase)
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var provoked = await Cases[errorCase](fixture);
        using var response = provoked.Response;
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(provoked.Status, $"{errorCase}: {text}");
        (response.Content.Headers.ContentType?.MediaType).Should().Be(ProblemMediaType, $"{errorCase}: every error body is Problem Details");

        using var body = JsonDocument.Parse(text);
        var problem = body.RootElement;
        problem.ValueKind.Should().Be(JsonValueKind.Object, errorCase);

        problem.TryGetProperty("status", out var status).Should().BeTrue($"{errorCase}: the body repeats the status");
        status.GetInt32().Should().Be((int)provoked.Status, errorCase);

        problem.TryGetProperty("title", out var title).Should().BeTrue($"{errorCase}: a problem has a title");
        title.GetString().Should().NotBeNullOrWhiteSpace(errorCase);

        problem.TryGetProperty("code", out var code).Should().BeTrue($"{errorCase}: the code is what a caller branches on");
        code.GetString().Should().Be(provoked.Code, errorCase);

        problem.TryGetProperty("traceId", out var traceId).Should().BeTrue($"{errorCase}: the trace id ties the answer to the host's logs");
        traceId.ValueKind.Should().Be(JsonValueKind.String, errorCase);
        traceId.GetString().Should().NotBeNullOrWhiteSpace(errorCase);

        provoked.Also?.Invoke(response, problem);
    }
}
