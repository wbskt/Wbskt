# Auth Host — E2E Scenario Catalogue

Scenario definitions for the `Wbskt.Auth.Host` API surface: `AuthController` (`/api/auth`),
`WorkspacesController` (`/api/workspaces`) and `ManagementController` (`/api/tenants`).

These are specifications, not code. Each row is meant to become one `[SkippableFact]` in
`Tests/Wbskt.E2E.FeatureTests/Scenarios/Auth/`, named after its ID.

---

## 1. How to read this

| Column | Meaning |
|---|---|
| **ID** | Stable identifier. Use as the test method name suffix, e.g. `AUTH_REG_04_DuplicateEmail_Returns409`. |
| **±** | `+` positive (the feature works) / `−` negative (the failure is handled correctly). |
| **Scenario** | Preconditions and the action. |
| **Expected** | Status code, error `code`, and any side effect that must be asserted separately. |

### Response shapes

Three distinct failure shapes exist; a test asserting the wrong one passes for the wrong reason.

- **`Error`** — `{ code, message, type }`, produced by `ApiControllerBase.MapError` for every
  `Result` failure. This is what the `code` column refers to.
- **`ValidationProblemDetails`** — produced by `[ApiController]` model validation *before* the action
  runs (`[Required]`, `[StringLength]`, `[EmailAddress]`, malformed JSON, unbindable route values).
  It has no `code` field. Rows expecting this say **model-validation 400**.
- **Empty body** — `204 No Content` from `MapResult(Result.Success())`. Most mutations return this.

### Fixture helpers the scenarios use

All present on `ServicesFixture`. The ones worth knowing before writing a new scenario:

- `CreateUserAsync(invitationToken?)` → registers and logs in a unique account. On its own this
  makes an administrator **of a tenant of its own** — see the seed facts below.
- `CreateUserInTenantAsync(adminToken, tenantRef)` → invites an account into someone else's tenant,
  where it holds nothing. This is how you arrange an unprivileged caller.
- `SendAsync(method, url, token?, body?)` → the raw escape hatch. Returns the response untouched, so
  negative scenarios can read the status, the error body, or a header. The typed helpers all call
  `EnsureSuccessStatusCode` and are for arranging, not asserting.
- `ReadErrorCodeAsync(response)` → the `code` off an `Error` body, or null when the body is
  `ValidationProblemDetails` instead.
- `GetPageAsync(url, token)` → item count plus the `X-Total-Count` header.
- `GetEffectivePermissionsAsync(token, workspaceRef)` → the resolved slug set, for asserting what an
  assignment actually delivered.

### Seed facts the scenarios depend on

Partly seeded by `Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql`, partly a
consequence of how registration works:

1. **Registration provisions a whole tenant.** One transaction creates the account, its own tenant,
   that tenant's `Admin`/`User` roles, the creator's membership, a **tenant-wide** Admin assignment,
   and a default workspace. A brand-new account is therefore an administrator — of its own tenant,
   and of nothing else.
2. `root` / `admin@wbskt.com` holds Admin tenant-wide in the seeded tenant and owns Default Workspace.
3. The Admin role carries every permission in the catalogue.
4. **Joining someone else's tenant requires an invitation.** `POST /api/tenants/{ref}/invitations`
   issues a token, and the invitee redeems it either at registration (`invitationToken` on the
   register body) or afterwards via `POST /api/invitations/accept`. The redeeming account's email
   must match the address invited.
5. `Workspace_Create` grants the creator the tenant's Admin role **scoped to the new workspace only**.

Facts 1, 4 and 5 are the backbone of the authorization scenarios. The boundary worth testing is no
longer "registered user vs admin" — everyone is an admin somewhere — but **scope**: an invited member
who creates a workspace administers *that workspace*, and tenant administration requires the same
slugs held tenant-wide, which a workspace-scoped grant deliberately does not satisfy (`WS_CRD_04`).

To arrange a non-privileged caller, invite them into someone else's tenant
(`ServicesFixture.CreateUserInTenantAsync`). Registering alone will not do it — that makes them an
administrator of their own tenant instead.

### Known gaps in reachability

- **Rate limiting is per-IP and shared across the whole suite** (§7). Running those scenarios
  alongside the rest will cause unrelated 429s for the remainder of the window, which is why they
  are gated behind `E2E_RATE_LIMIT_TESTS=1` and meant to be run with a filter.
- **Test data accumulates.** Registration, workspace creation and role creation have no cleanup path
  (no delete-user endpoint). Every scenario must use a unique suffix, and the tenant member list will
  grow monotonically across runs — assertions must be "contains", never "count equals".

---

## 2. Regression — the four fixes

Highest priority: these encode defects that shipped, so they must fail against the previous revision.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `REG_01` | − | `POST /api/workspaces/resolve` with a well-formed but unknown `workspaceRef` | **403** `WORKSPACE_NOT_FOUND` (was 404) |
| `REG_02` | − | Admin creates a workspace, deletes it, then resolves the dead ref | **403** `WORKSPACE_NOT_FOUND` |
| `REG_03` | − | Same dead ref used against a *management host* workspace route (e.g. `GET /api/workspaces/{ref}/clients`) | **403**, never **500**. This is the user-visible half of the bug: `AuthServiceClient` only understands 401/403 and turns anything else into a `Failure` → 500 |
| `REG_04` | − | `POST /api/workspaces/{unknownRef}/members` | **403** `WORKSPACE_NOT_FOUND` |
| `REG_05` | − | `GET /api/workspaces/{unknownRef}/members` | **403** `WORKSPACE_NOT_FOUND` |
| `REG_06` | − | `PUT`/`DELETE /api/workspaces/{unknownRef}` | **403** `WORKSPACE_NOT_FOUND` |
| `REG_07` | − | Grant a role permission with slug `"roles.mange"` (typo) | **400** `AUTH_OPERATION_REJECTED`, message names the slug (was **204** with nothing written) |
| `REG_08` | − | Read the role's permissions after `REG_07` | The typo slug is **absent** — proves the 400 was not merely cosmetic |
| `REG_09` | − | Grant a *user* permission with an unknown slug | **400** `AUTH_OPERATION_REJECTED` |
| `REG_10` | + | Grant a role permission with a valid slug | **204**, and the slug appears in `GET .../roles/{ref}/permissions` |
| `REG_11` | − | `POST .../roles/{ref}/permissions` with `workspaceRef` in the body | **model-validation 400** — the field is no longer part of the contract and unmapped members are rejected (was silently dropped) |
| `REG_12` | + | `GET /api/tenants/{t}/roles?skip=0&take=1` | **200**, exactly 1 item, and header `X-Total-Count` ≥ 2 |
| `REG_13` | + | Same for `/groups`, `/permissions`, `/members` | `X-Total-Count` present on all four |
| `REG_14` | + | `GET /api/workspaces/{ref}/members?take=1` | **200**, 1 item, `X-Total-Count` present |
| `REG_15` | + | `X-Total-Count` is invariant across pages: request `take=1&skip=0` then `take=1&skip=1` | Same header value both times |

