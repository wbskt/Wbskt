using Wbskt.Management.Models;
using Wbskt.Management.Models.Workflow;
using Wbskt.Models;
using Wbskt.Protocol;

namespace Wbskt.Api.Client;

/// <summary>
/// The management host's API: registration policies, devices (registration, login, clients, tags,
/// readings, commands), message templates, the event log, integrations, workflows and their runs, and
/// the public callbacks. Each method sends the request, throws <see cref="ApiException"/> on a failure
/// status and returns the contract type from <c>Wbskt.Management.Models</c> (or <c>Wbskt.Protocol</c>
/// for the device calls); its <c>RawAsync</c> twin returns the response untouched.
/// </summary>
public sealed class WbsktManagementClient : WbsktApiClient
{
    /// <param name="http">Shared across clients; its handler and timeout are the caller's.</param>
    /// <param name="baseAddress">The management host, e.g. <c>https://api.example.com</c>. Defaults to <paramref name="http"/>'s BaseAddress.</param>
    /// <param name="accessToken">The bearer token to send, or null to call anonymously.</param>
    public WbsktManagementClient(HttpClient http, Uri? baseAddress = null, string? accessToken = null)
        : base(http, baseAddress, accessToken)
    {
    }

    /// <summary>A client for the same host that sends <paramref name="accessToken"/> (or nothing, when null).</summary>
    public WbsktManagementClient As(string? accessToken) => new(Http, BaseAddress, accessToken);

    private static string SearchQuery(EventLogSearch? search, params (string Name, object? Value)[] more) =>
        Query((search?.ToQuery() ?? Enumerable.Empty<(string Name, object? Value)>()).Concat(more).ToArray());

    // ── Registration policies ─────────────────────────────────────────────────────────────────────

