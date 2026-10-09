# WBSKT Project Coding Conventions

## 1. Architectural & Library Principles

*   **Native First:** Always prefer native .NET and ASP.NET Core alternatives over third-party libraries for fundamental features. Examples include using `PasswordHasher<T>` instead of `BCrypt.Net`, `System.Text.Json` instead of `Newtonsoft.Json`, and the built-in Dependency Injection container.
*   **Error Handling:** Expected failures travel as `Result` / `Result<T>` carrying an `Error`. Services return them; controllers inherit `ApiControllerBase` and call `MapResult`/`MapError`, which is the single place a status code is chosen. `GlobalExceptionMiddleware` is a safety net for what escapes as an exception, not the primary path.
    *   Never hand-roll `MapResult`/`MapError`/`GetCurrentUserId` in a controller — they live on `ApiControllerBase`.
    *   **Outcomes vs faults:** a `Result` is for business outcomes only: not found, forbidden, conflict, validation. A fault (a SQL error, a timeout, a bug) is not caught to become `Error.Failure(..., ex.Message)`; it propagates to `GlobalExceptionMiddleware`, which logs it once with the stack and returns the safe 500. Catch only where something must happen on failure: per-item results in a bulk call, compensation after a partial write, or a specific exception that *is* an outcome (`SqlException` for a known constraint number).
    *   **Lookups that may miss:** providers return `T?` from a `Find...` method for anything that may not exist (`ExecuteFindAsync` in `BaseSqlProvider`), and never throw for "not found". The caller turns `null` into its `<RESOURCE>_NOT_FOUND`.
    *   Pick the `Error` factory by the status you want: `Validation` → 400, `Unauthorized` → 401, `Forbidden` → 403, `NotFound` → 404, `Conflict` → 409, `Unavailable` → 503 with `Retry-After`, `Failure` → 500.
    *   `Unavailable` is for a dependency that is down when nothing was done, so the caller can simply retry (a command whose publish *is* the action, with the broker down). Never hand-build a 503 in a controller.
    *   `Unauthorized` means "we cannot identify the caller". A caller who is known but lacks a permission is `Forbidden`. Using 401 there makes clients that redirect to login on 401 sign the user out over a missing permission.
    *   A `Failure` message is assumed to be internal detail (exception text). It is logged and replaced with a generic message before it reaches the client, so never rely on it being visible.
*   **Thin controllers:** A controller binds the request, calls one service and maps its `Result`. Validation, ownership, publishing events and talking to the engine belong to services, where they can be unit-tested and reused. A controller never injects `IEventBus`, a provider or `IReferenceMapper`; `ControllerLayeringTests` enforces it. The only status a controller picks itself is between two successes (`200` vs `202`).

---

## 2. .NET & C# Conventions

### Syntax & Control Flow
*   **Braces Always:** Always use curly braces `{ }` for all `if`, `else`, `for`, `foreach`, and `while` blocks, even for single-line statements.
*   **Spacing:** Always include an empty line before a comment to separate it from the preceding code block.
*   **Modern Disposables:** Use the `await using var` syntax for all objects implementing `IAsyncDisposable` (e.g., `SqlConnection`, `SqlDataReader`) to ensure efficient, non-blocking cleanup.

### Naming Patterns
*   **`FindBy...`**: Returns strictly an **internal integer ID**.
*   **`GetBy...`**: Returns a **full record, entity, or DTO**.
*   **Async Suffix:** Any method returning a `Task` or `ValueTask`, or marked as `async`, must end with the `Async` suffix.

### Method Signatures & Returns
*   **No Null Returns:** Public functions must never return `null`. Use descriptive exceptions to handle missing data or logic failures.
*   **Collection Returns:** When returning a list or collection of items, always use `IReadOnlyCollection<T>` to signal that the result is an immutable snapshot.
*   **Controller Implementation:** Always use `async Task<T>` as the return type for controller actions. Use `async Task` (no generic) for actions that perform an operation without returning data. Success responses return the data directly.
*   **No Anonymous Returns:** Controller actions must never return `object` or anonymous types. Always define a named record or class for the response.
*   **DTO Preference:** For `POST` or `PUT` actions, prefer using DTOs/Records for input parameters instead of long lists of primitive arguments.
*   **Lists:** A list action binds `[FromQuery] PageRequest page` and returns `Page<T>` (`items`, `nextCursor`, and `totalCount` only where counting is cheap). A list ordered by an id pages by key (`page.AfterKey()`, cursor from `PageRequest.KeyCursor`); one that needs a total pages by offset (`page.Offset()`, then `MapPage`). Never take `skip`/`take`/`top` parameters of your own, and never return a bare array or a list type named for its items.
*   **Time Ranges:** `from`/`to` are `DateTimeOffset?` and are resolved with `TimeRange.Resolve`, which owns the defaults, the 1970 floor and the longest span. Each endpoint passes only its own default and cap.