---

## 3. Registration — `POST /api/auth/register`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_REG_01` | + | Unique username + email + 12-char password | **204** |
| `AUTH_REG_02` | + | Registered user can immediately log in | **200** with both tokens |
| `AUTH_REG_03` | + | New user appears in the admin's `GET /api/tenants/{t}/members` | Present, `isActive: true` |
| `AUTH_REG_04` | − | Email already registered | **204** — identical to success; the address's owner is mailed instead |
| `AUTH_REG_05` | − | Username already registered, different email | **409** `AUTH_USERNAME_CONFLICT` |
| `AUTH_REG_06` | − | Password of 11 characters | model-validation 400 |
| `AUTH_REG_07` | + | Password of exactly 12 characters | **204** — boundary |
| `AUTH_REG_08` | − | Password of 129 characters | model-validation 400 |
| `AUTH_REG_09` | − | Username of 2 characters | model-validation 400 |
| `AUTH_REG_10` | + | Username of exactly 3 characters | **204** — boundary |
| `AUTH_REG_11` | − | Username of 51 characters | model-validation 400 |
| `AUTH_REG_12` | − | `email` = `"not-an-email"` | model-validation 400 |
| `AUTH_REG_13` | − | Email exceeding 100 characters | model-validation 400 |
| `AUTH_REG_14` | − | Missing `password` field entirely | model-validation 400 |
| `AUTH_REG_15` | − | Empty JSON body `{}` | model-validation 400 listing all three fields |
| `AUTH_REG_16` | − | Malformed JSON | **400** |
| `AUTH_REG_17` | − | `Content-Type: text/plain` | **415** |
| `AUTH_REG_18` | − | Duplicate email differing only in case (`A@x.com` vs `a@x.com`) | Document actual behaviour — depends on the DB collation, and the two branches (409 vs a second account) are very different products |
| `AUTH_REG_19` | − | Two concurrent registrations of the same email | Exactly one **204**, one **409**; never two accounts |
| `AUTH_REG_20` | + | Endpoint is anonymous — no `Authorization` header needed | **204** |
| `AUTH_REG_21` | + | Password is not recoverable: register, then confirm no endpoint echoes it back | `GET /api/tenants/{t}/members` exposes only `refId`, `username`, `email`, `isActive` |

---

## 4. Login — `POST /api/auth/login`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_LOG_01` | + | Correct credentials | **200**, non-empty `accessToken` + `refreshToken` |
| `AUTH_LOG_02` | + | Access token is a JWT carrying `nameid`, `unique_name`, `email`, `type: "user"` | Decodes; `type` is `user` |
| `AUTH_LOG_03` | + | Two consecutive logins issue **different** refresh tokens | Both usable independently (multi-device) |
| `AUTH_LOG_04` | − | Correct email, wrong password | **401** `AUTH_INVALID_CREDENTIALS` |
| `AUTH_LOG_05` | − | Unregistered email | **401** `AUTH_INVALID_CREDENTIALS` — **identical code and message to `04`**; asserting they match is the point, it prevents account enumeration |
| `AUTH_LOG_06` | − | Deactivated account, correct password | Retired: tenant administrators suspend members instead, so only an operator can disable an account. The login still answers **401** `AUTH_USER_INACTIVE` for one, which distinguishes it from `04`/`05` |
| `AUTH_LOG_07` | − | Password differing only in case | **401** |
| `AUTH_LOG_08` | − | Email with leading/trailing whitespace | Document actual behaviour (no trimming today) |
| `AUTH_LOG_09` | − | Missing `password` | model-validation 400 |
| `AUTH_LOG_10` | − | `email` not an email address | model-validation 400 |
| `AUTH_LOG_11` | − | Empty body | model-validation 400 |
| `AUTH_LOG_12` | − | SQL-injection string as email (`' OR 1=1 --`) | model-validation 400 or **401**; never 200, never 500 |
| `AUTH_LOG_13` | + | Login is anonymous | No `Authorization` header required |
| `AUTH_LOG_14` | + | Failed login does not consume or invalidate an existing session | Refresh token issued earlier still works |

---

## 5. Token refresh & rotation — `POST /api/auth/refresh-token`

