# WBSKT Project Coding Conventions

## 1. Architectural & Library Principles

*   **Native First:** Always prefer native .NET and ASP.NET Core alternatives over third-party libraries for fundamental features. Examples include using `PasswordHasher<T>` instead of `BCrypt.Net`, `System.Text.Json` instead of `Newtonsoft.Json`, and the built-in Dependency Injection container.
*   **Error Handling:** Expected failures travel as `Result` / `Result<T>` carrying an `Error`. Services return them; controllers inherit `ApiControllerBase` and call `MapResult`/`MapError`, which is the single place a status code is chosen. `GlobalExceptionMiddleware` is a safety net for what escapes as an exception, not the primary path.
    *   Never hand-roll `MapResult`/`MapError`/`GetCurrentUserId` in a controller — they live on `ApiControllerBase`.
    *   Pick the `Error` factory by the status you want: `Validation` → 400, `Unauthorized` → 401, `Forbidden` → 403, `NotFound` → 404, `Conflict` → 409, `Failure` → 500.
    *   `Unauthorized` means "we cannot identify the caller". A caller who is known but lacks a permission is `Forbidden`. Using 401 there makes clients that redirect to login on 401 sign the user out over a missing permission.
    *   A `Failure` message is assumed to be internal detail (exception text). It is logged and replaced with a generic message before it reaches the client, so never rely on it being visible.

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

---

## 3. Data Access & Persistence

### The ID Boundary
*   **RefId vs. Id:** Public APIs must only expose **`RefId` (GUID)**. The **`Id` (Int)** is strictly for internal database relations.
*   **Mapping Responsibility:** The **Controller** is responsible for mapping a public `RefId` to an internal `Id`. This is achieved using the **Reference Mapping Pattern**.
*   **Workspace first, 403:** A workspace reference the caller is not a member of, or that does not exist, is `Error.Forbidden` (403) and the two cases are not told apart. This is the auth host's `resolve` answer, and it is what stops the API from confirming that a workspace exists.
*   **Resources inside a workspace, 404:** Once the caller has been resolved as a member of the workspace in the route, a resource reference (client, policy, template, workflow, run, ...) that does not exist **or** belongs to another workspace is `Error.NotFound` (404) with the resource's `<RESOURCE>_NOT_FOUND` code and the same message in both cases. The caller learns nothing beyond "not in this workspace", which is all a member needs to know. The same holds for a reference used as a filter (`?clientRefId=`): it is a 404, never an empty page.
*   **403 inside a workspace means a missing permission** (`PERMISSION_UNAUTHORIZED`), never an unknown or foreign reference.

### Reference Mapping Pattern
To decouple public GUIDs from internal integer IDs without polluting every service with lookup logic:

1.  **`IReferenceProvider`**: An interface implemented by any Provider that can look up an ID by a GUID (`Task<int> FindByReferenceIdAsync(Guid referenceId)`).
2.  **`IReferenceMapper`**: A high-level interface used by Controllers.
3.  **`ReferenceMapper<T>`**: A generic implementation that delegates lookups to a specific `IReferenceProvider`.
4.  **Registration**: Mappers are registered as **Keyed Services** (e.g., `builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IProjectProvider>>("Project")`).

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

### C# Controller (Using Reference Mapper)
```csharp
public class TemplateController : ControllerBase
{
    private readonly IReferenceMapper _templateMapper;
    private readonly ITemplateService _templateService;

    public TemplateController(
        [FromKeyedServices("Template")] IReferenceMapper templateMapper,
        ITemplateService templateService)
    {
        _templateMapper = templateMapper;
        _templateService = templateService;
    }

    [HttpGet("{templateRef:guid}")]
    public async Task<TemplateResponse> Get(Guid templateRef)
    {
        // 1. Translate Guid to internal Int ID
        int internalId = await _templateMapper.FindByReferenceIdAsync(templateRef);
        
        if (internalId <= 0) 
        {
            // Unknown and foreign references read the same: 404, see "The ID Boundary".
            throw new NotFoundException("Template not found.");
        }

        // 2. Use the internal ID for service layer calls
        return await _templateService.GetByIdAsync(internalId);
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