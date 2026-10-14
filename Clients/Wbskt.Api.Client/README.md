# Wbskt.Api.Client

Typed .NET clients for the two public HTTP hosts, built on the contract packages the hosts
themselves compile against (`Wbskt.Auth.Models`, `Wbskt.Management.Models`, `Wbskt.Models`,
`Wbskt.Protocol`). No dependencies beyond the framework: `HttpClient` and `System.Net.Http.Json`.

| Client                  | Host            | Covers                                                                              |
|-------------------------|-----------------|-------------------------------------------------------------------------------------|
| `WbsktAuthClient`       | auth host       | accounts, sessions, workspaces, tenants, members, invitations, roles, groups         |
| `WbsktManagementClient` | management host | policies, devices, clients, readings, commands, templates, event log, integrations, workflows, runs, public callbacks |

## Use

```csharp
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var auth = new WbsktAuthClient(http, new Uri("https://auth.example.com"));
var login = await auth.LoginAsync(new LoginRequest("me@example.com", "a long password"));

var management = new WbsktManagementClient(http, new Uri("https://api.example.com"), login.AccessToken);
var workspaces = await auth.As(login.AccessToken).GetWorkspacesAsync();
var policies = await management.GetPoliciesAsync(workspaces.First().RefId);
```

* One `HttpClient` serves both clients; pass the host's address, or set it as the
  `HttpClient.BaseAddress`.
* The bearer token is `AccessToken` (settable), or per call with `client.As(token)`, which returns
  a sibling over the same `HttpClient`. `As(null)` calls anonymously.
* Every endpoint has two methods. `XAsync` returns the contract type and throws `ApiException` on a
  failure status. `XRawAsync` returns the `HttpResponseMessage` untouched, for a caller that asserts
  on the status, the error code or a header. `SendRawAsync` sends anything, including bodies the
  contract does not allow.
* `ApiException` carries the `HttpStatusCode` and the parsed Problem Details as `ApiProblem`
  (`Status`, `Title`, `Detail`, `Code`, `TraceId`, and `Errors` for a validation failure). Branch on
  `Code` (`AUTH_ROLE_CONFLICT`, `CLIENT_NOT_FOUND`, ...), not on the message. `ApiProblem.ReadAsync`
  reads one off a raw response.
* Lists return `Page<T>`; pass its `NextCursor` back as `cursor` for the next page.
* Request bodies are written with web defaults (enums as numbers, as the hosts read them);
  responses are read accepting an enum as a name or a number.

The E2E suite (`Tests/Wbskt.E2E.FeatureTests`) is the first consumer: its `ServicesFixture` composes
the two clients and adds only arrangement helpers on top.