The most security-sensitive area, and entirely uncovered today.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_RT_01` | + | Valid refresh token | **200**, a **new** access token *and* a **new** refresh token |
| `AUTH_RT_02` | + | New access token authenticates a protected call | `GET /api/workspaces` → 200 |
| `AUTH_RT_03` | + | Chained rotation: refresh three times in sequence | Each returns 200 with fresh tokens |
| `AUTH_RT_04` | − | **Replay.** Refresh with token A → get B; refresh with A again | **401** `AUTH_TOKEN_INACTIVE` |
| `AUTH_RT_05` | − | After the `AUTH_RT_04` replay, try to use B | **401** — replay revokes the *entire* session family, not just the replayed token. This is the assertion that proves the theft-detection works |
| `AUTH_RT_06` | − | After `AUTH_RT_05`, the user must log in again | Fresh login succeeds; the account is not locked |
| `AUTH_RT_07` | − | Garbage token string | **401** `AUTH_INVALID_TOKEN` |
| `AUTH_RT_08` | − | Empty-string token | model-validation 400 |
| `AUTH_RT_09` | − | Another user's valid refresh token | Succeeds as *that* user (tokens are bearer) — assert the returned access token's `nameid` is the token's owner, never the caller's |
| `AUTH_RT_10` | − | Access token submitted as a refresh token | model-validation 400 — a JWT is longer than the 255-character bound on `RefreshToken`, so it never reaches the lookup |
| `AUTH_RT_11` | − | Refresh after `logout` revoked the token | **401** `AUTH_TOKEN_INACTIVE` |
| `AUTH_RT_12` | − | Refresh after `logout-all` | **401** |
| `AUTH_RT_13` | − | Refresh for a user deactivated since issuance | Retired with account deactivation from the API; a suspended member's refresh still works (`MEM_32`) |
| `AUTH_RT_14` | − | Refresh token past its 7-day expiry | **401** `AUTH_TOKEN_INACTIVE` — needs clock control or a seeded expired row; mark skipped if neither is available |
| `AUTH_RT_15` | − | Two concurrent refreshes with the same token | Exactly one 200; the other 401. Must not mint two live families |
| `AUTH_RT_16` | + | Rotation is anonymous (no `Authorization` header) | **200** — the refresh token is the credential |
| `AUTH_RT_17` | + | Access token issued before rotation stays valid until expiry | Documents that rotation does not retro-invalidate access tokens |
| `AUTH_RT_18` | − | Log out on device A, then device A retries its refresh; check device B | B is **also** revoked. Logout marks the token revoked, and rotation cannot tell a deliberate logout from a stolen-token replay — both are "a retired token came back". A benign post-logout retry therefore signs out every device and raises a `RefreshTokenReplay` alert. Assert current behaviour; the false positive is a separate decision |

---

## 6. Logout — `POST /api/auth/logout` and `/logout-all`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_OUT_01` | + | Logout with a live refresh token | **204**; that token then fails refresh with 401 |
| `AUTH_OUT_02` | + | Logout with an **unknown** token | **204** — deliberately indistinguishable from `01`. Assert both status *and* empty body, so the endpoint cannot be used as a token oracle |
| `AUTH_OUT_03` | + | Logout twice with the same token | **204** both times (idempotent) |
| `AUTH_OUT_04` | + | Logout is anonymous | No `Authorization` header required |
| `AUTH_OUT_05` | + | Logout revokes only the presented session | A second device's refresh token still works |
| `AUTH_OUT_06` | − | Missing `refreshToken` field | model-validation 400 |
| `AUTH_OUT_07` | + | `logout-all` with a valid access token | **204**; **every** refresh token for that user fails afterwards |
| `AUTH_OUT_08` | − | `logout-all` with no token | **401** |
| `AUTH_OUT_09` | − | `logout-all` with an expired/garbage bearer token | **401** |
| `AUTH_OUT_10` | + | `logout-all` does not affect other users | A second user's session survives |
| `AUTH_OUT_11` | − | Access tokens (the caller's and another device's) after `logout-all` | **401** — logout-all revokes issued access tokens too |
| `AUTH_OUT_12` | + | `logout-all` is idempotent | Second call also **204** |

---

## 6a. Account security — `change-password`, `sessions`, lockout

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_PW_01` | + | Change password with the right current password | **200** with a new pair; the new session is the only one listed, the earlier access tokens are **401**, the new pair works, and only the new password signs in |
| `AUTH_PW_02` | − | Wrong current password | **400** `AUTH_CURRENT_PASSWORD_INVALID`; the session and the old password still work |
| `AUTH_PW_03` | − | No token | **401** |
| `AUTH_SES_01` | + | Two sign-ins, list, end the older | Both listed; after `DELETE` the ended one is gone and its refresh token fails, the other still refreshes |
| `AUTH_SES_02` | − | End another user's session by id | **404**; the owner's session is untouched |
| `AUTH_SES_03` | + | List, refresh, list again | The session keeps its id and sign-in time; `isCurrent` marks it both times |
| `AUTH_SES_04` | − | End one session from another | Its access token gets **401** on the auth host at once and on the management host shortly after; the other session's token still works |
| `AUTH_SES_05` | − | Log out, then use that session's access token | **401**; a fresh sign-in works |
| `AUTH_LOCK_01` | − | Ten wrong passwords, then the right one | **401** `AUTH_INVALID_CREDENTIALS`, identical to a wrong password; sessions issued earlier still work |
| `AUTH_LOCK_02` | + | Nine wrong passwords, a success, one more wrong one | The next correct sign-in still succeeds: a success resets the count |

---

## 7. Rate limiting & transport

Run isolated — the partition key is the client IP and these will otherwise poison neighbouring tests.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_RL_01` | − | Exceed `RateLimiting:Authentication:PermitLimit` failed logins in the window | **429** once the limit is passed |
| `AUTH_RL_02` | + | After the window elapses, login works again | **200**. The window is exhausted with an unknown address, so the per-account lockout does not also apply |
| `AUTH_RL_03` | − | The limit applies to `register` | **429** |
| `AUTH_RL_04` | − | The limit applies to `refresh-token` | **429** |
| `AUTH_RL_05` | − | `logout` is limited, in the refresh bucket (`RateLimiting:TokenRefresh`) | **429** past the refresh limit |
| `AUTH_RL_06` | + | `logout-all` is **not** rate limited (no `[EnableRateLimiting]`) | No 429 — asserts the current shape so a future change is deliberate |
| `AUTH_RL_07` | + | `/healthz` is anonymous | **200** |
| `AUTH_RL_08` | − | `/metrics` without a token | Not **200** — nothing on a public router serves metrics |
| `AUTH_RL_09` | − | `/metrics` with a valid token | **404** — metrics are pushed over OTLP to the in-network collector, not scraped |
| `AUTH_RL_10` | − | Successful logins also count toward the limit | Confirms the brake cannot be bypassed by interleaving valid credentials |
| `AUTH_RL_11` | + | More logouts than the credential limit, then login | No 429 on logout, login **200**: signing out does not spend the sign-in budget |
| `AUTH_RL_12` | − | Exceed `RateLimiting:EmailVerification:PermitLimit` on `verify-email`, then login | **429** on verify-email, login **200**: its own bucket |