### API Style (routes and models)
Every new or changed endpoint follows these rules; `Docs/API.Endpoints.md` is the catalogue.

*   **Collections are plural nouns**, scoped under the workspace: `/api/workspaces/{workspaceRef}/clients`, `/registration-policies`, `/workflows/{workflowRef}/runs`. A sub-collection is plural too (`/commands`, `/signals/{signalName}`, `/versions`).
*   **Field changes PATCH the resource.** `PATCH /clients/{clientRef}` with `{ name?, status? }`: a field left out stays as it is, and a request naming no field is a 400. Do not add one route per field (`/name`, `/status`). `PUT` replaces a whole thing (a template, a client's tag set).
*   **Operations POST to a verb.** Something that is not a field write, that has side effects of its own or that cannot be undone by writing a field back is `POST /{resource}/{action}`: `/disable`, `/deprecate`, `/reinstate`, `/rotate-pin`, `/rotate-secret`, `/ping`, `/cancel`. Creating something in a collection is `POST` to the collection (`/commands`, `/runs`).
*   **Filters are query parameters, not nested routes.** `GET /clients?policyRefId=` rather than `/clients/policy/{policyRef}`. A filter is named after the field it matches in the response (`policyRefId`, `clientRefId`, `status`, `tag`). A filter naming another workspace's resource is a 404, never an empty page.
*   **Route parameters name the resource they identify:** `{clientRef}`, `{policyRef}`, `{templateRef}`, `{workflowRef}`, `{runRef}`, and `{signalName}` / `{variableName}` for names. Never a bare `{refId}`, `{id}` or `{name}`. The C# parameter has the same name.
*   **Lists** take `PageRequest` and return `Page<T>` (see Method Signatures above). Time ranges are `from`/`to` through `TimeRange`.
*   **Models live in model files, never at the bottom of a controller.** Request and response records go in the host's `Models/`; a contract that another project needs (the E2E tests, an SDK, the engine) goes in `Wbskt.Management.Models`. A response never exposes an internal `Id`.
*   **Renaming a route keeps the old one for one release** as a second action marked `[ApiExplorerSettings(IgnoreApi = true)]` that calls the new one, with a `Deprecated:` summary naming its replacement and an E2E test that it still answers. Note it in `Docs/API.Endpoints.md` and in the console issue, and remove it in the release after.

---

## 3. Data Access & Persistence

### The ID Boundary
*   **RefId vs. Id:** Public APIs must only expose **`RefId` (GUID)**. The **`Id` (Int)** is strictly for internal database relations.
*   **Mapping Responsibility:** Controllers pass the public `RefId` to the service, and the **service** resolves it (through a provider's `Find...` method or `WorkspaceOwnership.LoadAsync`), because resolving it is also where ownership is checked.
*   **Workspace first, 403:** A workspace reference the caller is not a member of, or that does not exist, is `Error.Forbidden` (403) and the two cases are not told apart. This is the auth host's `resolve` answer, and it is what stops the API from confirming that a workspace exists.
*   **Resources inside a workspace, 404:** Once the caller has been resolved as a member of the workspace in the route, a resource reference (client, policy, template, workflow, run, ...) that does not exist **or** belongs to another workspace is `Error.NotFound` (404) with the resource's `<RESOURCE>_NOT_FOUND` code and the same message in both cases. The caller learns nothing beyond "not in this workspace", which is all a member needs to know. The same holds for a reference used as a filter (`?clientRefId=`): it is a 404, never an empty page.
*   **403 inside a workspace means a missing permission** (`PERMISSION_UNAUTHORIZED`), never an unknown or foreign reference.

### Reference Mapping Pattern
To decouple public GUIDs from internal integer IDs without polluting every service with lookup logic:

1.  **`IReferenceProvider`**: An interface implemented by any Provider that can look up an ID by a GUID (`Task<int> FindByReferenceIdAsync(Guid referenceId)`).
2.  **`IReferenceMapper`**: A high-level interface used by services and adapters (never by a controller).
3.  **`ReferenceMapper<T>`**: A generic implementation that delegates lookups to a specific `IReferenceProvider`.
4.  **Registration**: Mappers are registered as **Keyed Services** (e.g., `builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IProjectProvider>>("Project")`).

### Workspace Permissions (management host)
*   **Declare, don't check:** A workspace-scoped action (`api/workspaces/{workspaceRef}/...`) states what it needs with `[RequiresPermission(PermissionNames.X)]` and takes `[FromWorkspace] int workspaceId`. `WorkspacePermissionFilter` resolves the workspace once, before model binding, and answers the refusal itself; controllers never call `ResolveWorkspaceAsync`.
*   **All or any:** Several permissions on one attribute need all of them; `Mode = PermissionMatch.Any` needs one. Several attributes on an action must all pass. Attributes on a controller are its actions' default; an action with its own replaces them.
*   **Enforced by a test:** `ManagementEndpointPermissionTests` fails for a workspace-scoped action without the attribute, and pins every action's permissions.
*   **Owned references:** Load a resource through `WorkspaceOwnership.LoadAsync` (or `Check` for one already in hand). It answers the resource's one `<RESOURCE>_NOT_FOUND` 404 both for a reference that names nothing and for another workspace's, including references used as list filters. A 403 inside a workspace only ever means a missing permission.

### Provider Pattern
*   **Base Provider Pattern:** Do not repeat `SqlConnection` or `SqlCommand` boilerplate in every method. Inherit from a `BaseSqlProvider` (or equivalent) that encapsulates connection lifecycle, command execution, and mapping.
*   **No Cross-Provider Dependencies:** A Provider must never depend on or inject another Provider. If an operation requires data from multiple sources, it should be orchestrated in the Service layer.
*   **DRY Mappings:** Extract repeated entity mapping logic (e.g., `SqlDataReader` to `User`) into reusable private methods.

---

## 4. Database (SQL) Project Conventions

### Stored Procedure (SP) Naming
| Purpose           | Pattern                       | Example                  |
|-------------------|-------------------------------|--------------------------|
| **Lookup ID**     | `[Entity]_FindBy_[Criteria]`  | `User_FindBy_RefId`      |
| **Fetch Record**  | `[Entity]_GetBy_[Criteria]`   | `User_GetBy_Id`          |
| **Create/Insert** | `[Entity]_[Create/Insert]`    | `User_Create`            |
| **Link/Assign**   | `[Entity]_[Grant/Insert]`     | `RolePermission_Grant`   |
| **Logic/Verify**  | `[Entity]_Verify`             | `Permission_Verify`      |

### SQL Standards
*   **Keywords:** Use **UPPERCASE** (e.g., `SELECT`, `INSERT`, `WHERE`).
*   **Schema:** Always use the `dbo.` prefix.
*   **Precision:** Use `DATETIME2(3)` for all standard timestamps (e.g., `CreatedAt`, `ModifiedAt`) unless sub-second precision is specifically required.
*   **Audit Columns:** Every new table must include a `CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()` column.
*   **Formatting:** List columns explicitly (no `SELECT *`) with one column per line for better git diffs.

---

## 5. Implementation Examples

### C# Controller
```csharp
public class TemplatesController : ApiControllerBase
{
    private readonly ITemplateService _templateService;

    public TemplatesController(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    [HttpGet("{templateRef:guid}")]
    [RequiresPermission(PermissionNames.TemplatesRead)]
    public async Task<ActionResult<TemplateResponse>> Get([FromWorkspace] int workspaceId, Guid templateRef, CancellationToken ct)
    {
        // The service resolves the RefId and checks the workspace owns it; unknown and foreign
        // references both come back as TEMPLATE_NOT_FOUND (404), see "The ID Boundary".
        return MapResult(await _templateService.GetAsync(workspaceId, templateRef, ct));
    }
}
```

### BL Service Layer
```csharp
public class TemplateService : ITemplateService
{
    private readonly ITemplateProvider _templateProvider;

    public async Task<TemplateResponse> GetByIdAsync(int id)
    {
        var template = await _templateProvider.GetByIdAsync(id);

        // Logic requiring an empty line before this comment
        if (!template.IsActive)
        {
            throw new SecurityException("Template is restricted.");
        }

        return template.ToResponse();
    }
}
```

### SQL Stored Procedures
```sql
-- Fetching the internal ID by public RefId
CREATE PROCEDURE dbo.User_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Users
    WHERE RefId = @RefId;
END

-- Fetching the full record by internal ID
CREATE PROCEDURE dbo.User_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        Username,
        Email,
        CreatedAt
    FROM dbo.Users
    WHERE Id = @Id;
END
```