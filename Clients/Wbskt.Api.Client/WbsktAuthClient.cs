using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Api.Client;

/// <summary>
/// The auth host's API: accounts and sessions, workspaces, and tenant administration (members,
/// invitations, roles, groups, permissions). Each method sends the request, throws
/// <see cref="ApiException"/> on a failure status and returns the contract type from
/// <c>Wbskt.Auth.Models</c>; its <c>RawAsync</c> twin returns the response untouched.
/// </summary>
public sealed class WbsktAuthClient : WbsktApiClient
{
    /// <param name="http">Shared across clients; its handler and timeout are the caller's.</param>
    /// <param name="baseAddress">The auth host, e.g. <c>https://auth.example.com</c>. Defaults to <paramref name="http"/>'s BaseAddress.</param>
    /// <param name="accessToken">The bearer token to send, or null to call anonymously.</param>
    public WbsktAuthClient(HttpClient http, Uri? baseAddress = null, string? accessToken = null)
        : base(http, baseAddress, accessToken)
    {
    }

    /// <summary>A client for the same host that sends <paramref name="accessToken"/> (or nothing, when null).</summary>
    public WbsktAuthClient As(string? accessToken) => new(Http, BaseAddress, accessToken);

    // ── Account ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates an account. Without an invitation the account gets a tenant of its own. <c>POST /api/auth/register</c>.</summary>
    public Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RegisterRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="RegisterAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RegisterRawAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/register", request, cancellationToken);

    /// <summary>Signs in with an email and password and returns the token pair. <c>POST /api/auth/login</c>.</summary>
    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<LoginResponse>(LoginRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="LoginAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> LoginRawAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/login", request, cancellationToken);

    /// <summary>Rotates a refresh token for a new token pair. A null request relies on the refresh cookie. <c>POST /api/auth/refresh-token</c>.</summary>
    public Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest? request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<LoginResponse>(RefreshTokenRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="RefreshTokenAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RefreshTokenRawAsync(RefreshTokenRequest? request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/refresh-token", request, cancellationToken);

    /// <summary>Revokes one refresh token. Answers 204 whether or not it was live. <c>POST /api/auth/logout</c>.</summary>
    public Task LogoutAsync(RefreshTokenRequest? request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(LogoutRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="LogoutAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> LogoutRawAsync(RefreshTokenRequest? request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/logout", request, cancellationToken);

    /// <summary>Revokes every refresh token the bearer holds. <c>POST /api/auth/logout-all</c>.</summary>
    public Task LogoutAllAsync(CancellationToken cancellationToken = default) =>
        ReceiveAsync(LogoutAllRawAsync(cancellationToken), cancellationToken);

    /// <summary><see cref="LogoutAllAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> LogoutAllRawAsync(CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/logout-all", null, cancellationToken);

    /// <summary>Changes the bearer's password and returns a fresh token pair; every other session ends. <c>POST /api/auth/change-password</c>.</summary>
    public Task<LoginResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<LoginResponse>(ChangePasswordRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="ChangePasswordAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ChangePasswordRawAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/change-password", request, cancellationToken);

    /// <summary>The bearer's live sign-in sessions. <c>GET /api/auth/sessions</c>.</summary>
    public Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<SessionResponse>>(GetSessionsRawAsync(cancellationToken), cancellationToken);

    /// <summary><see cref="GetSessionsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetSessionsRawAsync(CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, "/api/auth/sessions", null, cancellationToken);

    /// <summary>Ends one of the bearer's sessions. <c>DELETE /api/auth/sessions/{sessionId}</c>.</summary>
    public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RevokeSessionRawAsync(sessionId, cancellationToken), cancellationToken);

    /// <summary><see cref="RevokeSessionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RevokeSessionRawAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/auth/sessions/{sessionId}", null, cancellationToken);

    /// <summary>Asks for a password-reset link. Answers the same for every address. <c>POST /api/auth/forgot-password</c>.</summary>
    public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(ForgotPasswordRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="ForgotPasswordAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ForgotPasswordRawAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/forgot-password", request, cancellationToken);

    /// <summary>Redeems a password-reset link. <c>POST /api/auth/reset-password</c>.</summary>
    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(ResetPasswordRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="ResetPasswordAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ResetPasswordRawAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/reset-password", request, cancellationToken);

    /// <summary>Redeems an email confirmation link. <c>POST /api/auth/verify-email</c>.</summary>
    public Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(VerifyEmailRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="VerifyEmailAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> VerifyEmailRawAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/verify-email", request, cancellationToken);

    /// <summary>Asks for a fresh confirmation link. Answers the same for every address. <c>POST /api/auth/resend-verification</c>.</summary>
    public Task ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(ResendVerificationRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="ResendVerificationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ResendVerificationRawAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/auth/resend-verification", request, cancellationToken);

    /// <summary>Redeems an invitation for the bearer, who joins the inviting tenant. <c>POST /api/invitations/accept</c>.</summary>
    public Task<AcceptInvitationResponse> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<AcceptInvitationResponse>(AcceptInvitationRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="AcceptInvitationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> AcceptInvitationRawAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/invitations/accept", request, cancellationToken);

    // ── Workspaces ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The workspaces the bearer can see. <c>GET /api/workspaces</c>.</summary>
    public Task<IReadOnlyList<WorkspaceResponse>> GetWorkspacesAsync(CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<WorkspaceResponse>>(GetWorkspacesRawAsync(cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkspacesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkspacesRawAsync(CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, "/api/workspaces", null, cancellationToken);

    /// <summary>Creates a workspace owned by the bearer. <c>POST /api/workspaces</c>.</summary>
    public Task<WorkspaceResponse> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkspaceResponse>(CreateWorkspaceRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateWorkspaceAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateWorkspaceRawAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/workspaces", request, cancellationToken);

    /// <summary>Renames a workspace or changes its description. <c>PUT /api/workspaces/{workspaceRef}</c>.</summary>
    public Task UpdateWorkspaceAsync(Guid workspaceRef, CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateWorkspaceRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateWorkspaceAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateWorkspaceRawAsync(Guid workspaceRef, CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/workspaces/{workspaceRef}", request, cancellationToken);

    /// <summary>Deletes a workspace. <c>DELETE /api/workspaces/{workspaceRef}</c>.</summary>
    public Task DeleteWorkspaceAsync(Guid workspaceRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteWorkspaceRawAsync(workspaceRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteWorkspaceAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteWorkspaceRawAsync(Guid workspaceRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}", null, cancellationToken);

    /// <summary>Hands a workspace to another member of its tenant. <c>PUT /api/workspaces/{workspaceRef}/owner</c>.</summary>
    public Task TransferWorkspaceOwnershipAsync(Guid workspaceRef, TransferOwnershipRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(TransferWorkspaceOwnershipRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="TransferWorkspaceOwnershipAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> TransferWorkspaceOwnershipRawAsync(Guid workspaceRef, TransferOwnershipRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/workspaces/{workspaceRef}/owner", request, cancellationToken);

    /// <summary>Adds a tenant member to a workspace by email. <c>POST /api/workspaces/{workspaceRef}/members</c>.</summary>
    public Task AddWorkspaceMemberAsync(Guid workspaceRef, AddMemberRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(AddWorkspaceMemberRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="AddWorkspaceMemberAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> AddWorkspaceMemberRawAsync(Guid workspaceRef, AddMemberRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/members", request, cancellationToken);

    /// <summary>A page of a workspace's members. <c>GET /api/workspaces/{workspaceRef}/members</c>.</summary>
    public Task<Page<TenantMemberResponse>> GetWorkspaceMembersAsync(Guid workspaceRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<TenantMemberResponse>>(GetWorkspaceMembersRawAsync(workspaceRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkspaceMembersAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkspaceMembersRawAsync(Guid workspaceRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/members{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Removes a member from a workspace. <c>DELETE /api/workspaces/{workspaceRef}/members/{userRef}</c>.</summary>
    public Task RemoveWorkspaceMemberAsync(Guid workspaceRef, Guid userRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveWorkspaceMemberRawAsync(workspaceRef, userRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveWorkspaceMemberAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveWorkspaceMemberRawAsync(Guid workspaceRef, Guid userRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}/members/{userRef}", null, cancellationToken);

    /// <summary>The internal workspace id and the bearer's effective permissions in it. A host-to-host route (<c>/api/internal</c>): never routed publicly, so only a caller on the backend network reaches it. <c>POST /api/internal/workspaces/resolve</c>.</summary>
    public Task<ResolvedWorkspaceResponse> ResolveWorkspaceAsync(ResolveWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ResolvedWorkspaceResponse>(ResolveWorkspaceRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="ResolveWorkspaceAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ResolveWorkspaceRawAsync(ResolveWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/internal/workspaces/resolve", request, cancellationToken);

    // ── Tenants ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The tenants the bearer belongs to. <c>GET /api/tenants</c>.</summary>
    public Task<IReadOnlyList<TenantResponse>> GetTenantsAsync(CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<TenantResponse>>(GetTenantsRawAsync(cancellationToken), cancellationToken);

    /// <summary><see cref="GetTenantsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetTenantsRawAsync(CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, "/api/tenants", null, cancellationToken);

    /// <summary>Creates a tenant administered by the bearer. <c>POST /api/tenants</c>.</summary>
    public Task<TenantResponse> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<TenantResponse>(CreateTenantRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateTenantAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateTenantRawAsync(CreateTenantRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/tenants", request, cancellationToken);

    /// <summary>Renames a tenant or changes its description. <c>PUT /api/tenants/{tenantRef}</c>.</summary>
    public Task UpdateTenantAsync(Guid tenantRef, UpdateTenantRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateTenantRawAsync(tenantRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateTenantAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateTenantRawAsync(Guid tenantRef, UpdateTenantRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/tenants/{tenantRef}", request, cancellationToken);

    /// <summary>A page of a tenant's outstanding invitations. <c>GET /api/tenants/{tenantRef}/invitations</c>.</summary>
    public Task<Page<InvitationResponse>> GetInvitationsAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<InvitationResponse>>(GetInvitationsRawAsync(tenantRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetInvitationsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetInvitationsRawAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/invitations{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Invites an address to a tenant. The response carries the raw token, the only time it is available. <c>POST /api/tenants/{tenantRef}/invitations</c>.</summary>
    public Task<CreatedInvitationResponse> CreateInvitationAsync(Guid tenantRef, CreateInvitationRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<CreatedInvitationResponse>(CreateInvitationRawAsync(tenantRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateInvitationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateInvitationRawAsync(Guid tenantRef, CreateInvitationRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/invitations", request, cancellationToken);

    /// <summary>Withdraws an invitation. <c>DELETE /api/tenants/{tenantRef}/invitations/{invitationRef}</c>.</summary>
    public Task RevokeInvitationAsync(Guid tenantRef, Guid invitationRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RevokeInvitationRawAsync(tenantRef, invitationRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RevokeInvitationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RevokeInvitationRawAsync(Guid tenantRef, Guid invitationRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/invitations/{invitationRef}", null, cancellationToken);

    /// <summary>A page of a tenant's roles. <c>GET /api/tenants/{tenantRef}/roles</c>.</summary>
    public Task<Page<RoleResponse>> GetRolesAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<RoleResponse>>(GetRolesRawAsync(tenantRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetRolesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetRolesRawAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/roles{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Creates a role. <c>POST /api/tenants/{tenantRef}/roles</c>.</summary>
    public Task<RoleResponse> CreateRoleAsync(Guid tenantRef, CreateRoleRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RoleResponse>(CreateRoleRawAsync(tenantRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateRoleRawAsync(Guid tenantRef, CreateRoleRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/roles", request, cancellationToken);

    /// <summary>Renames a role or changes its description. <c>PUT /api/tenants/{tenantRef}/roles/{roleRef}</c>.</summary>
    public Task UpdateRoleAsync(Guid tenantRef, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateRoleRawAsync(tenantRef, roleRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateRoleRawAsync(Guid tenantRef, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/tenants/{tenantRef}/roles/{roleRef}", request, cancellationToken);

    /// <summary>Deletes a role. <c>DELETE /api/tenants/{tenantRef}/roles/{roleRef}</c>.</summary>
    public Task DeleteRoleAsync(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteRoleRawAsync(tenantRef, roleRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteRoleRawAsync(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/roles/{roleRef}", null, cancellationToken);

    /// <summary>The permissions attached to a role, with their deny flags. <c>GET /api/tenants/{tenantRef}/roles/{roleRef}/permissions</c>.</summary>
    public Task<IReadOnlyList<RolePermissionAssignmentResponse>> GetRolePermissionsAsync(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<RolePermissionAssignmentResponse>>(GetRolePermissionsRawAsync(tenantRef, roleRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetRolePermissionsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetRolePermissionsRawAsync(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/roles/{roleRef}/permissions", null, cancellationToken);

    /// <summary>Grants or denies a permission on a role. <c>POST /api/tenants/{tenantRef}/roles/{roleRef}/permissions</c>.</summary>
    public Task GrantRolePermissionAsync(Guid tenantRef, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(GrantRolePermissionRawAsync(tenantRef, roleRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="GrantRolePermissionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GrantRolePermissionRawAsync(Guid tenantRef, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/roles/{roleRef}/permissions", request, cancellationToken);

    /// <summary>Detaches a permission from a role. <c>DELETE /api/tenants/{tenantRef}/roles/{roleRef}/permissions/{slug}</c>.</summary>
    public Task RemoveRolePermissionAsync(Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveRolePermissionRawAsync(tenantRef, roleRef, slug, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveRolePermissionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveRolePermissionRawAsync(Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/roles/{roleRef}/permissions/{E(slug)}", null, cancellationToken);

    /// <summary>A page of a tenant's groups. <c>GET /api/tenants/{tenantRef}/groups</c>.</summary>
    public Task<Page<GroupResponse>> GetGroupsAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<GroupResponse>>(GetGroupsRawAsync(tenantRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetGroupsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetGroupsRawAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/groups{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Creates a group. <c>POST /api/tenants/{tenantRef}/groups</c>.</summary>
    public Task<GroupResponse> CreateGroupAsync(Guid tenantRef, CreateGroupRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<GroupResponse>(CreateGroupRawAsync(tenantRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateGroupAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateGroupRawAsync(Guid tenantRef, CreateGroupRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/groups", request, cancellationToken);

    /// <summary>Renames a group. <c>PUT /api/tenants/{tenantRef}/groups/{groupRef}</c>.</summary>
    public Task UpdateGroupAsync(Guid tenantRef, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateGroupRawAsync(tenantRef, groupRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateGroupAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateGroupRawAsync(Guid tenantRef, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/tenants/{tenantRef}/groups/{groupRef}", request, cancellationToken);

    /// <summary>Deletes a group. <c>DELETE /api/tenants/{tenantRef}/groups/{groupRef}</c>.</summary>
    public Task DeleteGroupAsync(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteGroupRawAsync(tenantRef, groupRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteGroupAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteGroupRawAsync(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/groups/{groupRef}", null, cancellationToken);

    /// <summary>The roles a group holds, with their scopes. <c>GET /api/tenants/{tenantRef}/groups/{groupRef}/roles</c>.</summary>
    public Task<IReadOnlyList<RoleAssignmentResponse>> GetGroupRolesAsync(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<RoleAssignmentResponse>>(GetGroupRolesRawAsync(tenantRef, groupRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetGroupRolesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetGroupRolesRawAsync(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/groups/{groupRef}/roles", null, cancellationToken);

    /// <summary>Assigns a role to a group, tenant-wide or in one workspace. <c>POST /api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}</c>.</summary>
    public Task AssignGroupRoleAsync(Guid tenantRef, Guid groupRef, Guid roleRef, AssignmentScopeRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(AssignGroupRoleRawAsync(tenantRef, groupRef, roleRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="AssignGroupRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> AssignGroupRoleRawAsync(Guid tenantRef, Guid groupRef, Guid roleRef, AssignmentScopeRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}", request, cancellationToken);

    /// <summary>Removes a group's role assignment at one scope (tenant-wide when <paramref name="workspaceRef"/> is null). <c>DELETE /api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}</c>.</summary>
    public Task RemoveGroupRoleAsync(Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveGroupRoleRawAsync(tenantRef, groupRef, roleRef, workspaceRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveGroupRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveGroupRoleRawAsync(Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/groups/{groupRef}/roles/{roleRef}{Query(("workspaceRef", workspaceRef))}", null, cancellationToken);

    /// <summary>A page of the permissions a tenant can grant. <c>GET /api/tenants/{tenantRef}/permissions</c>.</summary>
    public Task<Page<PermissionResponse>> GetPermissionsAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<PermissionResponse>>(GetPermissionsRawAsync(tenantRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetPermissionsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetPermissionsRawAsync(Guid tenantRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/permissions{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>A page of a tenant's members, optionally filtered by name or email. <c>GET /api/tenants/{tenantRef}/members</c>.</summary>
    public Task<Page<TenantMemberResponse>> GetTenantMembersAsync(Guid tenantRef, string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<TenantMemberResponse>>(GetTenantMembersRawAsync(tenantRef, search, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetTenantMembersAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetTenantMembersRawAsync(Guid tenantRef, string? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/members{Query(("search", search), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>The roles a member holds directly, with their scopes. <c>GET /api/tenants/{tenantRef}/members/{userRef}/roles</c>.</summary>
    public Task<IReadOnlyList<RoleAssignmentResponse>> GetMemberRolesAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<RoleAssignmentResponse>>(GetMemberRolesRawAsync(tenantRef, userRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetMemberRolesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetMemberRolesRawAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/members/{userRef}/roles", null, cancellationToken);

    /// <summary>The permissions granted or denied to a member directly. <c>GET /api/tenants/{tenantRef}/members/{userRef}/permissions</c>.</summary>
    public Task<IReadOnlyList<UserPermissionAssignmentResponse>> GetMemberPermissionsAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<UserPermissionAssignmentResponse>>(GetMemberPermissionsRawAsync(tenantRef, userRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetMemberPermissionsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetMemberPermissionsRawAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/members/{userRef}/permissions", null, cancellationToken);

    /// <summary>The groups a member belongs to. <c>GET /api/tenants/{tenantRef}/members/{userRef}/groups</c>.</summary>
    public Task<IReadOnlyList<GroupMembershipResponse>> GetMemberGroupsAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IReadOnlyList<GroupMembershipResponse>>(GetMemberGroupsRawAsync(tenantRef, userRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetMemberGroupsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetMemberGroupsRawAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/tenants/{tenantRef}/members/{userRef}/groups", null, cancellationToken);

    /// <summary>Adds a member to a group. <c>POST /api/tenants/{tenantRef}/members/{userRef}/groups/{groupRef}</c>.</summary>
    public Task AddMemberToGroupAsync(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(AddMemberToGroupRawAsync(tenantRef, userRef, groupRef, cancellationToken), cancellationToken);

    /// <summary><see cref="AddMemberToGroupAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> AddMemberToGroupRawAsync(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/members/{userRef}/groups/{groupRef}", null, cancellationToken);

    /// <summary>Removes a member from a group. <c>DELETE /api/tenants/{tenantRef}/members/{userRef}/groups/{groupRef}</c>.</summary>
    public Task RemoveMemberFromGroupAsync(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveMemberFromGroupRawAsync(tenantRef, userRef, groupRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveMemberFromGroupAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveMemberFromGroupRawAsync(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/members/{userRef}/groups/{groupRef}", null, cancellationToken);

    /// <summary>Assigns a role to a member, tenant-wide or in one workspace. <c>POST /api/tenants/{tenantRef}/members/{userRef}/roles/{roleRef}</c>.</summary>
    public Task AssignMemberRoleAsync(Guid tenantRef, Guid userRef, Guid roleRef, AssignmentScopeRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(AssignMemberRoleRawAsync(tenantRef, userRef, roleRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="AssignMemberRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> AssignMemberRoleRawAsync(Guid tenantRef, Guid userRef, Guid roleRef, AssignmentScopeRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/members/{userRef}/roles/{roleRef}", request, cancellationToken);

    /// <summary>Removes a member's role assignment at one scope (tenant-wide when <paramref name="workspaceRef"/> is null). <c>DELETE /api/tenants/{tenantRef}/members/{userRef}/roles/{roleRef}</c>.</summary>
    public Task RemoveMemberRoleAsync(Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveMemberRoleRawAsync(tenantRef, userRef, roleRef, workspaceRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveMemberRoleAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveMemberRoleRawAsync(Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/members/{userRef}/roles/{roleRef}{Query(("workspaceRef", workspaceRef))}", null, cancellationToken);

    /// <summary>Grants or denies a permission to a member directly. <c>POST /api/tenants/{tenantRef}/members/{userRef}/permissions</c>.</summary>
    public Task GrantMemberPermissionAsync(Guid tenantRef, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(GrantMemberPermissionRawAsync(tenantRef, userRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="GrantMemberPermissionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GrantMemberPermissionRawAsync(Guid tenantRef, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/tenants/{tenantRef}/members/{userRef}/permissions", request, cancellationToken);

    /// <summary>Removes a member's direct permission at one scope (tenant-wide when <paramref name="workspaceRef"/> is null). <c>DELETE /api/tenants/{tenantRef}/members/{userRef}/permissions/{slug}</c>.</summary>
    public Task RemoveMemberPermissionAsync(Guid tenantRef, Guid userRef, string slug, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveMemberPermissionRawAsync(tenantRef, userRef, slug, workspaceRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveMemberPermissionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveMemberPermissionRawAsync(Guid tenantRef, Guid userRef, string slug, Guid? workspaceRef = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/members/{userRef}/permissions/{E(slug)}{Query(("workspaceRef", workspaceRef))}", null, cancellationToken);

    /// <summary>Removes a member from a tenant. <c>DELETE /api/tenants/{tenantRef}/members/{userRef}</c>.</summary>
    public Task RemoveTenantMemberAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(RemoveTenantMemberRawAsync(tenantRef, userRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RemoveTenantMemberAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RemoveTenantMemberRawAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/tenants/{tenantRef}/members/{userRef}", null, cancellationToken);

    /// <summary>Suspends a member in one tenant, or lifts the suspension. <c>PUT /api/tenants/{tenantRef}/members/{userRef}/suspended</c>.</summary>
    public Task SetMemberSuspendedAsync(Guid tenantRef, Guid userRef, SetMemberSuspendedRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(SetMemberSuspendedRawAsync(tenantRef, userRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="SetMemberSuspendedAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SetMemberSuspendedRawAsync(Guid tenantRef, Guid userRef, SetMemberSuspendedRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/tenants/{tenantRef}/members/{userRef}/suspended", request, cancellationToken);
}
