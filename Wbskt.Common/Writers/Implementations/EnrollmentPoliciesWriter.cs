using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Extensions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class EnrollmentPoliciesWriter(ILogger<EnrollmentPoliciesWriter> logger, IConnectionStringProvider connectionStringProvider) : IEnrollmentPoliciesWriter
{
    public int InsertPolicy(EnrollmentPolicyRecord record)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(InsertPolicy));
        ArgumentNullException.ThrowIfNull(record);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.EnrollmentPolicies_Insert";

        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(record.UserId)));
        command.Parameters.Add(new SqlParameter("@PolicyRef", ProviderExtensions.ReplaceDbNulls(record.PolicyRef)));
        command.Parameters.Add(new SqlParameter("@Name", ProviderExtensions.ReplaceDbNulls(record.Name)));
        command.Parameters.Add(new SqlParameter("@PolicyType", ProviderExtensions.ReplaceDbNulls((int)record.PolicyType)));
        command.Parameters.Add(new SqlParameter("@MaxClients", ProviderExtensions.ReplaceDbNulls(record.MaxClients)));
        command.Parameters.Add(new SqlParameter("@ExpiryDate", ProviderExtensions.ReplaceDbNulls(record.ExpiryDate)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        command.ExecuteNonQuery();

        return (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }

    public void IncrementUsage(int policyId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(IncrementUsage));

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.EnrollmentPolicies_IncrementUsage";

        command.Parameters.Add(new SqlParameter("@Id", policyId));
        command.ExecuteNonQuery();
    }
}
