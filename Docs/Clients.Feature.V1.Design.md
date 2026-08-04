# Clients Feature V1 — API Gap Analysis & Design

## §1 Context & goals

The console UI mock for the **Clients** page (see `Docs/RoughFeatures.md`, `Docs/UI.Mockup.Prompt.md`) needs:

- **Explorer**: clients grouped by policy, per-client online dot, latency (`42ms`), last-seen (`3d`/`12h`), policy badges with client counts, filter box.
- **Client detail**: client id, ONLINE badge + RTT, uptime (`6d 14h`), policy chip, SDK name+version (`C# SDK v1.4`), platform (`linux-arm64`); actions **Send / Ping / Rename / Revoke**.
- **Send panel**: message type ("topic") + JSON editor + template picker (`Template: set-vent`).
- **Live comms**: real-time in/out stream (telemetry, commands, acks, ping/pong) with backfill, In/Out filter, tail.
- **Capabilities** chips (`telemetry.push`, `cmd.actuate`, …).
- **State variables** table (name, type, value, freshness).
- **Properties changed** history (`ventPosition 0 → 75`, `firmware 2.4.0 → 2.4.1`).

A codebase review (2026-07-20) found the platform covers roughly half of this. This doc records the gap analysis and the locked design to close it, end-to-end (SDK + Socket.Host + DB + Management API + events).

**Decisions (locked):**
1. Scope: full end-to-end, phased (§13).
2. Templates: BOTH capability-derived forms AND a saved-payload `MessageTemplates` CRUD (§10).
3. Property-change history rides the existing `EventLogs` pipeline — no dedicated history table.
4. RTT is measured in `Socket.Host` (socket round-trip only), persisted by `Management.Host`.

---

## §2 Current state & gaps

### 2.1 What already works

| Capability | Where |
|---|---|
| List clients / by policy (status+name filters, paged) | `ClientsController` `GET api/workspaces/{workspaceRef}/clients` and `GET …/clients/policy/{policyRefId}` |
| Approve / revoke / reject / revive | `PATCH …/clients/{clientRefId}/status` (`ClientStatus: Pending=0, Registered=1, Revoked=2, Rejected=3`) |
| Send command | `POST …/clients/{clientRefId}/command` → `ClientCommandEvent` → `Socket.Host/Handlers/ClientPayloadHandler` → socket; `ClientCommandDeliveredEvent`/`ClientCommandFailedEvent` |
| Presence | `Management.Host/Handlers/ClientPresenceHandler` ← connect/disconnect events → `Client_UpdatePresence` (`IsConnected`, `LastActivityAt`) |
| Browser realtime | `Hubs/NotificationHub` at `/hubs/notifications`; every `[SignalRNotify]` event auto-forwarded to group `ws:{WorkspaceId}` (`SignalRForwardingHandler`) |
| Durable history | `EventLogs` table ← `EventLoggerHandler` (every `IEvent`, batched); `GET …/event-logs?clientRefId=…` |
| Registration + client auth | `POST api/client-registrations/initiate` (PIN), `POST api/client-auth/login` (1 h JWT), `Socket.Host` `/ws` (`WebSocketAuthMiddleware`, `type == client` claim) |

### 2.2 Gaps (mock element → status)

| Mock element | Status today |
|---|---|
| Client detail page | **No `GET clients/{clientRefId}` endpoint exists** |
| Latency `42ms` | Dead loop: `ClientPingEvent` has **no consumer**; SDK's `sys.pong` arrives as a generic `ClientMessageReceivedEvent` and is never turned into `ClientPongEvent`; `ClientPongHandler` is orphaned; RTT never persisted. SDK carries a `TODO: this ping-pong system does not work` |
| Uptime `6d 14h` | No `ConnectedAt` column; `LastActivityAt` is overwritten on connect **and** disconnect |
| SDK + platform + capabilities | SDK sends `ClientCapabilities(Agent, Version, OS, List<CommandCapability>)` as a `"capabilities"` message, but **no server component parses or persists it**; SDK doesn't auto-detect OS/version, and apps must call `UpdateCapabilitiesAsync` manually |
| State variables / properties changed | Nothing exists. `ClientPropertyUpdatedEvent` is defined but has **no producer** and no `OldValue` field |
| Rename | No endpoint, no SP |
| Revoke (hardening) | Endpoint exists, but the live socket stays open and the still-valid JWT (≤1 h) lets the SDK auto-reconnect; `Socket.Host` ignores `ClientStatusChangedEvent` |
| Ack rows in live comms | `SocketMessage.CommandId` exists on the wire model but is never sent (`ClientPayloadHandler` TODO); no ack round-trip |
| Template picker | Nothing exists |
| Policy badges (`greenhouse-sensors 3`) | `RegistrationPolicyResponse` has no client counts |
| `retry` state in explorer | Not representable server-side — reconnect/backoff state lives inside the SDK (§11) |