---

## 8. Cross-cutting authentication

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `AUTH_TK_01` | − | Any `[Authorize]` endpoint with no `Authorization` header | **401** |
| `AUTH_TK_02` | − | `Authorization: Bearer garbage` | **401** |
| `AUTH_TK_03` | − | Token signed with the wrong key | **401** |
| `AUTH_TK_04` | − | Structurally valid but expired access token | **401** |
| `AUTH_TK_05` | − | Token with the signature stripped (`alg: none`) | **401** |
| `AUTH_TK_06` | − | **A client token** (from `POST /api/client-auth/login` on the management host) used against `GET /api/workspaces` | **401**. The client token is signed by the management host's key as issuer `wbskt-management` for audience `wbskt-socket`, so it fails on issuer, audience and signature. (Before the keys were split it was refused only because a client's subject is a Guid where `IdentityMiddleware` needs an int.) |
| `AUTH_TK_07` | − | Client token against `GET /api/tenants` | **401** |
| `AUTH_TK_08` | − | `Authorization` header without the `Bearer ` prefix | **401** |
| `AUTH_TK_09` | + | Token from a *rotated* refresh still authorizes | **200** |
| `AUTH_TK_10` | − | Access token of a member suspended in one tenant, on the auth and management hosts | **403** `WORKSPACE_FORBIDDEN` for that tenant's workspaces: at once on the auth host, and on the management host as soon as the Redis announcement drops its cached access; the same token still works in the member's own tenant |

---

## 9. Workspace resolution — `POST /api/workspaces/resolve`

The cross-service contract. Every workspace-scoped management-host request passes through here.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `WS_RES_01` | + | Admin resolves their own workspace | **200**, `workspaceId > 0`, `permissions` non-empty |
| `WS_RES_02` | + | Response carries the full effective set | Contains `users.manage`, `roles.manage`, `clients.read`, … |
| `WS_RES_03` | + | A member with no roles resolves successfully with an **empty** permission array | **200** — membership and permissions are separate gates; empty is a valid resolution |
| `WS_RES_04` | + | Workspace creator resolves their new workspace | **200**, permissions include `users.manage` |
| `WS_RES_05` | − | Unknown `workspaceRef` | **403** `WORKSPACE_NOT_FOUND` (see `REG_01`) |
| `WS_RES_06` | − | Valid workspace the caller is **not** a member of | **403** `WORKSPACE_UNAUTHORIZED` |
| `WS_RES_07` | − | No `Authorization` header | **401** — must be distinguishable from `06`, or the console signs users out on a permission error |
| `WS_RES_08` | − | Malformed (non-GUID) `workspaceRef` | model-validation 400 |
| `WS_RES_09` | − | Missing body | model-validation 400 |
| `WS_RES_10` | + | Resolution reflects a permission change immediately | Grant `logs.read`, re-resolve, slug present — no stale cache |
| `WS_RES_11` | + | Revocation reflected immediately | Remove the grant, re-resolve, slug absent |
| `WS_RES_12` | + | Deny beats allow | User-level deny of a role-granted slug ⇒ slug absent from the resolved set |
| `WS_RES_13` | + | Response never leaks internal user IDs | Only `workspaceId` (documented internal) and slugs |

---

## 10. Workspace CRUD — `/api/workspaces`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `WS_CRD_01` | + | Create with name + description | **200**, non-empty `refId` |
| `WS_CRD_02` | + | Created workspace appears in `GET /api/workspaces` | Present |
| `WS_CRD_03` | + | Creator is auto-granted workspace-scoped Admin | Resolve returns a full permission set |
| `WS_CRD_04` | − | Creator is **not** a tenant admin | `GET /api/tenants/{t}/roles` → **403** `PERMISSION_UNAUTHORIZED`. The single most important authorization boundary in the product |
| `WS_CRD_05` | + | Create with `description: null` | **200** |
| `WS_CRD_06` | − | Empty name | model-validation 400 |
| `WS_CRD_07` | − | Name of 101 characters | model-validation 400 |
| `WS_CRD_08` | − | Description of 501 characters | model-validation 400 |
| `WS_CRD_09` | + | Two workspaces may share a name | **200** — no uniqueness constraint; pin the current behaviour |
| `WS_CRD_10` | − | Create without a token | **401** |
| `WS_CRD_11` | + | `GET /api/workspaces` returns **only** the caller's workspaces | User B's workspace absent from user A's list |
| `WS_CRD_12` | + | A user with no workspaces gets an empty array | **200**, `[]` — not 404 |
| `WS_CRD_13` | + | Update name and description | **204**; re-read reflects both |
| `WS_CRD_14` | − | Update by a non-member | **403** |
| `WS_CRD_15` | − | Update by a member lacking `users.manage` | **403** `PERMISSION_UNAUTHORIZED` |
| `WS_CRD_16` | − | Update with an empty name | model-validation 400 |
| `WS_CRD_17` | + | Delete a workspace | **204**; absent from `GET /api/workspaces` |
| `WS_CRD_18` | − | Delete by a member lacking `users.manage` | **403** |
| `WS_CRD_19` | − | Delete twice | Second call **403** `WORKSPACE_NOT_FOUND` (the ref no longer resolves) |
| `WS_CRD_20` | + | Delete removes it from *other* members' lists too | Member B no longer sees it |
| `WS_CRD_21` | + | `createdAt` is populated and plausible | Within a few minutes of now. Note `CreateWorkspaceAsync` returns `DateTime.UtcNow` rather than the stored value, so a strict equality check against the list endpoint will flake |

---

