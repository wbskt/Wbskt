## .NET & C# Conventions

### 1. Control Flow & Syntax

* **Braces Always:** Always use curly braces `{ }` for all `if`, `else`, `for`, `foreach`, and `while` blocks, even for
  single-line statements.
* **Spacing:** Always include an empty line before a comment to separate it from the preceding code block.
* **No Null Returns:** Public functions must never return `null`. Use descriptive exceptions to handle missing data or
  logic failures.
* **Controller Implementation:** Always use `async Task<T>` as the return type for controller actions (where `T` is the
  DTO). Use `async Task` (no generic) for actions that perform an operation without returning data. Success responses return the data directly.
* **No Anonymous Returns:** Controller actions must never return `object` or anonymous types. Always define a named record or class for the response.
* **Error Handling:** Failure responses and HTTP status codes for errors are managed exclusively by the global exception
  handler.

### 2. Naming Patterns

We distinguish between fetching IDs and fetching full data:

* **`FindBy...`**: Returns strictly an **internal integer ID**.
* **`GetBy...`**: Returns a **full record, entity, or DTO**.
* **Async Suffix:** Any method returning a `Task` or `ValueTask`, or marked as `async`, must end with the `Async`
  suffix.

### 3. Implementation Example

```csharp
// --- Controller Layer ---
[HttpGet("{email}")]
public async Task<User> GetUser(string email)
{
    // The ONLY layer allowed to map reference keys to internal IDs
    int internalId = _userProvider.FindByEmail(email);

    if (internalId <= 0)
    {
        throw new SecurityException("...........");
    }

    var result = _userService.GetActiveProfile(internalId);
    return result;
}

[HttpGet("{templateRef:guid}", Name = nameof(GetTemplate))]
public async Task<TemplateResponse> GetTemplate(Guid templateRef, ncellationToken ct)
{
    var id = await _referenceMapper.FindByReferenceId(templateRef);
    var template = await _templateService.GetById(id, ct);

    return template.ToContract();
}

// --- BL Service Layer ---
public class UserService : IUserService
{
    private readonly IUserProvider _userProvider;

    public UserStatus GetUserStatus(int id)
    {
        var user = _userProvider.GetById(id);

        if (user == null)
        {
            throw new SecurityException("...........");
        }

        // Logic requiring an empty line before this comment
        if (user.IsActive)
        {
            return UserStatus.Active;
        }

        return UserStatus.InActive;
    }
}
```

---

## Database (SQL) Project Conventions

### 1. Stored Procedure (SP) Naming

| Purpose           | Pattern                       | Example                  |
|-------------------|-------------------------------|--------------------------|
| **Lookup ID**     | `[Entity]_FindBy_[Criteria]`  | `User_FindBy_Email`      |
| **Fetch Record**  | `[Entity]_GetBy_[Criteria]`   | `User_GetBy_Id`          |
| **Create/Insert** | `[Entity]_[Create/Insert]`    | `User_Create`            |
| **Link/Assign**   | `[Entity]_[Grant/Insert]`     | `RolePermission_Grant`   |
| **Logic/Verify**  | `[Entity]_Verify`             | `Permission_Verify`      |

### 2. SQL Standards

* **Keywords:** Use **UPPERCASE** (e.g., `SELECT`, `INSERT`, `WHERE`).
* **Schema:** Always use the `dbo.` prefix.
* **Formatting:** List columns explicitly (no `SELECT *`) with one column per line for better git diffs.

```sql
CREATE PROCEDURE dbo.User_FindBy_Email
    @Email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    -- Returns internal integer ID only
    SELECT Id
    FROM dbo.Users
    WHERE Email = @Email;
END
```