---

## §3 Data model changes (`Databases/Wbskt.Database`)

### 3.1 `Tables/Clients.sql` — new columns

```sql
ConnectedAt   DATETIME2(3) NULL,  -- set on connect, NULL on disconnect; uptime = now - ConnectedAt
LastRttMs     INT          NULL,  -- last measured socket round-trip
RttMeasuredAt DATETIME2(3) NULL
```

### 3.2 New `Tables/ClientCapabilities.sql` (1:1 sidecar)

Keeps the `NVARCHAR(MAX)` blob off the hot `Clients` row that list queries scan.

```sql
CREATE TABLE dbo.ClientCapabilities (
    ClientId         INT           NOT NULL,
    AgentName        NVARCHAR(100) NOT NULL,   -- ClientCapabilities.Agent  ("csharp-sdk")
    AgentVersion     NVARCHAR(50)  NOT NULL,   -- ClientCapabilities.Version
    Platform         NVARCHAR(100) NOT NULL,   -- ClientCapabilities.OS     ("linux-arm64")
    CapabilitiesJson NVARCHAR(MAX) NOT NULL,   -- serialized List<CommandCapability>
    UpdatedAt        DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ClientCapabilities PRIMARY KEY (ClientId),
    CONSTRAINT FK_ClientCapabilities_Clients FOREIGN KEY (ClientId) REFERENCES dbo.Clients (Id)
);
```

### 3.3 New `Tables/ClientStateVariables.sql`

```sql
CREATE TABLE dbo.ClientStateVariables (
    Id        INT           IDENTITY(1,1) NOT NULL,
    ClientId  INT           NOT NULL,
    Name      NVARCHAR(100) NOT NULL,
    DataType  NVARCHAR(20)  NOT NULL,   -- inferred from JSON value kind: number | string | boolean | object
    ValueJson NVARCHAR(MAX) NOT NULL,
    UpdatedAt DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ClientStateVariables PRIMARY KEY (Id),
    CONSTRAINT UQ_ClientStateVariables_Client_Name UNIQUE (ClientId, Name),
    CONSTRAINT FK_ClientStateVariables_Clients FOREIGN KEY (ClientId) REFERENCES dbo.Clients (Id)
);
```

### 3.4 New `Tables/MessageTemplates.sql`

```sql
CREATE TABLE dbo.MessageTemplates (
    Id          INT              IDENTITY(1,1) NOT NULL,
    RefId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    WorkspaceId INT              NOT NULL,
    PolicyId    INT              NULL,          -- optional: scope a template to one policy's clients
    Name        NVARCHAR(100)    NOT NULL,      -- "set-vent"
    MessageType NVARCHAR(100)    NOT NULL,      -- the SocketMessage type / "topic", e.g. "cmd/actuate"
    PayloadJson NVARCHAR(MAX)    NOT NULL,
    CreatedAt   DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_MessageTemplates PRIMARY KEY (Id),
    CONSTRAINT UQ_MessageTemplates_RefId UNIQUE (RefId),
    CONSTRAINT FK_MessageTemplates_RegistrationPolicies FOREIGN KEY (PolicyId) REFERENCES dbo.RegistrationPolicies (Id)
);
CREATE INDEX IX_MessageTemplates_WorkspaceId ON dbo.MessageTemplates (WorkspaceId);
```

### 3.5 Stored procedures (convention `Table_Action.sql`)