    /// <summary>A page of a workspace's registration policies. <c>GET /api/workspaces/{workspaceRef}/registration-policies</c>.</summary>
    public Task<Page<RegistrationPolicyResponse>> GetPoliciesAsync(Guid workspaceRef, bool? autoApproval = null, string? name = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<RegistrationPolicyResponse>>(GetPoliciesRawAsync(workspaceRef, autoApproval, name, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetPoliciesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetPoliciesRawAsync(Guid workspaceRef, bool? autoApproval = null, string? name = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/registration-policies{Query(("autoApproval", autoApproval), ("name", name), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>One registration policy. <c>GET /api/workspaces/{workspaceRef}/registration-policies/{policyRef}</c>.</summary>
    public Task<RegistrationPolicyResponse> GetPolicyAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RegistrationPolicyResponse>(GetPolicyRawAsync(workspaceRef, policyRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetPolicyAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetPolicyRawAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/registration-policies/{policyRef}", null, cancellationToken);

    /// <summary>Creates a registration policy; the response carries its PIN. <c>POST /api/workspaces/{workspaceRef}/registration-policies</c>.</summary>
    public Task<RegistrationPolicyResponse> CreatePolicyAsync(Guid workspaceRef, RegistrationPolicyRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RegistrationPolicyResponse>(CreatePolicyRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreatePolicyAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreatePolicyRawAsync(Guid workspaceRef, RegistrationPolicyRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/registration-policies", request, cancellationToken);

    /// <summary>Changes a registration policy. <c>PATCH /api/workspaces/{workspaceRef}/registration-policies/{policyRef}</c>.</summary>
    public Task UpdatePolicyAsync(Guid workspaceRef, Guid policyRef, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdatePolicyRawAsync(workspaceRef, policyRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdatePolicyAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdatePolicyRawAsync(Guid workspaceRef, Guid policyRef, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Patch, $"/api/workspaces/{workspaceRef}/registration-policies/{policyRef}", request, cancellationToken);

    /// <summary>Issues a policy a new PIN; the old one stops working. <c>POST /api/workspaces/{workspaceRef}/registration-policies/{policyRef}/rotate-pin</c>.</summary>
    public Task<RegistrationPolicyResponse> RotatePolicyPinAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RegistrationPolicyResponse>(RotatePolicyPinRawAsync(workspaceRef, policyRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RotatePolicyPinAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RotatePolicyPinRawAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/registration-policies/{policyRef}/rotate-pin", null, cancellationToken);

    /// <summary>Disables a registration policy. <c>POST /api/workspaces/{workspaceRef}/registration-policies/{policyRef}/disable</c>.</summary>
    public Task DisablePolicyAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DisablePolicyRawAsync(workspaceRef, policyRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DisablePolicyAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DisablePolicyRawAsync(Guid workspaceRef, Guid policyRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/registration-policies/{policyRef}/disable", null, cancellationToken);

    // ── Device registration ───────────────────────────────────────────────────────────────────────

    /// <summary>Registers a device under a policy with its PIN. Anonymous; the response carries the device's secret, the only time it is shown. <c>POST /api/client-registrations/initiate</c>.</summary>
    public Task<ClientRegistrationResponse> RegisterClientAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientRegistrationResponse>(RegisterClientRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="RegisterClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RegisterClientRawAsync(ClientRegistrationRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/client-registrations/initiate", request, cancellationToken);

    /// <summary>Exchanges a device's reference and secret for the access token the socket host takes. <c>POST /api/client-auth/login</c>.</summary>
    public Task<ClientLoginResponse> LoginClientAsync(ClientLoginRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientLoginResponse>(LoginClientRawAsync(request, cancellationToken), cancellationToken);

    /// <summary><see cref="LoginClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> LoginClientRawAsync(ClientLoginRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "/api/client-auth/login", request, cancellationToken);

    // ── Clients ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A page of a workspace's clients, optionally filtered. <c>GET /api/workspaces/{workspaceRef}/clients</c>.</summary>
    public Task<Page<ClientResponse>> GetClientsAsync(Guid workspaceRef, ClientStatus? status = null, string? name = null, string? tag = null, Guid? policyRefId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<ClientResponse>>(GetClientsRawAsync(workspaceRef, status, name, tag, policyRefId, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientsRawAsync(Guid workspaceRef, ClientStatus? status = null, string? name = null, string? tag = null, Guid? policyRefId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients{Query(("status", status), ("name", name), ("tag", tag), ("policyRefId", policyRefId), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Every tag in use in a workspace, with how many clients carry it. <c>GET /api/workspaces/{workspaceRef}/clients/tags</c>.</summary>
    public Task<Page<ClientTagCountResponse>> GetClientTagsAsync(Guid workspaceRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<ClientTagCountResponse>>(GetClientTagsRawAsync(workspaceRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientTagsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientTagsRawAsync(Guid workspaceRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/tags", null, cancellationToken);

    /// <summary>One client in detail. <c>GET /api/workspaces/{workspaceRef}/clients/{clientRef}</c>.</summary>
    public Task<ClientDetailResponse> GetClientAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientDetailResponse>(GetClientRawAsync(workspaceRef, clientRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientRawAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/{clientRef}", null, cancellationToken);

    /// <summary>Renames a client or changes its status; a field left null stays as it is. <c>PATCH /api/workspaces/{workspaceRef}/clients/{clientRef}</c>.</summary>
    public Task UpdateClientAsync(Guid workspaceRef, Guid clientRef, UpdateClientRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateClientRawAsync(workspaceRef, clientRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateClientRawAsync(Guid workspaceRef, Guid clientRef, UpdateClientRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Patch, $"/api/workspaces/{workspaceRef}/clients/{clientRef}", request, cancellationToken);

    /// <summary>Changes the status of many clients at once, reporting each failure separately. <c>PATCH /api/workspaces/{workspaceRef}/clients/status</c>.</summary>
    public Task<BulkClientStatusResponse> UpdateClientStatusesAsync(Guid workspaceRef, BulkClientStatusRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<BulkClientStatusResponse>(UpdateClientStatusesRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateClientStatusesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateClientStatusesRawAsync(Guid workspaceRef, BulkClientStatusRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Patch, $"/api/workspaces/{workspaceRef}/clients/status", request, cancellationToken);

    /// <summary>Deletes a client. <c>DELETE /api/workspaces/{workspaceRef}/clients/{clientRef}</c>.</summary>
    public Task DeleteClientAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteClientRawAsync(workspaceRef, clientRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteClientRawAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}/clients/{clientRef}", null, cancellationToken);

    /// <summary>Issues a client a new secret, shown once; the old one stops working at once. <c>POST /api/workspaces/{workspaceRef}/clients/{clientRef}/rotate-secret</c>.</summary>
    public Task<ClientSecretResponse> RotateClientSecretAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientSecretResponse>(RotateClientSecretRawAsync(workspaceRef, clientRef, cancellationToken), cancellationToken);

    /// <summary><see cref="RotateClientSecretAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RotateClientSecretRawAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/rotate-secret", null, cancellationToken);

    /// <summary>A client's state variables. <c>GET /api/workspaces/{workspaceRef}/clients/{clientRef}/state</c>.</summary>
    public Task<Page<ClientStateVariableResponse>> GetClientStateAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<ClientStateVariableResponse>>(GetClientStateRawAsync(workspaceRef, clientRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientStateAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientStateRawAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/state", null, cancellationToken);

    /// <summary>Replaces a client's tag set. <c>PUT /api/workspaces/{workspaceRef}/clients/{clientRef}/tags</c>.</summary>
    public Task<ClientTagsResponse> SetClientTagsAsync(Guid workspaceRef, Guid clientRef, SetClientTagsRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientTagsResponse>(SetClientTagsRawAsync(workspaceRef, clientRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="SetClientTagsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SetClientTagsRawAsync(Guid workspaceRef, Guid clientRef, SetClientTagsRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/tags", request, cancellationToken);

    /// <summary>Sends a command to a client. <c>POST /api/workspaces/{workspaceRef}/clients/{clientRef}/commands</c>.</summary>
    public Task<ClientCommandResponse> SendClientCommandAsync(Guid workspaceRef, Guid clientRef, ClientCommandRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientCommandResponse>(SendClientCommandRawAsync(workspaceRef, clientRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="SendClientCommandAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SendClientCommandRawAsync(Guid workspaceRef, Guid clientRef, ClientCommandRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/commands", request, cancellationToken);

    /// <summary>A page of a client's traffic (messages, commands, pings) from the event log. <c>GET /api/workspaces/{workspaceRef}/clients/{clientRef}/comms</c>.</summary>
    public Task<Page<EventLogResponse>> GetClientCommsAsync(Guid workspaceRef, Guid clientRef, string? direction = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<EventLogResponse>>(GetClientCommsRawAsync(workspaceRef, clientRef, direction, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientCommsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientCommsRawAsync(Guid workspaceRef, Guid clientRef, string? direction = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/comms{Query(("direction", direction), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Asks a connected client for a round-trip measurement. <c>POST /api/workspaces/{workspaceRef}/clients/{clientRef}/ping</c>.</summary>
    public Task PingClientAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(PingClientRawAsync(workspaceRef, clientRef, cancellationToken), cancellationToken);

    /// <summary><see cref="PingClientAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> PingClientRawAsync(Guid workspaceRef, Guid clientRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/ping", null, cancellationToken);

    /// <summary>A client variable's readings over a time range, in buckets. <c>GET /api/workspaces/{workspaceRef}/clients/{clientRef}/readings</c>.</summary>
    public Task<ClientReadingsResponse> GetClientReadingsAsync(Guid workspaceRef, Guid clientRef, string? name = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? bucket = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<ClientReadingsResponse>(GetClientReadingsRawAsync(workspaceRef, clientRef, name, from, to, bucket, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientReadingsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientReadingsRawAsync(Guid workspaceRef, Guid clientRef, string? name = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? bucket = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/readings{Query(("name", name), ("from", from), ("to", to), ("bucket", bucket))}", null, cancellationToken);

    /// <summary>A client's raw readings over a time range, as CSV. <c>GET /api/workspaces/{workspaceRef}/clients/{clientRef}/readings/csv</c>.</summary>
    public Task<string> GetClientReadingsCsvAsync(Guid workspaceRef, Guid clientRef, string? name = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        ReceiveTextAsync(GetClientReadingsCsvRawAsync(workspaceRef, clientRef, name, from, to, cancellationToken), cancellationToken);

    /// <summary><see cref="GetClientReadingsCsvAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetClientReadingsCsvRawAsync(Guid workspaceRef, Guid clientRef, string? name = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/clients/{clientRef}/readings/csv{Query(("name", name), ("from", from), ("to", to))}", null, cancellationToken);

    // ── Message templates ─────────────────────────────────────────────────────────────────────────

    /// <summary>A page of a workspace's message templates. <c>GET /api/workspaces/{workspaceRef}/message-templates</c>.</summary>
    public Task<Page<MessageTemplateResponse>> GetMessageTemplatesAsync(Guid workspaceRef, Guid? policyRefId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<MessageTemplateResponse>>(GetMessageTemplatesRawAsync(workspaceRef, policyRefId, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetMessageTemplatesAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetMessageTemplatesRawAsync(Guid workspaceRef, Guid? policyRefId = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/message-templates{Query(("policyRefId", policyRefId), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Creates a message template. <c>POST /api/workspaces/{workspaceRef}/message-templates</c>.</summary>
    public Task<MessageTemplateResponse> CreateMessageTemplateAsync(Guid workspaceRef, MessageTemplateRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<MessageTemplateResponse>(CreateMessageTemplateRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateMessageTemplateAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateMessageTemplateRawAsync(Guid workspaceRef, MessageTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/message-templates", request, cancellationToken);

    /// <summary>Replaces a message template. <c>PUT /api/workspaces/{workspaceRef}/message-templates/{templateRef}</c>.</summary>
    public Task UpdateMessageTemplateAsync(Guid workspaceRef, Guid templateRef, MessageTemplateRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(UpdateMessageTemplateRawAsync(workspaceRef, templateRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateMessageTemplateAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateMessageTemplateRawAsync(Guid workspaceRef, Guid templateRef, MessageTemplateRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/workspaces/{workspaceRef}/message-templates/{templateRef}", request, cancellationToken);

    /// <summary>Deletes a message template. <c>DELETE /api/workspaces/{workspaceRef}/message-templates/{templateRef}</c>.</summary>
    public Task DeleteMessageTemplateAsync(Guid workspaceRef, Guid templateRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteMessageTemplateRawAsync(workspaceRef, templateRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteMessageTemplateAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteMessageTemplateRawAsync(Guid workspaceRef, Guid templateRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}/message-templates/{templateRef}", null, cancellationToken);

    // ── Event log ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A page of a workspace's event log, newest first, narrowed by <paramref name="search"/>. <c>GET /api/workspaces/{workspaceRef}/event-logs</c>.</summary>
    public Task<Page<EventLogResponse>> GetEventLogsAsync(Guid workspaceRef, EventLogSearch? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<EventLogResponse>>(GetEventLogsRawAsync(workspaceRef, search, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetEventLogsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetEventLogsRawAsync(Guid workspaceRef, EventLogSearch? search = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/event-logs{SearchQuery(search, ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>How many entries a window of the event log holds, by group, event, person and source. <c>GET /api/workspaces/{workspaceRef}/event-logs/summary</c>.</summary>
    public Task<EventLogSummaryResponse> GetEventLogSummaryAsync(Guid workspaceRef, DateTimeOffset? from = null, DateTimeOffset? to = null, bool excludeDeviceTraffic = false, CancellationToken cancellationToken = default) =>
        ReceiveAsync<EventLogSummaryResponse>(GetEventLogSummaryRawAsync(workspaceRef, from, to, excludeDeviceTraffic, cancellationToken), cancellationToken);

    /// <summary><see cref="GetEventLogSummaryAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetEventLogSummaryRawAsync(Guid workspaceRef, DateTimeOffset? from = null, DateTimeOffset? to = null, bool excludeDeviceTraffic = false, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/event-logs/summary{Query(("from", from), ("to", to), ("traffic", excludeDeviceTraffic ? "exclude" : null))}", null, cancellationToken);

    /// <summary>The event log narrowed by <paramref name="search"/>, as CSV. <c>GET /api/workspaces/{workspaceRef}/event-logs/csv</c>.</summary>
    public Task<string> GetEventLogsCsvAsync(Guid workspaceRef, EventLogSearch? search = null, CancellationToken cancellationToken = default) =>
        ReceiveTextAsync(GetEventLogsCsvRawAsync(workspaceRef, search, cancellationToken), cancellationToken);

    /// <summary><see cref="GetEventLogsCsvAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetEventLogsCsvRawAsync(Guid workspaceRef, EventLogSearch? search = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/event-logs/csv{SearchQuery(search)}", null, cancellationToken);

    // ── Integrations ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A workspace's integrations, optionally of one type. <c>GET /api/workspaces/{workspaceRef}/integrations</c>.</summary>
    public Task<Page<IntegrationResponse>> GetIntegrationsAsync(Guid workspaceRef, string? type = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<IntegrationResponse>>(GetIntegrationsRawAsync(workspaceRef, type, cancellationToken), cancellationToken);

    /// <summary><see cref="GetIntegrationsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetIntegrationsRawAsync(Guid workspaceRef, string? type = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/integrations{Query(("type", type))}", null, cancellationToken);

    /// <summary>One integration. Never carries its secret. <c>GET /api/workspaces/{workspaceRef}/integrations/{integrationRef}</c>.</summary>
    public Task<IntegrationResponse> GetIntegrationAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IntegrationResponse>(GetIntegrationRawAsync(workspaceRef, integrationRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetIntegrationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetIntegrationRawAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/integrations/{integrationRef}", null, cancellationToken);

    /// <summary>Adds an integration. <c>POST /api/workspaces/{workspaceRef}/integrations</c>.</summary>
    public Task<IntegrationResponse> CreateIntegrationAsync(Guid workspaceRef, IntegrationCreateRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IntegrationResponse>(CreateIntegrationRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CreateIntegrationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CreateIntegrationRawAsync(Guid workspaceRef, IntegrationCreateRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/integrations", request, cancellationToken);

    /// <summary>Changes an integration; secrets are merged over the saved credential. <c>PATCH /api/workspaces/{workspaceRef}/integrations/{integrationRef}</c>.</summary>
    public Task<IntegrationResponse> UpdateIntegrationAsync(Guid workspaceRef, Guid integrationRef, IntegrationUpdateRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IntegrationResponse>(UpdateIntegrationRawAsync(workspaceRef, integrationRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="UpdateIntegrationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> UpdateIntegrationRawAsync(Guid workspaceRef, Guid integrationRef, IntegrationUpdateRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Patch, $"/api/workspaces/{workspaceRef}/integrations/{integrationRef}", request, cancellationToken);

    /// <summary>Deletes an integration no published workflow sends through. <c>DELETE /api/workspaces/{workspaceRef}/integrations/{integrationRef}</c>.</summary>
    public Task DeleteIntegrationAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteIntegrationRawAsync(workspaceRef, integrationRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteIntegrationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteIntegrationRawAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}/integrations/{integrationRef}", null, cancellationToken);

    /// <summary>The workflow steps that send through an integration. <c>GET /api/workspaces/{workspaceRef}/integrations/{integrationRef}/usage</c>.</summary>
    public Task<IntegrationUsageResponse> GetIntegrationUsageAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IntegrationUsageResponse>(GetIntegrationUsageRawAsync(workspaceRef, integrationRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetIntegrationUsageAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetIntegrationUsageRawAsync(Guid workspaceRef, Guid integrationRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/integrations/{integrationRef}/usage", null, cancellationToken);

    /// <summary>Tries an integration without saving anything. A failed test is a 200 with <c>Ok</c> false. <c>POST /api/workspaces/{workspaceRef}/integrations/test</c>.</summary>
    public Task<IntegrationTestResponse> TestIntegrationAsync(Guid workspaceRef, IntegrationTestRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<IntegrationTestResponse>(TestIntegrationRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="TestIntegrationAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> TestIntegrationRawAsync(Guid workspaceRef, IntegrationTestRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/integrations/test", request, cancellationToken);

    // ── Workflows ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Publishes a workflow definition as a new version. <c>POST /api/workspaces/{workspaceRef}/workflows</c>.</summary>
    public Task<WorkflowPublishResponse> PublishWorkflowAsync(Guid workspaceRef, WorkflowPublishRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowPublishResponse>(PublishWorkflowRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="PublishWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> PublishWorkflowRawAsync(Guid workspaceRef, WorkflowPublishRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows", request, cancellationToken);

    /// <summary>Validates a workflow definition without publishing it. <c>POST /api/workspaces/{workspaceRef}/workflows/validate</c>.</summary>
    public Task<WorkflowValidationResponse> ValidateWorkflowAsync(Guid workspaceRef, WorkflowPublishRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowValidationResponse>(ValidateWorkflowRawAsync(workspaceRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="ValidateWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ValidateWorkflowRawAsync(Guid workspaceRef, WorkflowPublishRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows/validate", request, cancellationToken);

    /// <summary>A page of a workspace's workflows. <c>GET /api/workspaces/{workspaceRef}/workflows</c>.</summary>
    public Task<Page<WorkflowSummaryDto>> GetWorkflowsAsync(Guid workspaceRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<WorkflowSummaryDto>>(GetWorkflowsRawAsync(workspaceRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowsRawAsync(Guid workspaceRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>A workflow's current version. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}</c>.</summary>
    public Task<WorkflowDefinitionDto> GetWorkflowAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowDefinitionDto>(GetWorkflowRawAsync(workspaceRef, workflowRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowRawAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}", null, cancellationToken);

    /// <summary>Every version of a workflow. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}/versions</c>.</summary>
    public Task<Page<WorkflowVersionDto>> GetWorkflowVersionsAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<Page<WorkflowVersionDto>>(GetWorkflowVersionsRawAsync(workspaceRef, workflowRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowVersionsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowVersionsRawAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/versions", null, cancellationToken);

    /// <summary>One version of a workflow. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}/versions/{version}</c>.</summary>
    public Task<WorkflowDefinitionDto> GetWorkflowVersionAsync(Guid workspaceRef, Guid workflowRef, int version, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowDefinitionDto>(GetWorkflowVersionRawAsync(workspaceRef, workflowRef, version, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowVersionAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowVersionRawAsync(Guid workspaceRef, Guid workflowRef, int version, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/versions/{version}", null, cancellationToken);

    /// <summary>Deprecates a workflow: its triggers stop starting runs. <c>POST /api/workspaces/{workspaceRef}/workflows/{workflowRef}/deprecate</c>.</summary>
    public Task DeprecateWorkflowAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeprecateWorkflowRawAsync(workspaceRef, workflowRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeprecateWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeprecateWorkflowRawAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/deprecate", null, cancellationToken);

    /// <summary>Deletes a workflow. <c>DELETE /api/workspaces/{workspaceRef}/workflows/{workflowRef}</c>.</summary>
    public Task DeleteWorkflowAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(DeleteWorkflowRawAsync(workspaceRef, workflowRef, cancellationToken), cancellationToken);

    /// <summary><see cref="DeleteWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> DeleteWorkflowRawAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Delete, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}", null, cancellationToken);

    /// <summary>Reinstates a deprecated workflow. <c>POST /api/workspaces/{workspaceRef}/workflows/{workflowRef}/reinstate</c>.</summary>
    public Task ReinstateWorkflowAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync(ReinstateWorkflowRawAsync(workspaceRef, workflowRef, cancellationToken), cancellationToken);

    /// <summary><see cref="ReinstateWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> ReinstateWorkflowRawAsync(Guid workspaceRef, Guid workflowRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/reinstate", null, cancellationToken);

    /// <summary>Publishes an earlier version of a workflow again as its newest. <c>POST /api/workspaces/{workspaceRef}/workflows/{workflowRef}/rollback/{version}</c>.</summary>
    public Task<WorkflowPublishResponse> RollbackWorkflowAsync(Guid workspaceRef, Guid workflowRef, int version, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowPublishResponse>(RollbackWorkflowRawAsync(workspaceRef, workflowRef, version, cancellationToken), cancellationToken);

    /// <summary><see cref="RollbackWorkflowAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> RollbackWorkflowRawAsync(Guid workspaceRef, Guid workflowRef, int version, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/rollback/{version}", null, cancellationToken);

    /// <summary>Starts a manual run. Re-posting the same idempotency key is deduplicated. <c>POST /api/workspaces/{workspaceRef}/workflows/{workflowRef}/runs</c>.</summary>
    public Task<StartRunResponse> StartRunAsync(Guid workspaceRef, Guid workflowRef, StartRunRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<StartRunResponse>(StartRunRawAsync(workspaceRef, workflowRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="StartRunAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> StartRunRawAsync(Guid workspaceRef, Guid workflowRef, StartRunRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/runs", request, cancellationToken);

    /// <summary>One of a workflow's shared variables. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}/variables/{variableName}</c>.</summary>
    public Task<SharedVariableDto> GetWorkflowVariableAsync(Guid workspaceRef, Guid workflowRef, string variableName, CancellationToken cancellationToken = default) =>
        ReceiveAsync<SharedVariableDto>(GetWorkflowVariableRawAsync(workspaceRef, workflowRef, variableName, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowVariableAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowVariableRawAsync(Guid workspaceRef, Guid workflowRef, string variableName, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/variables/{E(variableName)}", null, cancellationToken);

    /// <summary>Sets one of a workflow's shared variables. <c>PUT /api/workspaces/{workspaceRef}/workflows/{workflowRef}/variables/{variableName}</c>.</summary>
    public Task<SharedVariableDto> SetWorkflowVariableAsync(Guid workspaceRef, Guid workflowRef, string variableName, SharedVariableSetRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<SharedVariableDto>(SetWorkflowVariableRawAsync(workspaceRef, workflowRef, variableName, request, cancellationToken), cancellationToken);

    /// <summary><see cref="SetWorkflowVariableAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SetWorkflowVariableRawAsync(Guid workspaceRef, Guid workflowRef, string variableName, SharedVariableSetRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/variables/{E(variableName)}", request, cancellationToken);

    // ── Runs ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A page of a workflow's runs, newest first. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}/runs</c>.</summary>
    public Task<RunListResponse> GetRunsAsync(Guid workspaceRef, Guid workflowRef, string? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RunListResponse>(GetRunsRawAsync(workspaceRef, workflowRef, status, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetRunsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetRunsRawAsync(Guid workspaceRef, Guid workflowRef, string? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/runs{Query(("status", status), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>A page of every run in a workspace, newest first. <c>GET /api/workspaces/{workspaceRef}/runs</c>.</summary>
    public Task<RunListResponse> GetWorkspaceRunsAsync(Guid workspaceRef, string? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RunListResponse>(GetWorkspaceRunsRawAsync(workspaceRef, status, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkspaceRunsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkspaceRunsRawAsync(Guid workspaceRef, string? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/runs{Query(("status", status), ("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    /// <summary>Run counts and durations across a workspace over a time range. <c>GET /api/workspaces/{workspaceRef}/stats</c>.</summary>
    public Task<WorkspaceStatsResponse> GetWorkspaceStatsAsync(Guid workspaceRef, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkspaceStatsResponse>(GetWorkspaceStatsRawAsync(workspaceRef, from, to, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkspaceStatsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkspaceStatsRawAsync(Guid workspaceRef, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/stats{Query(("from", from), ("to", to))}", null, cancellationToken);

    /// <summary>Run counts and durations of one workflow over a time range. <c>GET /api/workspaces/{workspaceRef}/workflows/{workflowRef}/stats</c>.</summary>
    public Task<WorkflowStatsResponse> GetWorkflowStatsAsync(Guid workspaceRef, Guid workflowRef, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<WorkflowStatsResponse>(GetWorkflowStatsRawAsync(workspaceRef, workflowRef, from, to, cancellationToken), cancellationToken);

    /// <summary><see cref="GetWorkflowStatsAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetWorkflowStatsRawAsync(Guid workspaceRef, Guid workflowRef, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/workflows/{workflowRef}/stats{Query(("from", from), ("to", to))}", null, cancellationToken);

    /// <summary>A run with every branch and its state. <c>GET /api/workspaces/{workspaceRef}/runs/{runRef}</c>.</summary>
    public Task<RunDetailDto> GetRunAsync(Guid workspaceRef, Guid runRef, CancellationToken cancellationToken = default) =>
        ReceiveAsync<RunDetailDto>(GetRunRawAsync(workspaceRef, runRef, cancellationToken), cancellationToken);

    /// <summary><see cref="GetRunAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetRunRawAsync(Guid workspaceRef, Guid runRef, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/runs/{runRef}", null, cancellationToken);

    /// <summary>Asks for a run to be cancelled. Answers 202; the engine carries the cancel out afterwards. <c>POST /api/workspaces/{workspaceRef}/runs/{runRef}/cancel</c>.</summary>
    public Task CancelRunAsync(Guid workspaceRef, Guid runRef, CancelRunRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync(CancelRunRawAsync(workspaceRef, runRef, request, cancellationToken), cancellationToken);

    /// <summary><see cref="CancelRunAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> CancelRunRawAsync(Guid workspaceRef, Guid runRef, CancelRunRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/runs/{runRef}/cancel", request, cancellationToken);

    /// <summary>Sends a named signal to a run, resuming a branch parked on it. <c>POST /api/workspaces/{workspaceRef}/runs/{runRef}/signals/{signalName}</c>.</summary>
    public Task<SignalResponse> SignalRunAsync(Guid workspaceRef, Guid runRef, string signalName, SignalRequest request, CancellationToken cancellationToken = default) =>
        ReceiveAsync<SignalResponse>(SignalRunRawAsync(workspaceRef, runRef, signalName, request, cancellationToken), cancellationToken);

    /// <summary><see cref="SignalRunAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SignalRunRawAsync(Guid workspaceRef, Guid runRef, string signalName, SignalRequest request, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/workspaces/{workspaceRef}/runs/{runRef}/signals/{E(signalName)}", request, cancellationToken);

    /// <summary>A page of a run's history events, oldest first. <c>GET /api/workspaces/{workspaceRef}/runs/{runRef}/history</c>.</summary>
    public Task<HistoryListResponse> GetRunHistoryAsync(Guid workspaceRef, Guid runRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync<HistoryListResponse>(GetRunHistoryRawAsync(workspaceRef, runRef, cursor, limit, cancellationToken), cancellationToken);

    /// <summary><see cref="GetRunHistoryAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> GetRunHistoryRawAsync(Guid workspaceRef, Guid runRef, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Get, $"/api/workspaces/{workspaceRef}/runs/{runRef}/history{Query(("cursor", cursor), ("limit", limit))}", null, cancellationToken);

    // ── Public callbacks ──────────────────────────────────────────────────────────────────────────

    /// <summary>Resumes a branch parked on a WaitForHttp step. Anonymous and deliberately opaque: it answers 202 whether or not anything matched. <c>POST /api/callbacks/wake/{token}</c>.</summary>
    public Task SendHttpWakeAsync(string token, object? payload = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync(SendHttpWakeRawAsync(token, payload, cancellationToken), cancellationToken);

    /// <summary><see cref="SendHttpWakeAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SendHttpWakeRawAsync(string token, object? payload = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/callbacks/wake/{E(token)}", payload ?? EmptyBody, cancellationToken);

    /// <summary>Fires a workspace's webhook trigger. Anonymous and deliberately opaque: it answers 202 whether or not a trigger matched. <c>POST /api/callbacks/webhook/{workspaceRef}/{path}</c>.</summary>
    public Task SendWebhookAsync(Guid workspaceRef, string path, object? payload = null, CancellationToken cancellationToken = default) =>
        ReceiveAsync(SendWebhookRawAsync(workspaceRef, path, payload, cancellationToken), cancellationToken);

    /// <summary><see cref="SendWebhookAsync"/>, returning the response untouched for a caller that asserts on its status or error.</summary>
    public Task<HttpResponseMessage> SendWebhookRawAsync(Guid workspaceRef, string path, object? payload = null, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, $"/api/callbacks/webhook/{workspaceRef}/{E(path)}", payload ?? EmptyBody, cancellationToken);
}