## 11. Workspace membership — `/api/workspaces/{ref}/members`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `WS_MEM_01` | + | Add an existing user by email | **204** |
| `WS_MEM_02` | + | Added member appears in the member list | Present |
| `WS_MEM_03` | + | Added member can now resolve the workspace | **200** with an empty permission set (member, no roles) |
| `WS_MEM_04` | + | Added member sees it in their `GET /api/workspaces` | Present |
| `WS_MEM_05` | + | Adding the same user twice | **204**, no duplicate row in the member list |
| `WS_MEM_06` | − | Add an unregistered email | **404** `USER_NOT_FOUND`. Note this doubles as a registration oracle for anyone who can create a workspace — assert current behaviour, flag separately |
| `WS_MEM_07` | − | Add by a member lacking `users.manage` | **403** `PERMISSION_UNAUTHORIZED` |
| `WS_MEM_08` | − | Add to a workspace the caller is not in | **403** |
| `WS_MEM_09` | − | Add with a malformed email | model-validation 400 |
| `WS_MEM_10` | − | Add with an unknown workspace ref | **403** `WORKSPACE_NOT_FOUND` |
| `WS_MEM_11` | + | List members | **200**, includes owner + added members, `X-Total-Count` set |
| `WS_MEM_12` | + | Paging: `take=1` | 1 item; total header unchanged |
| `WS_MEM_13` | + | `take=0` is clamped to 1, not an error | **200**, 1 item |
| `WS_MEM_14` | + | `take=99999` is clamped to 200 | **200**, ≤ 200 items |
| `WS_MEM_15` | + | `skip=-5` is treated as 0 | **200** |
| `WS_MEM_16` | + | `skip` beyond the end | **200**, empty `items`, `X-Total-Count` still the real total |
| `WS_MEM_17` | − | List by a member lacking `users.read` | **403** |
| `WS_MEM_18` | + | Remove a member | **204**; gone from the list; they can no longer resolve |
| `WS_MEM_19` | − | Remove the **owner** | **400** `WORKSPACE_OWNER_PROTECTED` — a workspace with no owner cannot be administered |
| `WS_MEM_20` | − | Remove an unknown `userRef` | **403** `USER_NOT_FOUND` |
| `WS_MEM_21` | − | Remove by a member lacking `users.manage` | **403** |
| `WS_MEM_22` | + | Removal also drops assignments scoped to that workspace | Re-add the user; their old workspace-scoped role is gone |
| `WS_MEM_23` | + | Members list exposes `refId` only, never the integer `Id` | Response has no `id` field |

---

## 11a. Workspace access — invitations with workspaces, ownership, User role

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `INV_WS_01` | + | Invite naming one of two workspaces, register with it | The invitee resolves the named workspace (**200**) and not the other (**403**) |
| `INV_WS_02` | − | Invite naming another tenant's workspace | **403** `WORKSPACE_NOT_FOUND`, the same as an unknown ref |
| `INV_WS_03` | + | Invite with the `User` role and a workspace | The invitee's effective permissions there include the five `*.read` slugs and no `manage`/`create` |
| `WS_OWN_01` | + | Transfer to a tenant member outside the workspace | **204**; they can resolve it; removing them is now refused and removing the previous owner succeeds |
| `WS_OWN_02` | − | Transfer to someone outside the tenant | **404** `USER_NOT_FOUND`; they gain nothing |
| `WS_OWN_03` | − | Transfer by a member without `users.manage` | **403** |

---

## 11b. Devices — delete, rotate, bulk status (management host)

Covered by `Scenarios/Devices/DeviceLifecycleTests.cs`; listed here so device lifecycle sits beside the
workspace scenarios it depends on.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `DEV_DEL_01` | + | Delete a registered device | **204**; its secret no longer signs in (**401**) and its detail is **404** |
| `DEV_DEL_02` | − | Delete another workspace's device | **403** `CLIENT_UNAUTHORIZED`; the device still signs in |
| `DEV_SEC_01` | + | Rotate a device's secret | **200** with a new secret; the old one is **401**, the new one signs in |
| `DEV_PIN_01` | + | Rotate a policy's PIN | **200** with a new PIN; the old PIN no longer registers, the new one does, and existing devices still sign in |
| `DEV_BULK_01` | + | Approve two pending devices and an unknown ref | **200**; both approved, the unknown ref reported as `CLIENT_UNAUTHORIZED` |
| `DEV_BULK_02` | − | Approve two pending devices under a one-device policy | The first is approved, the second reported as `POLICY_LIMIT_REACHED` |
| `DEV_BULK_03` | − | Empty batch, or more than 100 | **400** |

---

## 11c. Workflows — delete, versions, webhook idempotency (management host)

Covered by `Scenarios/Workflows/WorkflowLifecycleTests.cs`.

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `WF_VER_01` | + | Publish twice, list versions | **200**; version 2 `Published`, version 1 `Superseded` |
| `WF_DEL_01` | + | Delete a workflow | **204**; current, versions and a second delete are **404**; it is not listed; republishing its RefId is **409** `WORKFLOW_DELETED` |
| `WF_DEL_02` | − | Delete another workspace's workflow | **404**; the owner still reads it |
| `WF_IDEM_01` | + | Send one webhook three times with the same `Idempotency-Key`, then once with another | One run for the repeated key, a second for the new one |
| `WF_IDEM_02` | − | `Idempotency-Key` longer than 255 characters | **400** |

---

## 12. Tenant administration — roles, groups, permissions

All under `/api/tenants/{tenantRef}`, all requiring the permission **tenant-wide**. Run as the seeded
admin unless stated.

### 12.1 Tenant listing

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `TEN_01` | + | `GET /api/tenants` as admin | **200**, includes Default Tenant |
| `TEN_02` | + | `GET /api/tenants` as a brand-new user | **200**, also includes Default Tenant (registration joins tenant 1) — no permission gate on this endpoint |
| `TEN_03` | − | Without a token | **401** |
| `TEN_04` | + | Response carries `refId` + `name` only | No internal `id` |

