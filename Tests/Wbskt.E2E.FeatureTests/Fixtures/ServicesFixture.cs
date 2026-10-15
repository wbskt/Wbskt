using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wbskt.Api.Client;
using Wbskt.Auth.Models;
using Wbskt.Management.Models;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// xUnit collection fixture for the E2E feature tests: one instance per collection (see
/// <c>E2ECollections.cs</c>), which is safe because it holds nothing but an HttpClient and the
/// result of its host probes.
///
/// <para>
/// The hosts are spoken to through the typed clients in <c>Clients/Wbskt.Api.Client</c>:
/// <see cref="Auth"/> and <see cref="Management"/>, over one cert-ignoring <see cref="HttpClient"/>.
/// <c>client.As(token)</c> speaks for one user; every endpoint has an <c>XAsync</c> method that returns
/// the contract type and throws <see cref="ApiException"/> on a failure status, and an <c>XRawAsync</c>
/// twin for a scenario that asserts on the status. What is left here is arrangement: creating users,
/// workspaces and devices, and waiting for what a scenario needs to observe.
/// </para>
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

    // Shared cert-ignoring HttpClient under both typed clients and the bespoke raw helpers.
    private readonly HttpClient _http;

    /// <summary>The auth host, anonymous. <c>Auth.As(token)</c> speaks for a user.</summary>
    public WbsktAuthClient Auth { get; }

    /// <summary>The management host, anonymous. <c>Management.As(token)</c> speaks for a user.</summary>
    public WbsktManagementClient Management { get; }

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

    /// <summary>
    /// A devices-only management host is reachable at DevicesBaseUrl (start-hosts.sh starts one).
    /// The scenarios that exercise it skip without it.
    /// </summary>
    public bool DevicesHostAvailable { get; }

    public ServicesFixture()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        // Generous on purpose: list and history reads grow with what a scenario created, and a slow
        // one should fail as a slow assertion, not as a TaskCanceledException from inside a helper.
        // How long a scenario waits for something is bounded by the PollUntilAsync timeouts, and the
        // host probes below carry their own short timeout.
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        Auth = new WbsktAuthClient(_http, new Uri(E2EConfig.AuthBaseUrl));
        Management = new WbsktManagementClient(_http, new Uri(E2EConfig.ManagementBaseUrl));

        // On the thread pool, so no awaited continuation needs the thread this blocks. The three
        // collections build their fixtures at the same time, and a probe that resumed on a context
        // whose threads were all blocked here would never finish.
        HostsAvailable = Task.Run(() => ProbePublicHostsAsync()).GetAwaiter().GetResult();
        EngineAvailable = Task.Run(() => ProbeHostAsync(E2EConfig.WorkflowBaseUrl)).GetAwaiter().GetResult();
        DevicesHostAvailable = Task.Run(() => ProbeHostAsync(E2EConfig.DevicesBaseUrl)).GetAwaiter().GetResult();
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
    /// Returns the bearer token and the admin's workspace RefId.
    /// <para>
    /// Only for scenarios about the seeded administrator or its tenant and Default Workspace
    /// themselves (the Auth folder). A scenario that just needs somewhere to publish workflows,
    /// register devices or start runs wants <see cref="CreateIsolatedWorkspaceAsync"/>: the
    /// Default Workspace is shared by every run of the suite and nothing in it is ever cleaned up.
    /// </para>
    /// </summary>
    public async Task<(string Token, Guid WorkspaceRef)> LoginAsAdminAsync()
    {
        var login = await Auth.LoginAsync(new LoginRequest(E2EConfig.AdminEmail, E2EConfig.AdminPassword));
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
        var user = await RegisterAndLoginAsync($"e2e-{suffix}", $"e2e-{suffix}@test.local", $"P@ss{suffix}!", invitationToken: null);

        return user.Token;
    }

    /// <summary>
    /// Registers a brand-new user (unique per run) and logs in.
    /// Returns the bearer token and the first workspace RefId.
    /// If the user has no workspaces yet a new one is created automatically.
    /// </summary>
    public async Task<(string Token, Guid WorkspaceRef)> RegisterAndLoginUserAsync()
    {
        var token = await RegisterAndLoginNewUserAsync();
        var workspaceRef = await ResolveOrCreateWorkspaceAsync(token);

        return (token, workspaceRef);
    }

    private async Task<Guid> ResolveOrCreateWorkspaceAsync(string token)
    {
        var auth = Auth.As(token);
        var workspaces = await auth.GetWorkspacesAsync();
        if (workspaces.Count > 0)
        {
            return workspaces[0].RefId;
        }

        // No workspace exists yet — create one.
        var created = await auth.CreateWorkspaceAsync(new CreateWorkspaceRequest("E2E Workspace", null));
        return created.RefId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Raw request helpers
    //
    // For a request the typed clients cannot express: a URL a scenario builds itself (query strings
    // under test, deprecated routes), a body the contract does not allow, a malformed header. These
    // return the response untouched so a test can read the status, the error body, or a header off it.
    // A request the contract can express goes through Auth/Management and its XRawAsync method.
    // ─────────────────────────────────────────────────────────────────────────

    public static string AuthUrl(string path) => $"{E2EConfig.AuthBaseUrl}{path}";

    public static string ManagementUrl(string path) => $"{E2EConfig.ManagementBaseUrl}{path}";

    /// <summary>Sends a request to an absolute URL and returns the raw response without throwing on a failure status.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token = null, object? body = null) =>
        Auth.As(token).SendRawAsync(method, url, body);

    /// <summary>
    /// Sends a JSON request whose body is empty. This is what "no body" looks like from a JSON client:
    /// the content type is declared, so model binding runs and rejects it with a 400. A request with
    /// no content type at all never reaches model binding; it gets a 415 from the input formatter.
    /// </summary>
    public async Task<HttpResponseMessage> SendEmptyJsonAsync(HttpMethod method, string url, string? token = null)
    {
        // Bespoke: an empty body under a JSON content type, which no serializer produces.
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
        var payload = "{\"sub\":\"" + subject + "\",\"typ\":\"user\",\"exp\":" + expires + "}";

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
        Auth.RegisterRawAsync(new RegisterRequest(username, email, password, invitationToken));

    /// <summary>Attempts a login and returns the raw response, without throwing on refusal.</summary>
    public Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        Auth.LoginRawAsync(new LoginRequest(email, password));

    /// <summary>A unique credential set that has not been registered.</summary>
    public static (string Username, string Email, string Password) NewCredentials()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return ($"e2e-{suffix}", $"e2e-{suffix}@test.local", $"P@ssw0rd-{suffix}");
    }

    /// <summary>Reads the <c>code</c> off a Problem Details error body, or null when the body is not one.</summary>
    public static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response) =>
        (await ApiProblem.ReadAsync(response))?.Code;

    /// <summary>
    /// GETs a paged list endpoint and returns how many items came back alongside the X-Total-Count
    /// header. A null total means the header was absent, which is the pagination bug itself.
    /// </summary>
    public async Task<(int ItemCount, int? TotalCount)> GetPageAsync(string url, string token)
    {
        using var resp = await SendAsync(HttpMethod.Get, url, token);
        var page = await WbsktApiClient.ReadAsync<Page<JsonElement>>(resp);

        int? total = null;
        if (resp.Headers.TryGetValues("X-Total-Count", out var values)
            && int.TryParse(values.FirstOrDefault(), out var parsed))
        {
            total = parsed;
        }

        return (page.Items.Count(), total);
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
        var invitation = await Auth.As(adminToken).CreateInvitationAsync(tenantRef, new CreateInvitationRequest(email, roleRef));
        return invitation.Token;
    }

    private async Task<TestUser> RegisterAndLoginAsync(string username, string email, string password, string? invitationToken)
    {
        await Auth.RegisterAsync(new RegisterRequest(username, email, password, invitationToken));
        var login = await Auth.LoginAsync(new LoginRequest(email, password));

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
        var login = await Auth.LoginAsync(new LoginRequest(email, password));
        return new Session(login.AccessToken, login.RefreshToken!);
    }

    /// <summary>Reads the token pair out of a successful login or rotation response.</summary>
    public static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        var login = await WbsktApiClient.ReadAsync<LoginResponse>(response);
        return new Session(login.AccessToken, login.RefreshToken!);
    }

    /// <summary>Exchanges a refresh token, returning the raw response so failures stay assertable.</summary>
    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Auth.RefreshTokenRawAsync(new RefreshTokenRequest(refreshToken));

    /// <summary>Revokes a single refresh token. Answers 204 whether or not it was live, by design.</summary>
    public Task<HttpResponseMessage> LogoutAsync(string refreshToken) =>
        Auth.LogoutRawAsync(new RefreshTokenRequest(refreshToken));

    /// <summary>Revokes every refresh token the bearer holds.</summary>
    public Task<HttpResponseMessage> LogoutAllAsync(string accessToken) =>
        Auth.As(accessToken).LogoutAllRawAsync();

    /// <summary>
    /// The workspaces the bearer can see. Used to establish *whose* access token was issued without
    /// decoding it — a token that lists user B's workspaces belongs to user B.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetWorkspaceRefsAsync(string accessToken) =>
        (await Auth.As(accessToken).GetWorkspacesAsync()).Select(w => w.RefId).ToList();

    /// <summary>Suspends a member in one tenant, or lifts it. Requires a caller with tenant-wide users.manage.</summary>
    public Task<HttpResponseMessage> SetMemberSuspendedAsync(string adminToken, Guid tenantRef, Guid userRef, bool isSuspended) =>
        Auth.As(adminToken).SetMemberSuspendedRawAsync(tenantRef, userRef, new SetMemberSuspendedRequest(isSuspended));

    /// <summary>The caller's first tenant reference — every /api/tenants route needs one.</summary>
    public async Task<Guid> GetTenantRefAsync(string token)
    {
        var tenants = await GetTenantsAsync(token);
        if (tenants.Count == 0)
        {
            throw new InvalidOperationException("Caller belongs to no tenant.");
        }

        return tenants[0].RefId;
    }

    /// <summary>Redeems an invitation for the bearer. Returns the raw response so callers can assert on rejections.</summary>
    public Task<HttpResponseMessage> AcceptInvitationAsync(string accessToken, string token) =>
        Auth.As(accessToken).AcceptInvitationRawAsync(new AcceptInvitationRequest(token));

    /// <summary>The invitations a tenant has outstanding.</summary>
    public async Task<IReadOnlyList<InvitationResponse>> GetInvitationsAsync(string token, Guid tenantRef) =>
        (await Auth.As(token).GetInvitationsAsync(tenantRef, limit: 200)).Items.ToList();

    /// <summary>Every tenant the caller belongs to, not just the first.</summary>
    public Task<IReadOnlyList<TenantResponse>> GetTenantsAsync(string token) =>
        Auth.As(token).GetTenantsAsync();

    /// <summary>Finds a tenant member's public reference by email, or null when absent.</summary>
    public async Task<Guid?> FindTenantMemberRefAsync(string token, Guid tenantRef, string email)
    {
        var page = await Auth.As(token).GetTenantMembersAsync(tenantRef, search: email, limit: 200);
        return page.Items.FirstOrDefault(m => string.Equals(m.Email, email, StringComparison.OrdinalIgnoreCase))?.RefId;
    }

    /// <summary>Creates a tenant role and returns its reference.</summary>
    public async Task<Guid> CreateRoleAsync(string token, Guid tenantRef, string? name = null)
    {
        var role = await Auth.As(token).CreateRoleAsync(
            tenantRef,
            new CreateRoleRequest(name ?? $"e2e-role-{Guid.NewGuid():N}", "created by an E2E scenario"));

        return role.RefId;
    }

    /// <summary>Creates a tenant group and returns its reference.</summary>
    public async Task<Guid> CreateGroupAsync(string token, Guid tenantRef, string? name = null)
    {
        var group = await Auth.As(token).CreateGroupAsync(tenantRef, new CreateGroupRequest(name ?? $"e2e-group-{Guid.NewGuid():N}", null));
        return group.RefId;
    }

    /// <summary>Creates a workspace owned by the caller and returns its reference.</summary>
    public async Task<Guid> CreateWorkspaceAsync(string token, string? name = null)
    {
        var workspace = await Auth.As(token).CreateWorkspaceAsync(new CreateWorkspaceRequest(name ?? $"e2e-ws-{Guid.NewGuid():N}", null));
        return workspace.RefId;
    }

    /// <summary>
    /// A workspace created for one scenario, with the brand-new user who owns it. Deconstructs to
    /// <c>(token, workspaceRef)</c> for the common case of a scenario that only needs those two.
    /// </summary>
    public sealed record WorkspaceHandle(string Token, Guid WorkspaceRef, TestUser User)
    {
        public void Deconstruct(out string token, out Guid workspaceRef)
        {
            token = Token;
            workspaceRef = WorkspaceRef;
        }
    }

    /// <summary>
    /// Registers a brand-new user and creates a workspace they own, for one scenario to arrange
    /// everything in. The user lands in a tenant of their own with the tenant's Admin role, so every
    /// workspace-scoped operation a scenario arranges (policies, devices, workflow publish, runs,
    /// integrations, event logs) is allowed.
    /// <para>
    /// This exists so scenarios never share the seeded Default Workspace (see
    /// <see cref="LoginAsAdminAsync"/>): what a scenario publishes, the runs it starts and the
    /// events it logs stay in a workspace no other scenario writes to, so assertions on lists and
    /// feeds see only that scenario's rows and the per-workspace ceilings stay at their defaults.
    /// </para>
    /// </summary>
    public async Task<WorkspaceHandle> CreateIsolatedWorkspaceAsync()
    {
        var user = await CreateUserAsync();
        var workspaceRef = await CreateWorkspaceAsync(user.Token);

        return new WorkspaceHandle(user.Token, workspaceRef, user);
    }

    /// <summary>
    /// Calls the gate every other host depends on, returning the raw response. Most scenarios here
    /// are about which status comes back, so this deliberately does not throw on failure.
    /// </summary>
    public Task<HttpResponseMessage> ResolveWorkspaceAsync(string? token, Guid workspaceRef) =>
        Auth.As(token).ResolveWorkspaceRawAsync(new ResolveWorkspaceRequest(workspaceRef));

    /// <summary>Reads a successful resolve response: the internal workspace ID and the effective permission slugs.</summary>
    public static Task<ResolvedWorkspaceResponse> ReadResolvedWorkspaceAsync(HttpResponseMessage response) =>
        WbsktApiClient.ReadAsync<ResolvedWorkspaceResponse>(response);

    /// <summary>The caller's effective permission slugs in a workspace. Throws if the resolve fails.</summary>
    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(string token, Guid workspaceRef) =>
        (await Auth.As(token).ResolveWorkspaceAsync(new ResolveWorkspaceRequest(workspaceRef))).Permissions;

    // ── Tenant administration read-back ──────────────────────────────────────

    public async Task<IReadOnlyList<RoleResponse>> GetRolesAsync(string token, Guid tenantRef) =>
        (await Auth.As(token).GetRolesAsync(tenantRef, limit: 200)).Items.ToList();

    public async Task<IReadOnlyList<GroupResponse>> GetGroupsAsync(string token, Guid tenantRef) =>
        (await Auth.As(token).GetGroupsAsync(tenantRef, limit: 200)).Items.ToList();

    public Task<IReadOnlyList<RoleAssignmentResponse>> GetGroupRoleAssignmentsAsync(string token, Guid tenantRef, Guid groupRef) =>
        Auth.As(token).GetGroupRolesAsync(tenantRef, groupRef);

    public Task<IReadOnlyList<RoleAssignmentResponse>> GetUserRoleAssignmentsAsync(string token, Guid tenantRef, Guid userRef) =>
        Auth.As(token).GetMemberRolesAsync(tenantRef, userRef);

    public Task<IReadOnlyList<UserPermissionAssignmentResponse>> GetUserPermissionsAsync(string token, Guid tenantRef, Guid userRef) =>
        Auth.As(token).GetMemberPermissionsAsync(tenantRef, userRef);

    public Task<IReadOnlyList<GroupMembershipResponse>> GetUserGroupsAsync(string token, Guid tenantRef, Guid userRef) =>
        Auth.As(token).GetMemberGroupsAsync(tenantRef, userRef);

    /// <summary>The slugs currently attached to a role definition.</summary>
    public async Task<IReadOnlyList<string>> GetRolePermissionSlugsAsync(string token, Guid tenantRef, Guid roleRef) =>
        (await GetRolePermissionsAsync(token, tenantRef, roleRef)).Select(p => p.Slug).ToList();

    /// <summary>
    /// The permissions on a role with their deny flags. The slug-only read above answers "is it
    /// attached"; a grant and a denial are both attached, so telling them apart needs this.
    /// </summary>
    public Task<IReadOnlyList<RolePermissionAssignmentResponse>> GetRolePermissionsAsync(string token, Guid tenantRef, Guid roleRef) =>
        Auth.As(token).GetRolePermissionsAsync(tenantRef, roleRef);

    // ─────────────────────────────────────────────────────────────────────────
    // Policy helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates an AutoApproval=true registration policy and returns (policyRefId, pin).</summary>
    public async Task<(Guid PolicyRef, string Pin)> CreatePolicyAsync(
        string token,
        Guid workspaceRef,
        bool autoApproval = true)
    {
        var policy = await Management.As(token).CreatePolicyAsync(
            workspaceRef,
            new RegistrationPolicyRequest($"e2e-policy-{Guid.NewGuid():N}", null, autoApproval));

        return (policy.RefId, policy.Pin);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client registration helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Initiates client registration via PIN and returns (clientRefId, secret).</summary>
    public async Task<(Guid ClientRefId, string Secret)> RegisterClientAsync(string pin, string name)
    {
        var result = await Management.RegisterClientAsync(new ClientRegistrationRequest(pin, name));
        return (result.ClientRefId, result.Secret);
    }

    /// <summary>Obtains a client JWT (client-side auth).</summary>
    public async Task<string> LoginClientAsync(Guid clientRefId, string secret) =>
        (await Management.LoginClientAsync(new ClientLoginRequest(clientRefId, secret))).AccessToken;

    // ─────────────────────────────────────────────────────────────────────────
    // Workflow helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Publishes a workflow definition JSON and returns the workflow RefId.</summary>
    public async Task<Guid> PublishWorkflowAsync(string token, Guid workspaceRef, Guid workflowRefId, string name, JsonElement definitionElement)
    {
        var definition = definitionElement.Deserialize<Wbskt.Workflow.Abstraction.Models.WorkflowDefinition>(JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize workflow definition in test.");

        var published = await Management.As(token).PublishWorkflowAsync(
            workspaceRef,
            new WorkflowPublishRequest(workflowRefId, name, null, definition));

        return published.RefId;
    }

    /// <summary>Lists runs for a workflow definition.</summary>
    public async Task<IReadOnlyList<RunSummaryDto>> ListRunsAsync(string token, Guid workspaceRef, Guid workflowRefId, int top = 50)
    {
        // The endpoint serves at most 200 runs a page, so follow nextCursor until `top` are read.
        var management = Management.As(token);
        var runs = new List<RunSummaryDto>();
        string? cursor = null;
        while (runs.Count < top)
        {
            using var resp = await management.GetRunsRawAsync(workspaceRef, workflowRefId, cursor: cursor, limit: top - runs.Count);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return runs;
            }

            var result = await WbsktApiClient.ReadAsync<RunListResponse>(resp);
            runs.AddRange(result.Items);
            if (result.NextCursor is not { } next)
            {
                break;
            }

            cursor = next;
        }

        return runs;
    }

    /// <summary>
    /// Starts a manual run, optionally with an idempotency key (re-posting the same key dedupes), and
    /// returns the engine's answer: the run's RefId and whether it was started, deduplicated or queued.
    /// </summary>
    public Task<StartRunResponse> StartManualRunAsync(string token, Guid workspaceRef, Guid workflowRefId, string triggerNodeId, string? idempotencyKey) =>
        Management.As(token).StartRunAsync(workspaceRef, workflowRefId, new StartRunRequest(triggerNodeId, null, idempotencyKey));

    /// <summary>Asks for a manual run and returns the raw response, for callers that expect it may be refused.</summary>
    public Task<HttpResponseMessage> StartManualRunRawAsync(string token, Guid workspaceRef, Guid workflowRefId, string triggerNodeId, string? idempotencyKey) =>
        Management.As(token).StartRunRawAsync(workspaceRef, workflowRefId, new StartRunRequest(triggerNodeId, null, idempotencyKey));

    /// <summary>Reads a manual start's answer. The outcome is read whether the host writes it as a name or a number.</summary>
    public static Task<StartRunResponse> ReadStartRunResponseAsync(HttpResponseMessage response) =>
        WbsktApiClient.ReadAsync<StartRunResponse>(response);

    /// <summary>
    /// POSTs to Management's public (anonymous) http-wake callback to resume a parked WaitForHttp
    /// branch. Management relays to the backend-only engine with the shared api-key; the token is
    /// the one the workflow author pinned at design time (WaitForHttpConfig.Token). The callback is
    /// deliberately opaque (uniform 202, no match/run details), so callers observe the resume via
    /// its side effects rather than the response body.
    /// </summary>
    public Task SendHttpWakeAsync(string token, object? payload = null) =>
        Management.SendHttpWakeAsync(token, payload);

    /// <summary>
    /// POSTs to Management's public (anonymous) workspace-scoped webhook callback, which relays to
    /// the backend-only engine with the shared api-key. Mirrors an external system firing a webhook
    /// trigger. The callback is opaque (uniform 202), so callers observe the started run via the
    /// runs API rather than the response body.
    /// </summary>
    public Task SendWebhookAsync(Guid workspaceRef, string path, object? payload = null) =>
        Management.SendWebhookAsync(workspaceRef, path, payload);

    /// <summary>Sends a named signal to a parked run via the workspace-scoped management endpoint.</summary>
    public async Task<(bool Matched, string Outcome)> SendSignalAsync(string token, Guid workspaceRef, Guid runRefId, string signalName, object? payload = null)
    {
        var request = new SignalRequest(signalName, JsonSerializer.SerializeToElement(payload ?? new { }, JsonOptions));
        var result = await Management.As(token).SignalRunAsync(workspaceRef, runRefId, signalName, request);

        return (result.Matched, result.Outcome);
    }

    /// <summary>Fetches the full run detail (summary + all branches, each carrying its LocalJson state).</summary>
    public Task<RunDetailDto> GetRunDetailAsync(string token, Guid workspaceRef, Guid runRefId) =>
        Management.As(token).GetRunAsync(workspaceRef, runRefId);

    /// <summary>
    /// Asks for a run to be cancelled by RefId. The answer is 202: the engine carries the cancel out
    /// afterwards, so a caller that needs the run cancelled polls for it (<see cref="WaitForRunTerminalAsync"/>).
    /// </summary>
    public async Task CancelRunAsync(string token, Guid workspaceRef, Guid runRefId)
    {
        using var resp = await CancelRunRawAsync(token, workspaceRef, runRefId);
        await WbsktApiClient.EnsureSuccessAsync(resp);
    }

    /// <summary>Asks for a run to be cancelled and returns the raw response, for the scenarios about which status comes back.</summary>
    public Task<HttpResponseMessage> CancelRunRawAsync(string token, Guid workspaceRef, Guid runRefId) =>
        Management.As(token).CancelRunRawAsync(workspaceRef, runRefId, new CancelRunRequest("test cancellation"));

    /// <summary>Fetches the ordered history events for a run (each carrying NodeCompleted/NodeFailed PayloadJson).</summary>
    public async Task<IReadOnlyList<HistoryEventDto>> GetHistoryAsync(string token, Guid workspaceRef, Guid runRefId, int top = 200)
    {
        // The endpoint serves at most 200 events a page, so follow nextCursor until `top` are read.
        var management = Management.As(token);
        var events = new List<HistoryEventDto>();
        string? cursor = null;
        while (events.Count < top)
        {
            var result = await management.GetRunHistoryAsync(workspaceRef, runRefId, cursor, top - events.Count);
            events.AddRange(result.Items);
            if (result.NextCursor is not { } next)
            {
                break;
            }

            cursor = next;
        }

        return events;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Observable waits
    //
    // Everything a scenario waits for is something it can observe: a run appearing, a status, a
    // history event, a command arriving. Each wait polls for that signal with a generous timeout, so
    // a fast runner moves on as soon as it is there and a slow one still gets it, and a timeout says
    // what was last seen instead of failing a later assertion with nothing to go on.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls until a workflow has at least one run and returns the newest one's RefId. Throws a
    /// <see cref="TimeoutException"/> naming what the run list last held when none appears in time.
    /// </summary>
    public async Task<Guid> WaitForFirstRunAsync(string token, Guid workspaceRef, Guid workflowRefId, TimeSpan timeout)
    {
        var runs = await WaitForRunCountAsync(token, workspaceRef, workflowRefId, 1, timeout);
        return runs[0].RefId;
    }

    /// <summary>
    /// Polls a workflow's runs until there are at least <paramref name="count"/>, and returns them
    /// (newest first). Throws a <see cref="TimeoutException"/> with the last count and statuses.
    /// </summary>
    public Task<IReadOnlyList<RunSummaryDto>> WaitForRunCountAsync(string token, Guid workspaceRef, Guid workflowRefId, int count, TimeSpan timeout) =>
        PollUntilAsync<IReadOnlyList<RunSummaryDto>>(
            $"at least {count} run(s) of workflow {workflowRefId}",
            async () =>
            {
                var runs = await ListRunsAsync(token, workspaceRef, workflowRefId, top: Math.Max(50, count));
                return (runs.Count >= count, runs, DescribeRuns(runs));
            },
            timeout,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// Polls a workflow's runs until one has the requested status, returning it. Throws a
    /// <see cref="TimeoutException"/> with the statuses last seen and the newest run's latest history
    /// events. The return type stays nullable for the callers written when it returned null on timeout.
    /// </summary>
    public async Task<RunSummaryDto?> WaitForRunStatusAsync(string token, Guid workspaceRef, Guid workflowRefId, string status, TimeSpan timeout)
    {
        IReadOnlyList<RunSummaryDto> lastRuns = [];
        try
        {
            return await PollUntilAsync<RunSummaryDto?>(
                $"a run of workflow {workflowRefId} with status {status}",
                async () =>
                {
                    lastRuns = await ListRunsAsync(token, workspaceRef, workflowRefId);
                    var match = lastRuns.FirstOrDefault(r => string.Equals(r.Status, status, StringComparison.Ordinal));
                    return (match is not null, match, DescribeRuns(lastRuns));
                },
                timeout,
                TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException ex) when (lastRuns.Count > 0)
        {
            throw new TimeoutException($"{ex.Message}; {await DescribeRecentHistoryAsync(token, workspaceRef, lastRuns[0].RefId)}", ex);
        }
    }

    /// <summary>
    /// Polls a run until it reaches any terminal status and returns its final summary. Throws a
    /// <see cref="TimeoutException"/> with the status last seen and the run's latest history events.
    /// The return type stays nullable for the callers written when it returned null on timeout.
    /// </summary>
    public async Task<RunSummaryDto?> WaitForRunTerminalAsync(string token, Guid workspaceRef, Guid runRefId, TimeSpan timeout)
    {
        try
        {
            return await PollUntilAsync<RunSummaryDto?>(
                $"run {runRefId} to finish",
                async () =>
                {
                    var detail = await GetRunDetailAsync(token, workspaceRef, runRefId);
                    return (IsTerminalStatus(detail.Summary.Status), detail.Summary, $"status {detail.Summary.Status}");
                },
                timeout,
                TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{ex.Message}; {await DescribeRecentHistoryAsync(token, workspaceRef, runRefId)}", ex);
        }
    }

    /// <summary>
    /// Polls a run's history until an event matches <paramref name="match"/>, and returns that event.
    /// Throws a <see cref="TimeoutException"/> listing the latest events seen.
    /// </summary>
    public Task<HistoryEventDto> WaitForHistoryEventAsync(
        string token, Guid workspaceRef, Guid runRefId, Func<HistoryEventDto, bool> match, string what, TimeSpan timeout) =>
        PollUntilAsync<HistoryEventDto>(
            $"{what} in the history of run {runRefId}",
            async () =>
            {
                var history = await GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
                var found = history.FirstOrDefault(match);
                return (found is not null, found!, DescribeHistory(history));
            },
            timeout,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// Polls a run's history until a branch has parked on a bookmark (a <c>BranchParked</c> event:
    /// AwaitSignal, WaitForHttp, Delay and the other waits all write one), and returns that event.
    /// <para>
    /// The engine records the park just before it writes the bookmark row, so a signal or wake sent
    /// the instant this returns can still find nothing to resume. Send a signal with
    /// <see cref="SendSignalUntilMatchedAsync"/>, and repeat an http wake until its effect shows.
    /// </para>
    /// </summary>
    public Task<HistoryEventDto> WaitForBranchParkedAsync(string token, Guid workspaceRef, Guid runRefId, TimeSpan timeout) =>
        WaitForHistoryEventAsync(
            token, workspaceRef, runRefId,
            e => string.Equals(e.EventKind, "BranchParked", StringComparison.Ordinal),
            "a BranchParked event",
            timeout);

    /// <summary>
    /// Sends a signal until the engine reports it resumed a bookmark, and returns the outcome. Safe
    /// to repeat: every signal is its own inbound event, and one that finds no bookmark is dropped
    /// rather than kept for later. Throws a <see cref="TimeoutException"/> with the last outcome.
    /// </summary>
    public Task<string> SendSignalUntilMatchedAsync(
        string token, Guid workspaceRef, Guid runRefId, string signalName, TimeSpan timeout, object? payload = null) =>
        PollUntilAsync<string>(
            $"signal '{signalName}' to resume run {runRefId}",
            async () =>
            {
                var (matched, outcome) = await SendSignalAsync(token, workspaceRef, runRefId, signalName, payload);
                return (matched, outcome, $"outcome {outcome}");
            },
            timeout,
            TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// Waits for a workflow's manual trigger to be live in the engine by starting manual runs until
    /// one is started (a trigger the engine has not picked up yet answers 409
    /// WORKFLOW_HAS_NO_MANUAL_TRIGGER), and returns that run's RefId. It leaves exactly one run behind.
    /// </summary>
    public Task<Guid> WaitForManualTriggerLiveAsync(string token, Guid workspaceRef, Guid workflowRefId, Guid triggerNodeId, TimeSpan timeout) =>
        PollUntilAsync<Guid>(
            $"the manual trigger of workflow {workflowRefId} to start a run",
            async () =>
            {
                using var resp = await StartManualRunRawAsync(token, workspaceRef, workflowRefId, triggerNodeId.ToString(), idempotencyKey: null);
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, Guid.Empty, $"HTTP {(int)resp.StatusCode} {await ReadErrorCodeAsync(resp)}");
                }

                var started = await ReadStartRunResponseAsync(resp);
                return (started.Outcome == StartRunOutcome.Started, started.RunRefId, $"outcome {started.Outcome}");
            },
            timeout,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// Fires a webhook until a run of <paramref name="workflowRefId"/> appears, and returns the newest
    /// run's RefId. Nothing reports when a newly published trigger is live in the engine (its trigger
    /// registrations are cached there and dropped by an event after a publish), and the public
    /// callback answers 202 either way, so a run appearing is the signal. Deliveries sent before the
    /// trigger was live, or still on the queue, can start more runs after this returns: count runs by
    /// a marker in their trigger data (<see cref="ListRunsCarryingAsync"/>) rather than by total.
    /// </summary>
    public Task<Guid> FireWebhookUntilRunAsync(string token, Guid workspaceRef, Guid workflowRefId, string path, TimeSpan timeout, object? payload = null) =>
        PollUntilAsync<Guid>(
            $"a webhook on '{path}' to start a run of workflow {workflowRefId}",
            async () =>
            {
                await SendWebhookAsync(workspaceRef, path, payload);
                var runs = await ListRunsAsync(token, workspaceRef, workflowRefId);
                return (runs.Count > 0, runs.Count > 0 ? runs[0].RefId : Guid.Empty, DescribeRuns(runs));
            },
            timeout,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// The runs of a workflow whose branch state contains <paramref name="marker"/>. A run's root
    /// branch keeps its trigger data under <c>trigger</c>, so a unique value sent in a webhook body or
    /// device message identifies the runs that delivery started.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListRunsCarryingAsync(string token, Guid workspaceRef, Guid workflowRefId, string marker)
    {
        var carrying = new List<Guid>();
        foreach (var run in await ListRunsAsync(token, workspaceRef, workflowRefId, top: 200))
        {
            var detail = await GetRunDetailAsync(token, workspaceRef, run.RefId);
            if (detail.Branches.Any(b => b.LocalJson is { } json && json.Contains(marker, StringComparison.Ordinal)))
            {
                carrying.Add(run.RefId);
            }
        }

        return carrying;
    }

    /// <summary>Polls until at least <paramref name="count"/> runs of a workflow carry <paramref name="marker"/>, and returns them.</summary>
    public Task<IReadOnlyList<Guid>> WaitForRunsCarryingAsync(string token, Guid workspaceRef, Guid workflowRefId, string marker, int count, TimeSpan timeout) =>
        PollUntilAsync<IReadOnlyList<Guid>>(
            $"{count} run(s) of workflow {workflowRefId} carrying '{marker}'",
            async () =>
            {
                var runs = await ListRunsCarryingAsync(token, workspaceRef, workflowRefId, marker);
                return (runs.Count >= count, runs, $"{runs.Count} carrying it");
            },
            timeout,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// A bounded wait for the few cases with no observable signal to wait for instead. It is a plain
    /// delay; <paramref name="reason"/> exists so every such wait says, where it is used, why nothing
    /// better is available. Waiting for something that can be observed belongs in the helpers above.
    /// </summary>
    public static Task WaitWithoutSignalAsync(TimeSpan duration, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return Task.Delay(duration);
    }

    private static string DescribeRuns(IReadOnlyList<RunSummaryDto> runs) =>
        runs.Count == 0
            ? "no runs"
            : $"{runs.Count} run(s): {string.Join(", ", runs.Take(10).Select(r => $"{r.RefId} {r.Status}"))}";

    private static string DescribeHistory(IReadOnlyList<HistoryEventDto> history) =>
        history.Count == 0
            ? "no history events"
            : $"{history.Count} history event(s), latest: {string.Join(", ", history.TakeLast(8).Select(DescribeEvent))}";

    // A failure's kind alone says nothing about why it failed, and CI keeps the host logs only as an
    // artifact; the error code and message travel on the event's payload, so the failure that cost
    // the run reads straight from the test output.
    private static string DescribeEvent(HistoryEventDto e)
    {
        string node = e.NodeId is { } nodeId ? $"@{nodeId.ToString("N")[..8]}" : string.Empty;
        if (!e.EventKind.EndsWith("Failed", StringComparison.Ordinal) && !e.EventKind.EndsWith("Abandoned", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(e.PayloadJson))
        {
            return e.EventKind + node;
        }

        try
        {
            using var payload = JsonDocument.Parse(e.PayloadJson);
            string? code = payload.RootElement.TryGetProperty("errorCode", out var c) ? c.ToString() : null;
            string? message = payload.RootElement.TryGetProperty("message", out var m) ? m.ToString() : null;
            return $"{e.EventKind}{node}({code}: {message})";
        }
        catch (JsonException)
        {
            return $"{e.EventKind}{node}({e.PayloadJson})";
        }
    }

    // Best effort: this only adds detail to a failure that is already being reported.
    private async Task<string> DescribeRecentHistoryAsync(string token, Guid workspaceRef, Guid runRefId)
    {
        try
        {
            return $"run {runRefId} has {DescribeHistory(await GetHistoryAsync(token, workspaceRef, runRefId, top: 1000))}";
        }
        catch (Exception ex)
        {
            return $"its history could not be read ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>Reads the central event-log feed for a workspace, optionally filtered by event name.</summary>
    public async Task<IReadOnlyList<EventLogResponse>> GetEventLogsAsync(string token, Guid workspaceRef, string? eventName = null, int take = 200)
    {
        var search = string.IsNullOrWhiteSpace(eventName) ? null : new EventLogSearch { EventName = eventName };
        var page = await Management.As(token).GetEventLogsAsync(workspaceRef, search, limit: take);

        return page.Items.ToList();
    }

    /// <summary>The set of run statuses that represent a finalized run.</summary>
    public static bool IsTerminalStatus(string status) =>
        status is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled";

    // ─────────────────────────────────────────────────────────────────────────
    // Polling helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls <paramref name="predicate"/> every <paramref name="interval"/> (2 s by default) until it
    /// returns true or <paramref name="timeout"/> elapses, and returns whether it did. The predicate
    /// is always checked once more at the deadline. Prefer <see cref="PollUntilAsync{T}"/>, whose
    /// timeout says what was last seen.
    /// </summary>
    public static async Task<bool> PollAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        TimeSpan? interval = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = interval ?? TimeSpan.FromSeconds(2);

        while (true)
        {
            if (await predicate())
            {
                return true;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(remaining < delay ? remaining : delay);
        }
    }

    /// <summary>
    /// Polls <paramref name="probe"/> every <paramref name="interval"/> (1 s by default) until it
    /// reports done, and returns the value it reported with it. Each probe also describes what it
    /// saw; when <paramref name="timeout"/> elapses first this throws a <see cref="TimeoutException"/>
    /// reading "Timed out after {elapsed} waiting for {what}; last state: {state}". The probe is
    /// always run once more at the deadline.
    /// </summary>
    public static async Task<T> PollUntilAsync<T>(
        string what,
        Func<Task<(bool Done, T Value, string State)>> probe,
        TimeSpan timeout,
        TimeSpan? interval = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = interval ?? TimeSpan.FromSeconds(1);

        while (true)
        {
            var (done, value, state) = await probe();
            if (done)
            {
                return value;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    $"Timed out after {stopwatch.Elapsed.TotalSeconds:0.0} s (limit {timeout.TotalSeconds:0.#} s) waiting for {what}; last state: {state}");
            }

            await Task.Delay(remaining < delay ? remaining : delay);
        }
    }

    /// <summary>
    /// <see cref="PollUntilAsync{T}"/> for a probe with nothing to return: polls until it reports
    /// done, or throws a <see cref="TimeoutException"/> naming <paramref name="what"/> and the last state.
    /// </summary>
    public static Task PollUntilAsync(
        string what,
        Func<Task<(bool Done, string State)>> probe,
        TimeSpan timeout,
        TimeSpan? interval = null) =>
        PollUntilAsync<bool>(
            what,
            async () =>
            {
                var (done, state) = await probe();
                return (done, done, state);
            },
            timeout,
            interval);

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();
}
