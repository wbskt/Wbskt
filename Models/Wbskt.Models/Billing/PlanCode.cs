using System.Text.Json.Serialization;

namespace Wbskt.Models.Billing;

/// <summary>
/// The plan a tenant is on. The auth host owns it (<c>dbo.Tenants.PlanCode</c>); the main database
/// keeps a copy per workspace (<c>dbo.WorkspaceAccounts</c>) for the hosts that enforce its limits.
/// Stored and sent by name, so adding a plan never renumbers the others. A new plan also needs its
/// limits in <c>PlanCatalog.BuiltIn</c> and its name in <c>CK_Tenants_PlanCode</c> (auth database) and
/// <c>CK_WorkspaceAccounts_PlanCode</c> (main database).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanCode>))]
public enum PlanCode
{
    Free,
    Maker,
    Pro,
    Business
}