### 12.2 Roles

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `ROLE_01` | + | Create a role | **200** with `refId`, echoed name/description |
| `ROLE_02` | + | Created role appears in the list | Present; `X-Total-Count` incremented |
| `ROLE_03` | + | Create with a null description | **200** |
| `ROLE_04` | − | Duplicate name in the same tenant | **409** `AUTH_ROLE_CONFLICT` |
| `ROLE_05` | − | Empty name | model-validation 400 |
| `ROLE_06` | − | Name of 101 characters | model-validation 400 |
| `ROLE_07` | − | Description of 256 characters | model-validation 400 |
| `ROLE_08` | + | Rename a role | **204**; list reflects it |
| `ROLE_09` | − | Rename onto an existing name | **409** `AUTH_ROLE_CONFLICT` |
| `ROLE_10` | − | Update an unknown `roleRef` | **403** `ROLE_NOT_FOUND` |
| `ROLE_11` | + | Delete a role | **204**; absent from the list |
| `ROLE_12` | + | Deleting a role removes it from everyone who held it | A user assigned it loses the derived permissions on re-resolve |
| `ROLE_13` | − | Delete an unknown `roleRef` | **403** `ROLE_NOT_FOUND` |
| `ROLE_14` | − | Any role operation without `roles.manage` | **403** `PERMISSION_UNAUTHORIZED` |
| `ROLE_15` | − | List roles without `roles.read` | **403** |
| `ROLE_16` | − | Any role route with an unknown `tenantRef` | **403** `TENANT_NOT_FOUND` |
| `ROLE_17` | − | A user holding `roles.manage` **workspace-scoped only** | **403** — management operations require the permission tenant-wide |
| `ROLE_18` | − | Non-GUID `tenantRef` in the route | **404** from routing (the `:guid` constraint rejects it before the action) |

### 12.3 Role permissions

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `RPERM_01` | + | Grant a valid slug | **204**; appears in the role's permission list |
| `RPERM_02` | + | Grant with `isDeny: true` | **204**; listed with `isDeny: true` |
| `RPERM_03` | + | Re-grant the same slug flipping `isDeny` | **204**; the row is updated, not duplicated |
| `RPERM_04` | − | Unknown slug | **400** `AUTH_OPERATION_REJECTED` (`REG_07`) |
| `RPERM_05` | − | Body containing `workspaceRef` | model-validation 400 (`REG_11`) |
| `RPERM_06` | − | Empty slug | model-validation 400 |
| `RPERM_07` | − | Slug of 101 characters | model-validation 400 |
| `RPERM_08` | + | Remove a granted permission | **204**; absent from the list |
| `RPERM_09` | + | Remove a permission that was never granted | **204** — delete is idempotent |
| `RPERM_10` | + | Removing is not the same as denying | After remove, the effective set falls back to other roles; after deny, it does not |
| `RPERM_11` | − | Grant/remove without `roles.manage` | **403** |
| `RPERM_12` | + | A granted role permission reaches the effective set | Assign the role to a user → resolve shows the slug |
| `RPERM_13` | + | Role permissions are unscoped | The same role assigned in two workspaces yields the slug in both |

### 12.4 Groups

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `GRP_01` | + | Create a top-level group | **200** with `refId`, `parentGroupRefId: null` |
| `GRP_02` | + | Create a nested group | **200**, `parentGroupRefId` echoes the parent |
| `GRP_03` | + | Created group appears in the list | Present |
| `GRP_04` | − | Duplicate name in the tenant | **409** `AUTH_GROUP_CONFLICT` |
| `GRP_05` | − | Unknown `parentGroupRef` | **403** `GROUP_NOT_FOUND` |
| `GRP_06` | − | Empty name | model-validation 400 |
| `GRP_07` | + | Rename a group | **204** |
| `GRP_08` | − | Rename onto an existing name | **409** |
| `GRP_09` | + | Delete a leaf group | **204** |
| `GRP_10` | − | Delete a group that has children | **400** `AUTH_OPERATION_REJECTED` ("Group has child groups") |
| `GRP_11` | + | Delete the child, then the parent | Both **204** |
| `GRP_12` | − | Delete an unknown `groupRef` | **403** `GROUP_NOT_FOUND` |
| `GRP_13` | − | Any group operation without `users.manage` | **403** |
| `GRP_14` | − | List groups without `users.read` | **403** |

### 12.5 Group role assignments

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `GRL_01` | + | Assign a role to a group, tenant-wide (`workspaceRef: null`) | **204** |
| `GRL_02` | + | Assign scoped to a workspace | **204**; listed with that `workspaceRef` |
| `GRL_03` | + | Assignment appears in `GET .../groups/{ref}/roles` | Present with the right scope |
| `GRL_04` | + | **A group member inherits the role's permissions** | Add user to group → resolve shows the slugs |
| `GRL_05` | + | Inheritance follows the parent chain | Role on the parent group reaches a member of the child group |
| `GRL_06` | + | Workspace-scoped group role applies only there | Slug present in workspace A, absent in workspace B |
| `GRL_07` | − | Assign with an unknown `roleRef` | **403** `ROLE_NOT_FOUND` |
| `GRL_08` | − | Assign with an unknown `groupRef` | **403** `GROUP_NOT_FOUND` |
| `GRL_09` | − | Assign with an unknown `workspaceRef` | **403** `WORKSPACE_NOT_FOUND` |
| `GRL_10` | − | Assign with **no request body** | model-validation 400 — the body is currently mandatory even for a tenant-wide assignment |
| `GRL_11` | + | Assign the same role twice | **204**, idempotent, no duplicate |
| `GRL_12` | + | Remove the assignment (`?workspaceRef=` on the query string) | **204**; members lose the permissions |
| `GRL_13` | + | Removing a workspace-scoped assignment leaves the tenant-wide one intact | Scope is part of the identity of an assignment |
| `GRL_14` | + | Remove a non-existent assignment | **204**, idempotent |
| `GRL_15` | − | Assign/remove without `roles.manage` | **403** |

---

