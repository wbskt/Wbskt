using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wbskt.Infrastructure.HealthChecks;

/// <summary>
/// Opens a connection and runs <c>SELECT 1</c>. Hand-rolled rather than taken from a health-check
/// package: it is a dozen lines against <c>Microsoft.Data.SqlClient</c>, which every host already
/// references, and this repository pins its dependencies carefully enough that one fewer is worth
/// more than the convenience.
///
/// The probe carries its own short timeouts, independent of whatever the application connection
/// string asks for. A readiness endpoint that hangs is worse than one that fails: the caller —
/// Docker's healthcheck, and through it Traefik's routing table — gets no answer at all rather than
/// a definite "not ready", and simply waits.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    private readonly int _timeoutSeconds;

    public SqlServerHealthCheck(IConfiguration configuration, string connectionStringName, int timeoutSeconds = 3)
    {
        string configured = configuration.GetConnectionString(connectionStringName)
                            ?? throw new InvalidOperationException(
                                $"Connection string '{connectionStringName}' is not configured.");

        // Override the connect timeout rather than inheriting the application's, which is tuned for
        // real work and is typically far longer than a health check should ever wait.
        _connectionString = new SqlConnectionStringBuilder(configured)
        {
            ConnectTimeout = timeoutSeconds
        }.ConnectionString;

        _timeoutSeconds = timeoutSeconds;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(timeout.Token);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = _timeoutSeconds;

            await command.ExecuteScalarAsync(timeout.Token);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"SQL Server did not respond within {_timeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            // The message reaches an endpoint that is anonymous by necessity, so it names the failure
            // without echoing the connection string or server details back to the caller.
            return HealthCheckResult.Unhealthy("SQL Server is not reachable.", ex);
        }
    }
}
