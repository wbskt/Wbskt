using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// xUnit collection fixture shared across all E2E feature tests.
///
/// On construction the fixture probes each of the four dev hosts with a short
/// timeout and sets <see cref="HostsAvailable"/> accordingly. All helper methods
/// are safe to call only when hosts are available; individual tests guard this
/// via <c>Skip.IfNot(fixture.HostsAvailable, …)</c>.
/// </summary>
public sealed class ServicesFixture : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Shared cert-ignoring HttpClient for all REST calls inside the fixture helpers.
    private readonly HttpClient _http;

    public bool HostsAvailable { get; }

    public ServicesFixture()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        HostsAvailable = ProbeAllHostsAsync().GetAwaiter().GetResult();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Health probe
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> ProbeAllHostsAsync()
    {
        var urls = new[]
        {
            E2EConfig.AuthBaseUrl,
            E2EConfig.ManagementBaseUrl,
            E2EConfig.SocketHttpBaseUrl,
            E2EConfig.WorkflowBaseUrl
        };

        foreach (var url in urls)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await _http.GetAsync(url, cts.Token);

                // Any HTTP response (even 404 / 401) means the host is up.
                // Only treat a connection-level failure as "unavailable".
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auth helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs in as the seeded administrator (admin@wbskt.com / Password123!) which has the
    /// Admin role (all permissions) and owns the Default Workspace (internal Id = 1).
    /// Returns the bearer token and the admin's workspace RefId. This is the privileged
    /// identity used for permission-gated operations (policy creation, workflow publish).
    /// </summary>
    public async Task<(string Token, Guid WorkspaceRef)> LoginAsAdminAsync()
    {
        var loginResp = await _http.PostAsJsonAsync(
            $"{E2EConfig.AuthBaseUrl}/api/auth/login",
            new { Email = E2EConfig.AdminEmail, Password = E2EConfig.AdminPassword });
        loginResp.EnsureSuccessStatusCode();

        var login = await loginResp.Content.ReadFromJsonAsync<LoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty admin login response.");

        var workspaceRef = await ResolveOrCreateWorkspaceAsync(login.AccessToken);

        return (login.AccessToken, workspaceRef);
    }

    /// <summary>
    /// Registers a brand-new user (unique per run) and logs in, returning only the bearer
    /// token. Does not resolve or create a workspace, so it exercises the public signup
    /// path without depending on workspace permissions.
    /// </summary>
    public async Task<string> RegisterAndLoginNewUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"e2e-{suffix}@test.local";
        var password = $"P@ss{suffix}!";
        var username = $"e2e-{suffix}";

        var registerResp = await _http.PostAsJsonAsync(
            $"{E2EConfig.AuthBaseUrl}/api/auth/register",
            new { Username = username, Email = email, Password = password });
        registerResp.EnsureSuccessStatusCode();

        var loginResp = await _http.PostAsJsonAsync(
            $"{E2EConfig.AuthBaseUrl}/api/auth/login",
            new { Email = email, Password = password });
        loginResp.EnsureSuccessStatusCode();

        var login = await loginResp.Content.ReadFromJsonAsync<LoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty login response.");

        return login.AccessToken;
    }

    /// <summary>
    /// Registers a brand-new user (unique per run) and logs in.
    /// Returns the bearer token and the first workspace RefId.
    /// If the user has no workspaces yet a new one is created automatically.
    /// </summary>
    public async Task<(string Token, Guid WorkspaceRef)> RegisterAndLoginUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"e2e-{suffix}@test.local";
        var password = $"P@ss{suffix}!";
        var username = $"e2e-{suffix}";

        // 1. Register
        var registerResp = await _http.PostAsJsonAsync(
            $"{E2EConfig.AuthBaseUrl}/api/auth/register",
            new { Username = username, Email = email, Password = password });
        registerResp.EnsureSuccessStatusCode();

        // 2. Login
        var loginResp = await _http.PostAsJsonAsync(
            $"{E2EConfig.AuthBaseUrl}/api/auth/login",
            new { Email = email, Password = password });
        loginResp.EnsureSuccessStatusCode();

        var login = await loginResp.Content.ReadFromJsonAsync<LoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty login response.");

        // 3. Resolve workspace (list or create)
        var workspaceRef = await ResolveOrCreateWorkspaceAsync(login.AccessToken);

        return (login.AccessToken, workspaceRef);
    }

    private async Task<Guid> ResolveOrCreateWorkspaceAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{E2EConfig.AuthBaseUrl}/api/workspaces");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var workspaces = await resp.Content.ReadFromJsonAsync<List<WorkspaceDto>>(JsonOptions)
            ?? [];

        if (workspaces.Count > 0)
        {
            return workspaces[0].RefId;
        }

        // No workspace exists yet — create one.
        using var createReq = new HttpRequestMessage(HttpMethod.Post, $"{E2EConfig.AuthBaseUrl}/api/workspaces");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new { Name = "E2E Workspace", Description = (string?)null });
        var createResp = await _http.SendAsync(createReq);
        createResp.EnsureSuccessStatusCode();

        var created = await createResp.Content.ReadFromJsonAsync<WorkspaceDto>(JsonOptions)
            ?? throw new InvalidOperationException("Workspace creation returned empty response.");

        return created.RefId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Policy helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates an AutoApproval=true registration policy and returns (policyRefId, pin).</summary>
    public async Task<(Guid PolicyRef, string Pin)> CreatePolicyAsync(
        string token,
        Guid workspaceRef,
        bool autoApproval = true)
    {
        var policyName = $"e2e-policy-{Guid.NewGuid():N}";
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/registration-policies");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { Name = policyName, MaxClients = (int?)null, AutoApproval = autoApproval });

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var policy = await resp.Content.ReadFromJsonAsync<PolicyDto>(JsonOptions)
            ?? throw new InvalidOperationException("Policy creation returned empty response.");

        return (policy.RefId, policy.Pin);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client registration helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Initiates device registration via PIN and returns (clientRefId, secret).</summary>
    public async Task<(Guid ClientRefId, string Secret)> RegisterClientAsync(string pin, string name)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{E2EConfig.ManagementBaseUrl}/api/client-registrations/initiate",
            new { Pin = pin, Name = name });
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<ClientRegistrationDto>(JsonOptions)
            ?? throw new InvalidOperationException("Client registration returned empty response.");

        return (result.ClientRefId, result.Secret);
    }

    /// <summary>Obtains a client JWT (device-side auth).</summary>
    public async Task<string> LoginClientAsync(Guid clientRefId, string secret)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{E2EConfig.ManagementBaseUrl}/api/client-auth/login",
            new { ClientRefId = clientRefId, Secret = secret });
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<ClientLoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Client login returned empty response.");

        return result.AccessToken;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Workflow helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Publishes a workflow definition JSON and returns the workflow RefId.</summary>
    public async Task<Guid> PublishWorkflowAsync(string token, Guid workspaceRef, Guid workflowRefId, string name, JsonElement definitionElement)
    {
        var request = new WorkflowPublishRequest(workflowRefId, name, null, definitionElement);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/workflows");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(request, options: JsonOptions);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var published = await resp.Content.ReadFromJsonAsync<WorkflowPublishResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Workflow publish returned empty response.");

        return published.RefId;
    }

    /// <summary>Lists runs for a workflow definition.</summary>
    public async Task<IReadOnlyList<RunSummaryDto>> ListRunsAsync(string token, Guid workspaceRef, Guid workflowRefId)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/workflows/{workflowRefId}/runs");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<RunListResponse>(JsonOptions);
        return result?.Runs ?? [];
    }

    /// <summary>Sends a named signal to a parked run via the workspace-scoped management endpoint.</summary>
    public async Task<(bool Matched, string Outcome)> SendSignalAsync(string token, Guid workspaceRef, Guid runRefId, string signalName, object? payload = null)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/runs/{runRefId}/signals/{signalName}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { SignalName = signalName, Payload = payload ?? new { } }, options: JsonOptions);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<SignalDto>(JsonOptions)
            ?? throw new InvalidOperationException("Signal returned empty response.");
        return (result.Matched, result.Outcome);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Polling helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls <paramref name="predicate"/> every <paramref name="interval"/> until it returns true
    /// or <paramref name="timeout"/> elapses, whichever comes first.
    /// Returns true if the predicate became satisfied within the timeout.
    /// </summary>
    public static async Task<bool> PollAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        TimeSpan? interval = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var delay = interval ?? TimeSpan.FromSeconds(2);

        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return true;
            }

            await Task.Delay(delay);
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();

    // ─────────────────────────────────────────────────────────────────────────
    // Local DTOs (mirrors of API response shapes — keeps the test project
    // truly black-box without pulling in host assemblies)
    // ─────────────────────────────────────────────────────────────────────────

    private record LoginDto(string AccessToken, string RefreshToken);
    private record WorkspaceDto(Guid RefId, string Name, string? Description, DateTime CreatedAt);
    private record PolicyDto(Guid RefId, string Pin, string Name, int? MaxClients, bool AutoApproval, bool IsEnabled, DateTime CreatedAt);
    private record ClientRegistrationDto(Guid ClientRefId, string Secret, int Status);
    private record ClientLoginDto(string AccessToken, int ExpiresIn);
    private record SignalDto(bool Matched, string Outcome);
}