## 13. Tenant members — `/api/tenants/{t}/members`

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `MEM_01` | + | List members | **200**, includes root, `X-Total-Count` set |
| `MEM_02` | + | `?search=` matches on username/email | Filtered results |
| `MEM_03` | + | `search` with no matches | **200**, empty `items` |
| `MEM_04` | + | Paging combines with `search` | `X-Total-Count` reflects the **filtered** total |
| `MEM_05` | − | List without `users.read` | **403** |
| `MEM_06` | + | List a user's roles | **200**, scope on each entry |
| `MEM_07` | + | List a user's direct permissions | **200** with `slug`, `isDeny`, `workspaceRef` |
| `MEM_08` | + | List a user's groups | **200** |
| `MEM_09` | − | Any of `06`–`08` with an unknown `userRef` | **403** `USER_NOT_FOUND` |
| `MEM_10` | − | `06`/`07` without `roles.read` | **403** |
| `MEM_11` | − | `08` without `users.read` | **403** |
| `MEM_12` | + | Add a user to a group | **204**; appears in their groups |
| `MEM_13` | + | Group membership grants the group's roles | Effective set gains the slugs |
| `MEM_14` | + | Add to the same group twice | **204**, idempotent |
| `MEM_15` | − | Add with an unknown `groupRef` | **403** `GROUP_NOT_FOUND` |
| `MEM_16` | + | Remove from a group | **204**; permissions lost |
| `MEM_17` | + | Remove when not a member | **204**, idempotent |
| `MEM_18` | + | Assign a role tenant-wide | **204**; applies in every workspace of the tenant |
| `MEM_19` | + | Assign a role workspace-scoped | **204**; applies only there |
| `MEM_20` | − | Assign with an unknown `roleRef` | **403** `ROLE_NOT_FOUND` |
| `MEM_21` | − | Assign with a `workspaceRef` from another tenant | **400** `AUTH_OPERATION_REJECTED` (SP guard 50002) |
| `MEM_22` | + | Remove a role assignment | **204**; permissions lost |
| `MEM_23` | + | Tenant-wide and workspace-scoped assignments of the same role coexist | Removing one leaves the other |
| `MEM_24` | + | Grant a direct user permission | **204**; visible in their permission list; in the effective set |
| `MEM_25` | + | Grant a direct **deny** | **204**; slug **absent** from the effective set even though a role allows it |
| `MEM_26` | + | Direct grant scoped to a workspace | Present there, absent elsewhere |
| `MEM_27` | − | Grant an unknown slug | **400** (`REG_09`) |
| `MEM_28` | + | Remove a direct permission | **204**; the decision reverts to the user's roles |
| `MEM_29` | + | Remove is scope-sensitive | Removing the workspace-scoped row leaves the tenant-wide one |
| `MEM_30` | − | Grant/remove without `roles.manage` | **403** |
| `MEM_31` | + | Suspend a member | **204**; their existing token is refused (**403**) in that tenant's workspaces, which also leave their workspace and tenant lists |
| `MEM_32` | + | Suspension leaves the account alone | Login and refresh still work; only the suspending tenant disappears from their list |
| `MEM_33` | + | Lift the suspension | **204**; access returns exactly as it was |
| `MEM_34` | + | Suspension is visible in the member list | `isSuspended: true`, `isActive: true` |
| `MEM_35` | − | Suspend without `users.manage` | **403** |
| `MEM_36` | − | Suspend an unknown `userRef` | **403** `USER_NOT_FOUND` |
| `MEM_37` | − | An administrator suspends themselves | **400** `AUTH_CANNOT_SUSPEND_SELF`. The database also refuses any suspension that leaves the tenant with no administrator (50008). `MEM_37b`: the old `/active` route is gone |

---

## 14. Permission precedence — the effective set

Exercised through `POST /api/workspaces/resolve`. Order per `Permission_EffectiveSet`:
**User-Deny > User-Allow > Role-Deny > Role-Allow > default deny.**

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `PREC_01` | + | No assignments at all | Slug absent (default deny) |
| `PREC_02` | + | Role allow only | Present |
| `PREC_03` | + | Role deny only | Absent |
| `PREC_04` | + | Role allow + role deny (two roles) | **Absent** — role deny wins |
| `PREC_05` | + | User allow over a role deny | **Present** — user level outranks role level |
| `PREC_06` | + | User deny over a role allow | **Absent** |
| `PREC_07` | + | User deny over a user allow | **Absent** — deny wins within a level |
| `PREC_08` | + | Tenant-wide role applies in every workspace | Present in two workspaces |
| `PREC_09` | + | Workspace-scoped role applies only in its workspace | Present in A, absent in B |
| `PREC_10` | + | Group-derived role behaves identically to a direct one | Same result |
| `PREC_11` | + | Nested-group inheritance | Parent group's role reaches the child's member |
| `PREC_12` | + | An assignment from another tenant never applies | Requires the seeded second tenant |
| `PREC_13` | + | Removing the last source removes the slug | Present → remove → absent |

---

## 15. Cross-tenant isolation

**No longer blocked.** Tenants are self-serve, so two registrations give two isolated tenants with no
seeding required — `ServicesFixture.CreateUserAsync()` twice is the whole arrangement.

Largely covered already by `Scenarios/Auth/TenantLifecycleTests.cs` (`TEN_01`–`TEN_20`), which owns
the tenant and invitation surface. The rows below that it does not reach are the *addressability*
cases — using a reference from tenant B under tenant A's route — and they remain worth writing.

`ISO_09` has been resolved rather than documented: adding a workspace member now requires the target
to already belong to the workspace's tenant, so the cross-boundary capture it described is closed
(`WS_MEM_06b`).