| SP | Notes |
|---|---|
| `Client_UpdatePresence` (extend) | `IF @IsConnected = 1 SET ConnectedAt = @LastActivityAt ELSE SET ConnectedAt = NULL` — same params, both paths still set `LastActivityAt` |
| `Client_UpdateName` (new) | `UPDATE … SET Name = @Name WHERE Id = @Id AND WorkspaceId = @WorkspaceId`; SELECT old name first (for the event) |
| `Client_UpdateRtt` (new) | sets `LastRttMs`, `RttMeasuredAt` |
| `Client_ResetAllPresence` (new) | `UPDATE dbo.Clients SET IsConnected = 0, ConnectedAt = NULL WHERE IsConnected = 1` — see §12 stale-presence note |
| `ClientCapabilities_Upsert` / `ClientCapabilities_GetBy_ClientId` (new) | MERGE-free upsert (`UPDATE`; if `@@ROWCOUNT = 0` `INSERT`) |
| `ClientStateVariable_Upsert` (new) | upsert one `(ClientId, Name)`; `OUTPUT`/SELECT the **previous** `ValueJson` so the caller can emit `OldValue` and skip no-op updates |
| `ClientStateVariable_GetBy_ClientId` (new) | ordered by `Name` |
| `MessageTemplate_Create / Update / Delete / GetAll / GetBy_RefId` (new) | `GetAll` filters `@WorkspaceId`, optional `@PolicyId`, paged with `@TotalCount OUTPUT` like `RegistrationPolicy_GetAll` |
| `RegistrationPolicy_GetAll` / `RegistrationPolicy_GetBy_RefId` (extend) | add subquery columns: `RegisteredClientCount` (`Status = 1`), `ConnectedClientCount` (`IsConnected = 1`) |
| `Client_GetAll` / `Client_GetBy_PolicyId` / `Client_GetBy_RefId` (extend) | return the new `Clients` columns; `Client_GetBy_RefId` additionally LEFT JOINs `ClientCapabilities` + `RegistrationPolicies.Name` for the detail endpoint |

---

## §4 Management API (`Hosts/Wbskt.Management.Host`)

All routes stay under `api/workspaces/{workspaceRef:guid}/…` with the existing `IAuthServiceClient.ResolveWorkspaceAsync(workspaceRef, permission)` gate.

### 4.1 `GET …/clients/{clientRefId:guid}` — **new** (detail page)

