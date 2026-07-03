using System.Diagnostics.Metrics;

namespace Wbskt.Auth.Host.Telemetry;

public sealed class AuthMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Auth";
    private readonly Meter _meter;

    public AuthMetrics()
    {
        _meter = new Meter(MeterName);
        
        LoginRequests = _meter.CreateCounter<long>("wbskt_auth_login_requests_total");
        RefreshRequests = _meter.CreateCounter<long>("wbskt_auth_refresh_requests_total");
        Registrations = _meter.CreateCounter<long>("wbskt_auth_registrations_total");
        
        WorkspaceResolutions = _meter.CreateCounter<long>("wbskt_auth_workspace_resolutions_total");
        PermissionChecks = _meter.CreateCounter<long>("wbskt_auth_permission_checks_total");
        
        PasswordHashDuration = _meter.CreateHistogram<double>("wbskt_auth_password_hash_duration_ms");
    }

    public Counter<long> LoginRequests { get; }
    public Counter<long> RefreshRequests { get; }
    public Counter<long> Registrations { get; }
    public Counter<long> WorkspaceResolutions { get; }
    public Counter<long> PermissionChecks { get; }
    public Histogram<double> PasswordHashDuration { get; }

    public void RecordLogin(string outcome) => 
        LoginRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordRefresh(string outcome) => 
        RefreshRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordRegistration(string outcome) => 
        Registrations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordWorkspaceResolution(string workspaceRef, string outcome) => 
        WorkspaceResolutions.Add(1, 
            new KeyValuePair<string, object?>("workspace_ref", workspaceRef), 
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordPermissionCheck(string permissionSlug, string outcome) => 
        PermissionChecks.Add(1, 
            new KeyValuePair<string, object?>("permission_slug", permissionSlug), 
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordPasswordHash(double durationMs) => 
        PasswordHashDuration.Record(durationMs);

    public void Dispose() => _meter.Dispose();
}