| ID | ± | Scenario | Expected |
|---|---|---|---|
| `ISO_01` | − | Tenant-A admin lists tenant B's roles | **403** `TENANT_NOT_FOUND` |
| `ISO_02` | − | Tenant-A admin addresses a tenant-B `roleRef` under tenant A's route | **403** `ROLE_NOT_FOUND` |
| `ISO_03` | − | Same for a `groupRef` | **403** `GROUP_NOT_FOUND` |
| `ISO_04` | − | Same for a `userRef` | **403** `USER_NOT_FOUND` — an admin must not pull an outside user into their permission graph |
| `ISO_05` | − | Assign a tenant-B workspace as the scope of a tenant-A assignment | **400** `AUTH_OPERATION_REJECTED` (SP guard 50002) |
| `ISO_06` | − | Grant a tenant-B user a permission via tenant A's route | **403** `USER_NOT_FOUND` |
| `ISO_07` | − | Resolve a tenant-B workspace as a tenant-A user | **403** `WORKSPACE_UNAUTHORIZED` |
| `ISO_08` | − | `GET /api/tenants` shows only the caller's tenants | Tenant B absent |
| `ISO_09` | − | **`POST /api/workspaces/{a}/members` with a tenant-B user's email** | Currently **204** — `WorkspaceMember_Add` deliberately adds the user to the owning tenant. This is the one path that crosses the boundary `ISO_04` protects. Assert the behaviour as it stands and treat the design question separately |

---

## 16. Suggested implementation order

1. **§2 regression** — ✅ implemented in `Scenarios/Auth/AuthRegressionTests.cs`. `REG_07`–`REG_10`
   assert behaviour that lives in the stored procedures, so they need the corrected database
   deployed, not just the host rebuilt.
2. **§5 refresh/rotation** — ✅ implemented in `Scenarios/Auth/TokenRotationTests.cs`.
   `AUTH_RT_15` found a real defect (one refresh token could be exchanged twice concurrently) and
   the rotation now claims the token before minting; the test is the regression guard for it.
   `AUTH_RT_14` skips pending clock control.
3. **§8** — ✅ implemented in `Scenarios/Auth/TokenAcceptanceTests.cs`, except `AUTH_TK_04`
   (expired access token — needs clock control) and `AUTH_TK_09` (already covered by `AUTH_RT_02`).
4. **§9 resolve** — ✅ implemented in `Scenarios/Auth/WorkspaceResolutionTests.cs`. The cross-service
   contract, including that 401 and 403 stay distinguishable and that a grant is visible on the very
   next resolve.
5. **§10 workspace CRUD** — ✅ implemented in `Scenarios/Auth/WorkspaceLifecycleTests.cs`, including
   `WS_CRD_04`, the workspace-admin vs tenant-admin boundary.
6. **§11 workspace membership** — ✅ implemented in `Scenarios/Auth/WorkspaceMembershipTests.cs`.
7. **Tenants and invitations** — ✅ `Scenarios/Auth/TenantLifecycleTests.cs` (`TEN_01`–`TEN_20`),
   which also absorbs most of §12.1 and §15.
8. **§3, §4, §6** — broad but shallow; cheap now that the fixture helpers exist. Note `§4` needs
   revising first: login no longer validates the identifier's format, so `AUTH_LOG_10` is stale.
9. **§12.2–12.3 roles and role permissions** — ✅ `Scenarios/Auth/TenantRoleTests.cs`.
10. **§12.4–12.5 groups and group role assignments** — ✅ `Scenarios/Auth/TenantGroupTests.cs`.
11. **§13 member administration** — ✅ `Scenarios/Auth/TenantMemberTests.cs`. Member *removal* is
    not repeated here; `TEN_13`–`TEN_16` own offboarding and its workspace-transfer rules.
    One ID drifted from the catalogue: `MEM_37` here asserts that deactivation spans every tenant
    the account belongs to, which is worth having but is not the row §13 defines. The catalogue's
    `MEM_37` — an administrator deactivating *themselves* and stranding the tenant — is
    `MEM_37b`.
12. **§14 precedence** — ✅ `Scenarios/Auth/PermissionPrecedenceTests.cs`, covering the arms not
    already hit in passing by `WS_RES_12`, `MEM_25`, `GRL_05` and `MEM_18`/`MEM_19`.
13. **§3 registration** — ✅ `Scenarios/Auth/RegistrationTests.cs`, extended with the invitation-token
    rejections (`AUTH_REG_22`–`24`) the original catalogue predated.
14. **§4 login** — ✅ `Scenarios/Auth/LoginTests.cs`. `AUTH_LOG_10` was rewritten: `LoginRequest`
    deliberately dropped `[EmailAddress]`, so a malformed identifier now returns the same **401** as
    any other failed login rather than a 400. The test asserts it matches the wrong-password
    response instead of hardcoding a status.
15. **§6 logout** — ✅ `Scenarios/Auth/SessionTerminationTests.cs`.
16. **§7 rate limiting** — ✅ `Scenarios/Auth/RateLimitingTests.cs`, **opt-in**. The limiter
    partitions on caller IP, which the whole suite shares, so these are gated behind
    `E2E_RATE_LIMIT_TESTS=1` and meant to be run with a filter. `AUTH_RL_02` waits out the full
    window, so it is slow by nature. `E2E_AUTH_PERMIT_LIMIT` and `E2E_AUTH_WINDOW_MINUTES` must
    match the host's configuration.

### Coverage

Every section now has an implementation, and every scenario ID in §12.2–12.5 and §13 has a test.

What remains open is not scenarios but verification, in two separate senses.

**Compilation is no longer the gap, but it is still not automatic.** The suite builds clean under
the .NET 10 SDK (`dotnet build Tests/Wbskt.E2E.FeatureTests`), and all scenarios are discovered by
the runner. But nothing in `Tests/` is built by CI — `build-images.yml` builds only the four host
images, and no host references the test project — so a scenario that stops compiling still breaks
nobody's build. Until a host references it or CI builds it explicitly, that `dotnet build` is a
manual step.

**Nothing here has run green against a live stack.** Every scenario gates on
`Skip.IfNot(fixture.HostsAvailable, …)` and skips when the four dev hosts are down, so a full run
with no hosts reports success while asserting nothing. A ✅ in the list above means "written and
compiling", not "passing".

### Known mismatch

`Docs/API.Endpoints.md` says `PUT {tenantRef}/groups/{groupRef}` "renames a group **or re-parents
it**". `UpdateGroupRequest` carries only `Name`, so re-parenting is not reachable through the API.
Either the doc is ahead of the code or the property was dropped; no scenario asserts it until that
is settled.
