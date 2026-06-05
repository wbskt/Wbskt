namespace Wbskt.E2E.FeatureTests;

/// <summary>
/// Central source of E2E base URLs. Each value is overridable via an environment variable
/// so CI/CD environments can point at non-default hosts without recompiling.
/// </summary>
internal static class E2EConfig
{
    public static string AuthBaseUrl =>
        Environment.GetEnvironmentVariable("E2E_AUTH_URL") ?? "https://localhost:7000";

    public static string ManagementBaseUrl =>
        Environment.GetEnvironmentVariable("E2E_MANAGEMENT_URL") ?? "https://localhost:7010";

    public static string SocketHttpBaseUrl =>
        Environment.GetEnvironmentVariable("E2E_SOCKET_URL") ?? "https://localhost:7020";

    public static string SocketWsBaseUrl =>
        Environment.GetEnvironmentVariable("E2E_SOCKET_WS_URL") ?? "ws://localhost:5020";

    public static string WorkflowBaseUrl =>
        Environment.GetEnvironmentVariable("E2E_WORKFLOW_URL") ?? "https://localhost:7030";

    // Seeded administrator (Databases/Wbskt.Database.Auth/Scripts/Script.PostDeployment.sql):
    // user 'root' / admin@wbskt.com / Password123!, Admin role (all permissions),
    // owner of the Default Workspace (internal Id = 1).
    public static string AdminEmail =>
        Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL") ?? "admin@wbskt.com";

    public static string AdminPassword =>
        Environment.GetEnvironmentVariable("E2E_ADMIN_PASSWORD") ?? "Password123!";
}
