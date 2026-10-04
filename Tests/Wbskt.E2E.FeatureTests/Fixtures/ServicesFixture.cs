using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// xUnit collection fixture shared across all E2E feature tests.
///
/// <para>
/// <b>The register-then-sign-in helpers below depend on a development setting.</b> Sign-in refuses an
/// unconfirmed address, and confirming one means following a link out of an inbox this environment
/// does not have. <c>Auth:Email:RequireVerifiedEmailForSignIn</c> is false in the auth host's
/// <c>appsettings.Development.json</c>, which is what lets these helpers work; against a host running
/// as Production they will get 401 <c>AUTH_EMAIL_UNVERIFIED</c> instead. Standing a mail catcher
/// (MailHog exposes an HTTP API for reading what it received) beside the hosts would let the fixture
/// do the real round trip and remove the dependency — see Scenarios/Auth/AccountRecoveryTests.
/// </para>
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

    /// <summary>
    /// The three public-facing hosts (auth, management, socket) are reachable. This is the gate for
    /// the bulk of the suite; every engine interaction the tests need goes through Management
    /// (manual runs, signals, and the http-wake callback relay), so the engine's own reachability
    /// is not required here.
    /// </summary>
    public bool HostsAvailable { get; }

    /// <summary>
    /// The engine host is *directly* reachable at WorkflowBaseUrl. In a real deployment the engine
    /// is backend-network-only with no public route, so this is false; tests that hit an engine
    /// inbound endpoint directly (currently only the webhook trigger) gate on this and skip when the
    /// engine can't be reached, instead of failing.
    /// </summary>
    public bool EngineAvailable { get; }

    public ServicesFixture()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        HostsAvailable = ProbePublicHostsAsync().GetAwaiter().GetResult();
        EngineAvailable = ProbeHostAsync(E2EConfig.WorkflowBaseUrl).GetAwaiter().GetResult();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Health probe
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> ProbePublicHostsAsync()
    {
        var urls = new[]
        {
            E2EConfig.AuthBaseUrl,
            E2EConfig.ManagementBaseUrl,
            E2EConfig.SocketHttpBaseUrl
        };

        foreach (var url in urls)
        {
            if (!await ProbeHostAsync(url))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ProbeHostAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            // Any HTTP response (even 404 / 401) means the host is up.
            // Only a connection-level failure counts as "unavailable".
            await _http.GetAsync(url, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
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
    // Raw request helpers
    //
    // The typed helpers above all call EnsureSuccessStatusCode, which is right for
    // arranging a scenario but destroys exactly what a negative scenario asserts.
    // These return the response untouched so a test can read the status, the error
    // body, or a header off it.
    // ─────────────────────────────────────────────────────────────────────────

    public static string AuthUrl(string path) => $"{E2EConfig.AuthBaseUrl}{path}";

    public static string ManagementUrl(string path) => $"{E2EConfig.ManagementBaseUrl}{path}";

    /// <summary>Sends a request and returns the raw response without throwing on a failure status.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);

        if (token is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _http.SendAsync(req);
    }

    /// <summary>
    /// Sends a JSON request whose body is empty. This is what "no body" looks like from a JSON client:
    /// the content type is declared, so model binding runs and rejects it with a 400. A request with
    /// no content type at all never reaches model binding; it gets a 415 from the input formatter.
    /// </summary>
    public async Task<HttpResponseMessage> SendEmptyJsonAsync(HttpMethod method, string url, string? token = null)
    {
        using var req = new HttpRequestMessage(method, url);

        if (token is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        req.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        return await _http.SendAsync(req);
    }

    /// <summary>
    /// Sends a request with a verbatim Authorization header, bypassing the well-formedness checks
    /// that <see cref="AuthenticationHeaderValue"/> applies. Needed to present a malformed header.
    /// </summary>
    public async Task<HttpResponseMessage> SendWithRawAuthorizationAsync(HttpMethod method, string url, string headerValue)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Authorization", headerValue);

        return await _http.SendAsync(req);
    }

    /// <summary>
    /// Mints a JWT locally. Only used to produce tokens this host must refuse — one signed with a key
    /// it does not know, and one carrying no signature at all. Pass a null <paramref name="signingKey"/>
    /// for the unsigned <c>alg: none</c> form.
    /// </summary>
    public static string MintJwt(string? signingKey, string subject, TimeSpan lifetime)
    {
        var header = signingKey is null
            ? "{\"alg\":\"none\",\"typ\":\"JWT\"}"
            : "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

        var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var payload = "{\"nameid\":\"" + subject + "\",\"type\":\"user\",\"exp\":" + expires + "}";

        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(payload))}";
        if (signingKey is null)
        {
            return $"{signingInput}.";
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return $"{signingInput}.{Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)))}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Sends a verbatim body under a chosen media type. Needed for the two failures that cannot be
    /// expressed through a serializer: malformed JSON, and a content type the host does not accept.
    /// </summary>
    public async Task<HttpResponseMessage> SendRawBodyAsync(HttpMethod method, string url, string content, string mediaType)
    {
        using var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
        };

        return await _http.SendAsync(req);
    }

    /// <summary>Registers without logging in, so the raw response stays assertable.</summary>
    public Task<HttpResponseMessage> RegisterAsync(string username, string email, string password, string? invitationToken = null) =>
        SendAsync(
            HttpMethod.Post,
            AuthUrl("/api/auth/register"),
            body: new { Username = username, Email = email, Password = password, InvitationToken = invitationToken });

    /// <summary>Attempts a login and returns the raw response, without throwing on refusal.</summary>
    public Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/auth/login"), body: new { Email = email, Password = password });

    /// <summary>A unique credential set that has not been registered.</summary>
    public static (string Username, string Email, string Password) NewCredentials()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return ($"e2e-{suffix}", $"e2e-{suffix}@test.local", $"P@ssw0rd-{suffix}");
    }

    /// <summary>Reads the <c>code</c> off an <c>Error</c> response body, or null when the body is not one.</summary>
    public static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorDto>(JsonOptions);
            return error?.Code;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Model-validation failures return ProblemDetails, which has no code. Not an error here:
            // the caller is asking "what code, if any", and the status assertion carries the weight.
            return null;
        }
    }

    /// <summary>
    /// GETs a paged list endpoint and returns how many items came back alongside the X-Total-Count
    /// header. A null total means the header was absent, which is the pagination bug itself.
    /// </summary>
    public async Task<(int ItemCount, int? TotalCount)> GetPageAsync(string url, string token)
    {
        var resp = await SendAsync(HttpMethod.Get, url, token);
        resp.EnsureSuccessStatusCode();

        var page = await resp.Content.ReadFromJsonAsync<ListDto<JsonElement>>(JsonOptions)
            ?? throw new InvalidOperationException($"Empty list response from {url}.");

        int? total = null;
        if (resp.Headers.TryGetValues("X-Total-Count", out var values)
            && int.TryParse(values.FirstOrDefault(), out var parsed))
        {
            total = parsed;
        }

        return (page.Items.Count, total);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auth-host arrangement helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A registered, logged-in user. Unique per call so scenarios never collide.</summary>
    public sealed record TestUser(string Username, string Email, string Password, string Token);

    /// <summary>
    /// Registers and logs in a brand-new user, returning their credentials and token.
    /// <para>
    /// The new account lands in a tenant of its own, not in the caller's. A scenario that needs an
    /// administrator to act on this user — anything under <c>/api/tenants/{tenantRef}/members</c> —
    /// wants <see cref="CreateUserInTenantAsync"/> instead.
    /// </para>
    /// </summary>
    public async Task<TestUser> CreateUserAsync(string? invitationToken = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"e2e-{suffix}@test.local";

        // At least 12 characters — RegisterRequest enforces it.
        var password = $"P@ssw0rd-{suffix}";
        var username = $"e2e-{suffix}";

        return await RegisterAndLoginAsync(username, email, password, invitationToken);
    }

    /// <summary>
    /// Creates a user who is a member of <paramref name="tenantRef"/>, by inviting an address and
    /// registering against that invitation — the only route into an existing tenant.
    /// </summary>
    public async Task<TestUser> CreateUserInTenantAsync(string adminToken, Guid tenantRef)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"e2e-{suffix}@test.local";
        var password = $"P@ssw0rd-{suffix}";
        var username = $"e2e-{suffix}";

        var token = await InviteAsync(adminToken, tenantRef, email);

        return await RegisterAndLoginAsync(username, email, password, token);
    }

    /// <summary>Issues an invitation and returns its raw token — the only time it is available.</summary>
    public async Task<string> InviteAsync(string adminToken, Guid tenantRef, string email, Guid? roleRef = null)
    {
        var resp = await SendAsync(
            HttpMethod.Post,
            AuthUrl($"/api/tenants/{tenantRef}/invitations"),
            adminToken,
            new { Email = email, RoleRef = roleRef });
        resp.EnsureSuccessStatusCode();

        var invitation = await resp.Content.ReadFromJsonAsync<CreatedInvitationDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty invitation response.");

        return invitation.Token;
    }

    private async Task<TestUser> RegisterAndLoginAsync(string username, string email, string password, string? invitationToken)
    {
        var registerResp = await _http.PostAsJsonAsync(
            AuthUrl("/api/auth/register"),
            new { Username = username, Email = email, Password = password, InvitationToken = invitationToken });
        registerResp.EnsureSuccessStatusCode();

        var loginResp = await _http.PostAsJsonAsync(
            AuthUrl("/api/auth/login"),
            new { Email = email, Password = password });
        loginResp.EnsureSuccessStatusCode();

        var login = await loginResp.Content.ReadFromJsonAsync<LoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty login response.");

        return new TestUser(username, email, password, login.AccessToken);
    }

    /// <summary>
    /// One login's worth of credentials. The refresh token is the interesting half: it is the thing
    /// that rotates, and the access token merely rides along until it expires.
    /// </summary>
    public sealed record Session(string AccessToken, string RefreshToken);

    /// <summary>Logs in and returns both tokens. Throws if the credentials are refused.</summary>
    public async Task<Session> LoginAsync(string email, string password)
    {
        var response = await SendAsync(
            HttpMethod.Post, AuthUrl("/api/auth/login"), body: new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();

        return await ReadSessionAsync(response);
    }

    /// <summary>Reads the token pair out of a successful login or rotation response.</summary>
    public static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        var login = await response.Content.ReadFromJsonAsync<LoginDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty session response.");

        return new Session(login.AccessToken, login.RefreshToken);
    }

    /// <summary>Exchanges a refresh token, returning the raw response so failures stay assertable.</summary>
    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/auth/refresh-token"), body: new { RefreshToken = refreshToken });

    /// <summary>Revokes a single refresh token. Answers 204 whether or not it was live, by design.</summary>
    public Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/auth/logout"), body: new { RefreshToken = refreshToken });

    /// <summary>Revokes every refresh token the bearer holds.</summary>
    public Task<HttpResponseMessage> LogoutAllAsync(string accessToken) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/auth/logout-all"), accessToken);

    /// <summary>
    /// The workspaces the bearer can see. Used to establish *whose* access token was issued without
    /// decoding it — a token that lists user B's workspaces belongs to user B.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetWorkspaceRefsAsync(string accessToken)
    {
        var response = await SendAsync(HttpMethod.Get, AuthUrl("/api/workspaces"), accessToken);
        response.EnsureSuccessStatusCode();

        var workspaces = await response.Content.ReadFromJsonAsync<List<WorkspaceDto>>(JsonOptions) ?? [];
        return workspaces.Select(w => w.RefId).ToList();
    }

    /// <summary>Suspends a member in one tenant, or lifts it. Requires a caller with tenant-wide users.manage.</summary>
    public Task<HttpResponseMessage> SetMemberSuspendedAsync(string adminToken, Guid tenantRef, Guid userRef, bool isSuspended) =>
        SendAsync(
            HttpMethod.Put,
            AuthUrl($"/api/tenants/{tenantRef}/members/{userRef}/suspended"),
            adminToken,
            new { IsSuspended = isSuspended });

    /// <summary>The caller's first tenant reference — every /api/tenants route needs one.</summary>
    public async Task<Guid> GetTenantRefAsync(string token)
    {
        var resp = await SendAsync(HttpMethod.Get, AuthUrl("/api/tenants"), token);
        resp.EnsureSuccessStatusCode();

        var tenants = await resp.Content.ReadFromJsonAsync<List<TenantDto>>(JsonOptions) ?? [];
        if (tenants.Count == 0)
        {
            throw new InvalidOperationException("Caller belongs to no tenant.");
        }

        return tenants[0].RefId;
    }

    /// <summary>A pending invitation as an administrator sees it.</summary>
    public sealed record Invitation(Guid RefId, string Email, Guid? RoleRef, string? RoleName, DateTime ExpiresAt, DateTime CreatedAt);

    /// <summary>Redeems an invitation for the bearer. Returns the raw response so callers can assert on rejections.</summary>
    public Task<HttpResponseMessage> AcceptInvitationAsync(string accessToken, string token) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/invitations/accept"), accessToken, new { Token = token });

    /// <summary>The invitations a tenant has outstanding.</summary>
    public async Task<IReadOnlyList<Invitation>> GetInvitationsAsync(string token, Guid tenantRef)
    {
        var resp = await SendAsync(HttpMethod.Get, AuthUrl($"/api/tenants/{tenantRef}/invitations?take=200"), token);
        resp.EnsureSuccessStatusCode();

        var page = await resp.Content.ReadFromJsonAsync<ListDto<Invitation>>(JsonOptions);
        return page?.Items ?? [];
    }

    /// <summary>Every tenant the caller belongs to, not just the first.</summary>
    public async Task<IReadOnlyList<Tenant>> GetTenantsAsync(string token)
    {
        var resp = await SendAsync(HttpMethod.Get, AuthUrl("/api/tenants"), token);
        resp.EnsureSuccessStatusCode();

        var tenants = await resp.Content.ReadFromJsonAsync<List<Tenant>>(JsonOptions) ?? [];
        return tenants;
    }

    /// <summary>A tenant as the API exposes it.</summary>
    public sealed record Tenant(Guid RefId, string Name);

    /// <summary>Finds a tenant member's public reference by email, or null when absent.</summary>
    public async Task<Guid?> FindTenantMemberRefAsync(string token, Guid tenantRef, string email)
    {
        var resp = await SendAsync(
            HttpMethod.Get,
            AuthUrl($"/api/tenants/{tenantRef}/members?search={Uri.EscapeDataString(email)}&take=200"),
            token);
        resp.EnsureSuccessStatusCode();

        var page = await resp.Content.ReadFromJsonAsync<ListDto<MemberDto>>(JsonOptions);
        return page?.Items.FirstOrDefault(m => string.Equals(m.Email, email, StringComparison.OrdinalIgnoreCase))?.RefId;
    }

    /// <summary>Creates a tenant role and returns its reference.</summary>
    public async Task<Guid> CreateRoleAsync(string token, Guid tenantRef, string? name = null)
    {
        var resp = await SendAsync(
            HttpMethod.Post,
            AuthUrl($"/api/tenants/{tenantRef}/roles"),
            token,
            new { Name = name ?? $"e2e-role-{Guid.NewGuid():N}", Description = "created by an E2E scenario" });
        resp.EnsureSuccessStatusCode();

        var role = await resp.Content.ReadFromJsonAsync<RoleDto>(JsonOptions)
            ?? throw new InvalidOperationException("Role creation returned empty response.");

        return role.RefId;
    }

    /// <summary>Creates a tenant group and returns its reference.</summary>
    public async Task<Guid> CreateGroupAsync(string token, Guid tenantRef, string? name = null)
    {
        var resp = await SendAsync(
            HttpMethod.Post,
            AuthUrl($"/api/tenants/{tenantRef}/groups"),
            token,
            new { Name = name ?? $"e2e-group-{Guid.NewGuid():N}", ParentGroupRef = (Guid?)null });
        resp.EnsureSuccessStatusCode();

        var group = await resp.Content.ReadFromJsonAsync<GroupDto>(JsonOptions)
            ?? throw new InvalidOperationException("Group creation returned empty response.");

        return group.RefId;
    }

    /// <summary>Creates a workspace owned by the caller and returns its reference.</summary>
    public async Task<Guid> CreateWorkspaceAsync(string token, string? name = null)
    {
        var resp = await SendAsync(
            HttpMethod.Post,
            AuthUrl("/api/workspaces"),
            token,
            new { Name = name ?? $"e2e-ws-{Guid.NewGuid():N}", Description = (string?)null });
        resp.EnsureSuccessStatusCode();

        var workspace = await resp.Content.ReadFromJsonAsync<WorkspaceDto>(JsonOptions)
            ?? throw new InvalidOperationException("Workspace creation returned empty response.");

        return workspace.RefId;
    }

    /// <summary>The internal workspace ID and effective permission slugs a resolve call hands back.</summary>
    public sealed record ResolvedWorkspace(int WorkspaceId, IReadOnlyList<string> Permissions);

    /// <summary>
    /// Calls the gate every other host depends on, returning the raw response. Most scenarios here
    /// are about which status comes back, so this deliberately does not throw on failure.
    /// </summary>
    public Task<HttpResponseMessage> ResolveWorkspaceAsync(string? token, Guid workspaceRef) =>
        SendAsync(HttpMethod.Post, AuthUrl("/api/workspaces/resolve"), token, new { WorkspaceRef = workspaceRef });

    /// <summary>Reads a successful resolve response.</summary>
    public static async Task<ResolvedWorkspace> ReadResolvedWorkspaceAsync(HttpResponseMessage response)
    {
        var resolved = await response.Content.ReadFromJsonAsync<ResolvedWorkspaceDto>(JsonOptions)
            ?? throw new InvalidOperationException("Empty resolve response.");

        return new ResolvedWorkspace(resolved.WorkspaceId, resolved.Permissions);
    }

    /// <summary>The caller's effective permission slugs in a workspace. Throws if the resolve fails.</summary>
    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(string token, Guid workspaceRef)
    {
        var response = await ResolveWorkspaceAsync(token, workspaceRef);
        response.EnsureSuccessStatusCode();

        return (await ReadResolvedWorkspaceAsync(response)).Permissions;
    }

    // ── Tenant administration read-back ──────────────────────────────────────

    public sealed record Role(Guid RefId, string Name, string? Description);
    public sealed record Group(Guid RefId, string Name, Guid? ParentGroupRefId);

    /// <summary>A role held by a user or group, with the scope it was granted at.</summary>
    public sealed record RoleAssignment(Guid RoleRef, string RoleName, Guid? WorkspaceRef);

    /// <summary>A permission granted directly to a user, with its scope and whether it is a denial.</summary>
    public sealed record UserPermission(string Slug, bool IsDeny, Guid? WorkspaceRef);

    /// <summary>A group a user belongs to.</summary>
    public sealed record GroupMembership(Guid GroupRef, string GroupName);

    public async Task<IReadOnlyList<Role>> GetRolesAsync(string token, Guid tenantRef)
    {
        var page = await ReadListAsync<RoleDto>(AuthUrl($"/api/tenants/{tenantRef}/roles?take=200"), token);
        return page.Select(r => new Role(r.RefId, r.Name, r.Description)).ToList();
    }

    public async Task<IReadOnlyList<Group>> GetGroupsAsync(string token, Guid tenantRef)
    {
        var page = await ReadListAsync<GroupDto>(AuthUrl($"/api/tenants/{tenantRef}/groups?take=200"), token);
        return page.Select(g => new Group(g.RefId, g.Name, g.ParentGroupRefId)).ToList();
    }

    public Task<IReadOnlyList<RoleAssignment>> GetGroupRoleAssignmentsAsync(string token, Guid tenantRef, Guid groupRef) =>
        ReadArrayAsync<RoleAssignment>(AuthUrl($"/api/tenants/{tenantRef}/groups/{groupRef}/roles"), token);

    public Task<IReadOnlyList<RoleAssignment>> GetUserRoleAssignmentsAsync(string token, Guid tenantRef, Guid userRef) =>
        ReadArrayAsync<RoleAssignment>(AuthUrl($"/api/tenants/{tenantRef}/members/{userRef}/roles"), token);

    public Task<IReadOnlyList<UserPermission>> GetUserPermissionsAsync(string token, Guid tenantRef, Guid userRef) =>
        ReadArrayAsync<UserPermission>(AuthUrl($"/api/tenants/{tenantRef}/members/{userRef}/permissions"), token);

    public Task<IReadOnlyList<GroupMembership>> GetUserGroupsAsync(string token, Guid tenantRef, Guid userRef) =>
        ReadArrayAsync<GroupMembership>(AuthUrl($"/api/tenants/{tenantRef}/members/{userRef}/groups"), token);

    /// <summary>Reads a <c>ListResponse</c>-shaped body (<c>{ "items": [...] }</c>).</summary>
    private async Task<IReadOnlyList<T>> ReadListAsync<T>(string url, string token)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<ListDto<T>>(JsonOptions);
        return page?.Items ?? [];
    }

    /// <summary>Reads a bare JSON array body, which the read-back endpoints return unwrapped.</summary>
    private async Task<IReadOnlyList<T>> ReadArrayAsync<T>(string url, string token)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions) ?? [];
    }

    /// <summary>The slugs currently attached to a role definition.</summary>
    public async Task<IReadOnlyList<string>> GetRolePermissionSlugsAsync(string token, Guid tenantRef, Guid roleRef)
    {
        var resp = await SendAsync(
            HttpMethod.Get,
            AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"),
            token);
        resp.EnsureSuccessStatusCode();

        var permissions = await resp.Content.ReadFromJsonAsync<List<RolePermissionDto>>(JsonOptions) ?? [];
        return permissions.Select(p => p.Slug).ToList();
    }

    /// <summary>A permission attached to a role definition. Role permissions carry no scope.</summary>
    public sealed record RolePermission(string Slug, bool IsDeny);

    /// <summary>
    /// The permissions on a role with their deny flags. The slug-only read above answers "is it
    /// attached"; a grant and a denial are both attached, so telling them apart needs this.
    /// </summary>
    public Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(string token, Guid tenantRef, Guid roleRef) =>
        ReadArrayAsync<RolePermission>(AuthUrl($"/api/tenants/{tenantRef}/roles/{roleRef}/permissions"), token);

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

    /// <summary>Initiates client registration via PIN and returns (clientRefId, secret).</summary>
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

    /// <summary>Obtains a client JWT (client-side auth).</summary>
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
        var definition = definitionElement.Deserialize<Wbskt.Workflow.Abstraction.Models.WorkflowDefinition>(JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize workflow definition in test.");
        var request = new WorkflowPublishRequest(workflowRefId, name, null, definition);

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
    public async Task<IReadOnlyList<RunSummaryDto>> ListRunsAsync(string token, Guid workspaceRef, Guid workflowRefId, int top = 50)
    {
        // The endpoint serves at most 200 runs a page, so follow nextCursor until `top` are read.
        var runs = new List<RunSummaryDto>();
        long? cursor = null;
        while (runs.Count < top)
        {
            var url = $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/workflows/{workflowRefId}/runs?top={top - runs.Count}"
                + (cursor is { } c ? $"&cursor={c}" : "");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return runs;
            }

            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<RunListResponse>(JsonOptions);
            runs.AddRange(result?.Runs ?? []);
            if (result?.NextCursor is not { } next)
            {
                break;
            }

            cursor = next;
        }

        return runs;
    }

    /// <summary>Starts a manual run, optionally with an idempotency key (re-posting the same key dedupes).</summary>
    public async Task StartManualRunAsync(string token, Guid workspaceRef, Guid workflowRefId, string triggerNodeId, string? idempotencyKey)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/workflows/{workflowRefId}/runs");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { TriggerNodeId = triggerNodeId, Payload = (object?)null, IdempotencyKey = idempotencyKey }, options: JsonOptions);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// POSTs to Management's public (anonymous) http-wake callback to resume a parked WaitForHttp
    /// branch. Management relays to the backend-only engine with the shared api-key; the token is
    /// the one the workflow author pinned at design time (WaitForHttpConfig.Token). The callback is
    /// deliberately opaque (uniform 202, no match/run details), so callers observe the resume via
    /// its side effects rather than the response body.
    /// </summary>
    public async Task SendHttpWakeAsync(string token, object? payload = null)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{E2EConfig.ManagementBaseUrl}/api/callbacks/wake/{Uri.EscapeDataString(token)}",
            payload ?? new { });
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// POSTs to Management's public (anonymous) workspace-scoped webhook callback, which relays to
    /// the backend-only engine with the shared api-key. Mirrors an external system firing a webhook
    /// trigger. The callback is opaque (uniform 202), so callers observe the started run via the
    /// runs API rather than the response body.
    /// </summary>
    public async Task SendWebhookAsync(Guid workspaceRef, string path, object? payload = null)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{E2EConfig.ManagementBaseUrl}/api/callbacks/webhook/{workspaceRef}/{Uri.EscapeDataString(path)}",
            payload ?? new { });
        resp.EnsureSuccessStatusCode();
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

    /// <summary>Fetches the full run detail (summary + all branches, each carrying its LocalJson state).</summary>
    public async Task<RunDetailDto> GetRunDetailAsync(string token, Guid workspaceRef, Guid runRefId)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/runs/{runRefId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<RunDetailDto>(JsonOptions)
            ?? throw new InvalidOperationException("Run detail returned empty response.");
    }

    /// <summary>Cancels a specific run by RefId.</summary>
    public async Task CancelRunAsync(string token, Guid workspaceRef, Guid runRefId)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/runs/{runRefId}/cancel");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new CancelRunRequest("test cancellation"), options: JsonOptions);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Fetches the ordered history events for a run (each carrying NodeCompleted/NodeFailed PayloadJson).</summary>
    public async Task<IReadOnlyList<HistoryEventDto>> GetHistoryAsync(string token, Guid workspaceRef, Guid runRefId, int top = 200)
    {
        // The endpoint serves at most 200 events a page, so follow nextCursor until `top` are read.
        var events = new List<HistoryEventDto>();
        long fromEventId = 0;
        while (events.Count < top)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/runs/{runRefId}/history?top={top - events.Count}&fromEventId={fromEventId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<HistoryListResponse>(JsonOptions);
            events.AddRange(result?.Events ?? []);
            if (result?.NextCursor is not { } next)
            {
                break;
            }

            fromEventId = next;
        }

        return events;
    }

    /// <summary>Polls until the first run for a workflow appears, returning its RefId (or Guid.Empty on timeout).</summary>
    public async Task<Guid> WaitForFirstRunAsync(string token, Guid workspaceRef, Guid workflowRefId, TimeSpan timeout)
    {
        Guid runRefId = Guid.Empty;
        await PollAsync(
            async () =>
            {
                var runs = await ListRunsAsync(token, workspaceRef, workflowRefId);
                if (runs.Count > 0)
                {
                    runRefId = runs[0].RefId;
                    return true;
                }

                return false;
            },
            timeout,
            TimeSpan.FromSeconds(1));

        return runRefId;
    }

    /// <summary>Polls a workflow's runs until one reaches the requested terminal status, returning it (or null on timeout).</summary>
    public async Task<RunSummaryDto?> WaitForRunStatusAsync(string token, Guid workspaceRef, Guid workflowRefId, string status, TimeSpan timeout)
    {
        RunSummaryDto? match = null;
        await PollAsync(
            async () =>
            {
                var runs = await ListRunsAsync(token, workspaceRef, workflowRefId);
                match = runs.FirstOrDefault(r => string.Equals(r.Status, status, StringComparison.Ordinal));
                return match is not null;
            },
            timeout,
            TimeSpan.FromSeconds(2));

        return match;
    }

    /// <summary>Polls a specific run by RefId until it reaches any terminal status, returning its final summary (or null on timeout).</summary>
    public async Task<RunSummaryDto?> WaitForRunTerminalAsync(string token, Guid workspaceRef, Guid runRefId, TimeSpan timeout)
    {
        RunSummaryDto? summary = null;
        await PollAsync(
            async () =>
            {
                var detail = await GetRunDetailAsync(token, workspaceRef, runRefId);
                summary = detail.Summary;
                return IsTerminalStatus(detail.Summary.Status);
            },
            timeout,
            TimeSpan.FromSeconds(2));

        return summary;
    }

    /// <summary>Reads the central event-log feed for a workspace, optionally filtered by event name.</summary>
    public async Task<IReadOnlyList<EventLogItemDto>> GetEventLogsAsync(string token, Guid workspaceRef, string? eventName = null, int take = 200)
    {
        var url = $"{E2EConfig.ManagementBaseUrl}/api/workspaces/{workspaceRef}/event-logs?take={take}";
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            url += $"&eventName={Uri.EscapeDataString(eventName)}";
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<EventLogListDto>(JsonOptions);
        return result?.Items ?? [];
    }

    /// <summary>The set of run statuses that represent a finalized run.</summary>
    public static bool IsTerminalStatus(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";

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
    private record ErrorDto(string Code, string Message, int Type);
    private record ListDto<T>(List<T> Items);
    private record TenantDto(Guid RefId, string Name);
    private record RoleDto(Guid RefId, string Name, string? Description);
    private record GroupDto(Guid RefId, string Name, Guid? ParentGroupRefId);
    private record MemberDto(Guid RefId, string Username, string Email, bool IsActive);
    private record CreatedInvitationDto(Guid RefId, string Email, DateTime ExpiresAt, string Token);
    private record RolePermissionDto(string Slug, bool IsDeny);
    private record ResolvedWorkspaceDto(int WorkspaceId, List<string> Permissions);
    private record PolicyDto(Guid RefId, string Pin, string Name, int? MaxClients, bool AutoApproval, bool IsEnabled, DateTime CreatedAt);
    private record ClientRegistrationDto(Guid ClientRefId, string Secret, int Status);
    private record ClientLoginDto(string AccessToken, int ExpiresIn);
    private record SignalDto(bool Matched, string Outcome);
    private record EventLogListDto(IReadOnlyList<EventLogItemDto> Items);
}

/// <summary>Mirror of the Management Host's EventLogResponse (criticality serializes as a number: Info=0, Warning=1, Error=2).</summary>
public sealed record EventLogItemDto(string EventName, string EventData, int Criticality, Guid? PolicyRefId, Guid? ClientRefId, Guid? WorkflowRefId, DateTime CreatedAtUtc);