Permission `clients.read`. Verifies the client belongs to the resolved workspace (mirror `UpdateStatusAsync`'s ownership check).

```csharp
public record ClientDetailResponse(
    Guid ClientRefId,
    Guid PolicyRefId,
    string PolicyName,
    string Name,
    ClientStatus Status,
    bool IsConnected,
    DateTime? ConnectedAt,          // uptime = now - ConnectedAt while connected
    DateTime? LastActivityAt,
    int? LastRttMs,
    DateTime? RttMeasuredAt,
    string? AgentName,              // null until the client reports capabilities
    string? AgentVersion,
    string? Platform,
    IReadOnlyList<CommandCapability>? Capabilities,   // deserialized CapabilitiesJson
    DateTime CreatedAt
);
```

`CommandCapability`/`PropertySchema` records are duplicated today in `Wbskt.Client.Sdk`; move the shared shape into `Wbskt.Models` (or `Wbskt.Primitives/Models`) so the host doesn't reference the SDK package.

### 4.2 `ClientResponse` enrichment (explorer)

Add `DateTime? ConnectedAt` and `int? LastRttMs` to `ClientResponse` (`Models/ManagementModels.cs`) — the explorer needs latency + uptime without N detail calls.

### 4.3 `PATCH …/clients/{clientRefId:guid}/name` — **new** (Rename)

Permission `clients.update`; mirrors the `/status` route convention.

```csharp
public record UpdateClientNameRequest(string Name);   // [Required, MaxLength(100)]
```

Service: ownership check → `Client_UpdateName` → publish `ClientRenamedEvent` (§7.1) → 204.

### 4.4 `GET …/clients/{clientRefId:guid}/state` — **new** (state variables)

Permission `clients.read`.

```csharp
public record ClientStateVariableResponse(string Name, string DataType, string Value, DateTime UpdatedAt);
```

Returned as `ListResponse<ClientStateVariableResponse>`. Freshness ("4s ago") is computed client-side from `UpdatedAt`.

### 4.5 `GET …/clients/{clientRefId:guid}/comms` — **new** (Live Comms backfill)

Permission `logs.read`. Wraps the `EventLogs` query pinned to the comms event set so the UI has one stable contract for the initial page fill before the SignalR tail attaches:

- **In**: `ClientMessageReceivedEvent`, `ClientPongEvent`, `ClientCommandAckedEvent`
- **Out**: `ClientCommandEvent`, `ClientCommandDeliveredEvent`, `ClientCommandFailedEvent`, `ClientPingEvent`
- **Lifecycle**: `ClientConnectedEvent`, `ClientDisconnectedEvent`

Query params: `direction=in|out|all` (default `all`), `skip`, `take`. Response: `ListResponse<EventLogResponse>` (existing shape). Implementation: new SP `EventLog_GetCommsBy_Client` (a variant of `EventLog_GetBy_Workspace` with an `EventName IN (…)` set + `ClientId` filter) — chosen over adding multi-`eventName` filtering to `EventLogsController` to keep the UI contract stable.

### 4.6 Message templates — **new controller** (§10)

`[Route("api/workspaces/{workspaceRef:guid}/message-templates")]`

| Verb / route | Permission | Request → Response |
|---|---|---|
| `GET` (`policyRefId?`, `skip`, `take`) | `templates.read` | → `ListResponse<MessageTemplateResponse>` |
| `POST` | `templates.manage` | `MessageTemplateRequest` → `MessageTemplateResponse` |
| `PUT {refId:guid}` | `templates.manage` | `MessageTemplateRequest` → 204 |
| `DELETE {refId:guid}` | `templates.manage` | → 204 |

```csharp
public record MessageTemplateRequest(string Name, string MessageType, string PayloadJson, Guid? PolicyRefId);
public record MessageTemplateResponse(Guid RefId, string Name, string MessageType, string PayloadJson, Guid? PolicyRefId, DateTime CreatedAt, DateTime UpdatedAt);
```

> **Revised.** This originally reused `clients.read`/`clients.manage` on the reasoning that templates
> are send-payload tooling. That put template editing behind a slug named "Manage clients", which
> read as a much broader grant than it was and would have widened silently the moment
> `clients.manage` gated anything to do with clients themselves. Templates now have their own
> `templates.read`/`templates.manage` pair.

### 4.7 `RegistrationPolicyResponse` enrichment

Add `int RegisteredClientCount` and `int ConnectedClientCount` (explorer badges, policy-page "remaining slots" = `MaxClients - RegisteredClientCount`).

### 4.8 Ping endpoint — unchanged

`POST …/clients/{clientRefId}/ping` already publishes `ClientPingEvent`; it starts *working* once §5 lands. The response stays 204 — the RTT arrives asynchronously via SignalR `OnClientLatencyMeasuredEvent` and is persisted to `LastRttMs` for subsequent reads.

---

## §5 Ping / RTT loop (fix end-to-end)

Principle: **`Socket.Host` measures** (pure socket round-trip, no bus hops in the number), **`Management.Host` persists**.

1. **`Socket.Host` — new `ClientPingHandler : IConsumer<ClientPingEvent>`**: look up the connection; if open, send `SocketMessage("sys.ping", new { timestamp = DateTime.UtcNow })` (the key `timestamp` matches what `SocketClient.ReceiveLoopAsync` already parses) and record the send time in per-connection metadata. If offline → log debug, drop (same policy as `ClientPayloadHandler`).
2. **`ConnectionManager`**: store a `ClientConnection` record per refId instead of a bare `WebSocket`:
   ```csharp
   internal sealed class ClientConnection
   {
       public required WebSocket Socket { get; init; }
       public required int ClientId { get; init; }
       public required int WorkspaceId { get; init; }
       public DateTime? LastPingSentAt { get; set; }   // for RTT
   }
   ```
3. **`SocketHandler.ReceiveLoopAsync`**: special-case reserved `sys.*` types **before** the generic publish; `sys.*` frames are **not** published as `ClientMessageReceivedEvent` (keeps workflow-trigger routing keys `client:{refId}:{type}` noise-free):
   - `"sys.pong"` → `rtt = (now - LastPingSentAt).TotalMilliseconds`; publish `ClientPongEvent(…, OriginalPingTime)` (live-comms row) **and** `ClientLatencyMeasuredEvent(…, rtt)`.
   - `"sys.ack"` → §8.
4. **`Management.Host`**: `ClientPongHandler` currently computes RTT from `ClientPongEvent` using wall-clock-minus-bus-latency — repurpose it to consume **`ClientLatencyMeasuredEvent`** and call `Client_UpdateRtt`. SignalR forwarding of both events is automatic.
5. **`PingSampler`** (new `BackgroundService` in `Socket.Host`, P1): every `PingIntervalSeconds` (config, default 60) iterate `ConnectionManager.GetConnectedClients()` and send `sys.ping` directly (no bus round-trip) — keeps explorer latency fresh without any UI action. The Ping button stays for on-demand checks.

SDK side: `SocketClient` already replies `sys.pong { originalTimestamp }` — contract verified, no change needed for the loop itself.

---

## §6 Capabilities & state ingestion

`Socket.Host` stays a dumb pipe; ingestion lives in `Management.Host`, which already owns the DB providers. MassTransit is pub/sub — the workflow engine keeps receiving the same messages as triggers (`client:{refId}:state.report`); no conflict.

**New `ClientMetadataIngestionHandler : IConsumer<ClientMessageReceivedEvent>`** (beside `ClientPresenceHandler`), switching on `Type`:

- **`"capabilities"`** → deserialize payload as `ClientCapabilities`; validate (§12 size limits); `ClientCapabilities_Upsert`; publish:
  ```csharp
  [EventCriticality(EventCriticality.Info)]
  [SignalRNotify("OnClientCapabilitiesUpdatedEvent")]
  public sealed record ClientCapabilitiesUpdatedEvent(Guid ClientRefId, int ClientId, int WorkspaceId,
      string AgentName, string AgentVersion, string Platform) : BaseEvent, IClientContext;
  ```
- **`"state.report"`** → payload is a flat JSON object patch `{ "temperature": 36.2, "ventPosition": 75 }`. For each property: infer `DataType` from the JSON value kind; `ClientStateVariable_Upsert`; if the value actually changed, publish the (amended) property event:
  ```csharp
  public sealed record ClientPropertyUpdatedEvent(Guid ClientRefId, int ClientId, int WorkspaceId,
      string PropertyName, string? OldValue, string NewValue) : BaseEvent, IClientContext;   // OldValue added
  ```
  The mock's **Properties Changed** panel = `EventLogs` filtered by `eventName = ClientPropertyUpdatedEvent` + `clientRefId` (the existing `GET …/event-logs` already supports both filters); realtime updates via `OnClientPropertyUpdatedEvent`. No new history table.
- **Other types** → ignore (telemetry etc. remain workflow-engine + event-log concerns).

Reserved message types after this design: `sys.ping`, `sys.pong`, `sys.ack`, `capabilities`, `state.report` — document in the SDK README; apps should not use them for their own payloads.

---

## §7 New / amended events (`Events/Wbskt.Events/Client`)

| Event | Change |
|---|---|
| `ClientRenamedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string OldName, string NewName)` | **new**, Info, `[SignalRNotify("OnClientRenamedEvent")]` |
| `ClientCapabilitiesUpdatedEvent` | **new**, §6 |
| `ClientPropertyUpdatedEvent` | **amended**: add `string? OldValue` (no producer exists yet, so not a breaking change) |
| `ClientCommandAckedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, Guid CommandId)` | **new**, Info, `[SignalRNotify("OnClientCommandAckedEvent")]` (§8) |
| `ClientCommandEvent` / `ClientCommandDeliveredEvent` | **amended**: add `Guid? CommandId = null` (nullable default → old/new hosts can roll independently) |

---

## §8 Command acks (P2)

1. **Mint** `CommandId = Guid.NewGuid()` at the two publish sites: `ClientsController.SendCommand` and `Wbskt.Workflow.Engine.Host/InboundAdapters/DeviceCommandPublisher`. Return it in the API response (`202`/`200 { commandId }`) so the console can correlate the eventual ack.
2. **`ClientPayloadHandler`**: pass it through — `new SocketMessage(command.Type, command.Payload, command.CommandId?.ToString())` (resolves the existing `[RJ]` TODO).
3. **SDK**: on receiving a message with a `commandId`, auto-send `sys.ack { commandId }` after the frame is parsed (delivery ack); expose the id to apps via a new overload `OnMessageReceived(string type, object? payload, string? commandId)`.
4. **`Socket.Host`**: `sys.ack` → publish `ClientCommandAckedEvent` (special-cased in `ReceiveLoopAsync`, §5.3).
5. **App-level acks** (the mock's `ack {"command":"open_vent","status":"accepted"}`) stay ordinary client messages sent with `SendAsync("ack", …)` — they already flow through `ClientMessageReceivedEvent` into live comms and workflow triggers. Document the convention; no server change.

---

## §9 SDK (`Clients/Wbskt.Client.Sdk`)

1. **Auto-detected metadata** — apps should get platform/SDK reporting for free:
   - `Agent` = `"csharp-sdk"`, `Version` = assembly informational version, `OS` = `RuntimeInformation.RuntimeIdentifier` (e.g. `linux-arm64`).
   - `WbsktClient.HandleConnected` currently rebroadcasts capabilities only if the app called `UpdateCapabilitiesAsync`; change it to **always** send `"capabilities"` on every (re)connect, merging auto-detected metadata with the app-provided `List<CommandCapability>` (empty list if none).
2. **`ReportStateAsync`** — new public API:
   ```csharp
   Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch);   // => SendAsync("state.report", patch)
   ```
3. **Ack support** (§8): auto-`sys.ack`, `commandId` on the receive callback.
4. Remove the `[RJ]` ping-pong TODO in `SocketClient.ReceiveLoopAsync` once §5 lands (the client side of the contract was already correct).
5. Fix the receive-loop framing bug (§12.1) — it bites here first, because capabilities payloads are the first messages likely to exceed 4 KB.

---

## §10 Templates (Send panel)

Two complementary sources for the picker:

1. **Saved payloads** — the `MessageTemplates` CRUD (§3.4, §4.6). Workspace-scoped, optionally pinned to a policy so the picker can show only templates relevant to the selected client's policy.
2. **Capability-derived forms** — once §6 persists `CommandCapability(Command, Description, Parameters: List<PropertySchema>)`, the console generates a form/payload skeleton per declared command (name, data types, required flags, defaults) from `ClientDetailResponse.Capabilities`. Zero extra backend.

---

## §11 Explorer & console wiring notes

| Mock element | Source |
|---|---|
| Policy group headers + counts | `GET …/registration-policies` (now with counts, §4.7) |
| Clients under a policy | `GET …/clients/policy/{policyRefId}` |
| Online dot / latency / last-seen | `ClientResponse.IsConnected / LastRttMs / LastActivityAt`; live updates via `OnClientConnectedEvent` / `OnClientDisconnectedEvent` / `OnClientLatencyMeasuredEvent` |
| `retry` state | **Not representable server-side** — reconnect/backoff state lives inside the SDK (`MonitorReconnectionAsync`). UI falls back to offline + last-seen. Optional future: SDK presence-state report (`sys.presence`) — out of scope for V1 |
| Detail header | `GET …/clients/{clientRefId}` (§4.1) |
| Live comms | Backfill `GET …/clients/{clientRefId}/comms` (§4.5), then SignalR tail on the `OnClient*` methods |
| State variables | `GET …/clients/{clientRefId}/state` + `OnClientPropertyUpdatedEvent` |
| Properties changed | `GET …/event-logs?clientRefId=…&eventName=ClientPropertyUpdatedEvent` |

---

## §12 Security & hygiene (do alongside)

1. **Receive-loop framing bug (latent, fix in P0):** both `SocketHandler.ReceiveLoopAsync` (server) and `SocketClient.ReceiveLoopAsync` (SDK) read a single 4 KB buffer and never check `ReceiveResult.EndOfMessage` — any message > 4 KB is truncated/split into garbage frames. Accumulate until `EndOfMessage` (with a max-message-size cap, e.g. 64 KB → close with `MessageTooBig`). Capabilities payloads will hit this first.
2. **Revoke hardening** — new `Socket.Host` `ClientStatusChangedHandler : IConsumer<ClientStatusChangedEvent>`: if `Status != (byte)ClientStatus.Registered`, close the socket (`PolicyViolation`) **and** add the refId to an in-memory revocation cache (TTL ≥ client JWT lifetime, 1 h) checked in `SocketHandler.HandleAsync` before accepting — otherwise the SDK's auto-reconnect walks straight back in with its still-valid JWT. Same single-node constraint as `ConnectionManager`; both need a shared store (e.g. Redis) when the socket tier scales out.
3. **Stale presence after hard crash:** if `Socket.Host` dies without running the `finally` block, `IsConnected` stays 1 forever. On `Socket.Host` startup, publish a presence-reset (consume in Management.Host → `Client_ResetAllPresence`). Valid only while the socket tier is single-node; note as a known limitation.
4. **`[Authorize]` on `ClientsController`:** the class relies solely on per-action `ResolveWorkspaceAsync`; add the class-level attribute like `RegistrationPoliciesController` has (defense in depth — an action added without a resolve call would otherwise be anonymous).
5. **Ingestion validation (§6):** cap `capabilities` and `state.report` payload sizes (e.g. 32 KB / 8 KB), cap variable count per report and name length (100), reject non-object payloads — these land in `NVARCHAR(MAX)` columns writable by any registered device.
6. **Policy PIN** is returned cleartext in `RegistrationPolicyResponse` (masking is UI-only) and there is no regenerate-PIN endpoint — defer both to the policies feature pass; recorded here so it isn't lost.
7. **Event-log payloads** store full command/message JSON (`EventData`) — flag to users that device payloads should not carry secrets; consider redaction/retention knobs later.

---

## §13 Phasing

**P0 — detail page core** (everything the mock's header + actions need):
`GET clients/{refId}` + `ClientResponse` enrichment; rename; `ConnectedAt`/presence SP change; ping/RTT loop (§5.1–5.4); capabilities ingestion + storage (§6 capabilities branch); revoke force-disconnect + revocation cache; framing-bug fix (§12.1); SDK auto-metadata.

**P1 — state & explorer**:
`state.report` SDK API + ingestion; `ClientStateVariables` + `GET state`; `ClientPropertyUpdatedEvent` producer with `OldValue`; comms backfill endpoint; policy client counts; `PingSampler`; presence reset on startup.

**P2 — send UX & polish**:
Command acks (§8); `MessageTemplates` CRUD (§10); remaining §12 hygiene (validation caps, `[Authorize]`, docs for reserved types).

Dependency notes: §5 needs the `ConnectionManager` metadata refactor first; §6 state branch and §8 both depend on the §12.1 framing fix for payloads > 4 KB; §4.1 depends on §3 columns + `ClientCapabilities` table.

### Testing

- **Unit** (xUnit + Moq + FluentAssertions): `ClientMetadataIngestionHandler` (capabilities parse/validate, state patch → upsert + event with OldValue, oversized payload rejected), `ClientPingHandler`, repurposed `ClientPongHandler`, rename service (ownership check), revocation cache. Note: the management host currently has **zero** controller/service tests — new endpoints should establish the pattern.
- **Integration** (SQL): `ClientStateVariable_Upsert` returns previous value + no-op detection; `Client_UpdatePresence` connect/disconnect `ConnectedAt` behavior; policy count subqueries.
- **E2E** (`Tests/Wbskt.E2E.FeatureTests`, SkippableFact, drive `Wbskt.Simulator`): connect → capabilities visible on detail endpoint; ping → `LastRttMs` populated; `ReportStateAsync` → state endpoint + property-changed log; revoke → socket closed and reconnect rejected.

---

## §14 Known limitations (V1)

- `ConnectionManager`, revocation cache, and presence reset are **single-node**; scaling the socket tier requires a shared connection registry.
- Commands to offline clients are silently dropped (no queueing/store-and-forward) — unchanged from today; consider a `PendingCommands` outbox later.
- `retry`/reconnecting state is client-side only (§11).
- Revoked clients can hold a valid JWT up to 1 h; the revocation cache closes the socket window, but REST `client-auth` re-login is already blocked by status.
- `LastActivityAt` reflects connect/disconnect, not message traffic (bumping it per message was considered and skipped — write amplification; the explorer's last-seen semantics only need lifecycle times).
