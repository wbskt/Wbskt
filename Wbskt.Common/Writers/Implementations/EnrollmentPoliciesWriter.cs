using System.Data;
using System.Data.SqlClient;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class EnrollmentPoliciesWriter(IConnectionStringProvider connectionStringProvider) : IEnrollmentPoliciesWriter
{
    public int InsertPolicy(EnrollmentPolicyRecord record)
    {
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        using var command = new SqlCommand("dbo.EnrollmentPolicies_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@UserId", record.UserId);
        command.Parameters.AddWithValue("@PolicyRef", record.PolicyRef);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@PolicyType", (int)record.PolicyType);

        if (record.MaxClients.HasValue)
        {
            command.Parameters.AddWithValue("@MaxClients", record.MaxClients.Value);
        }
        else
        {
            command.Parameters.AddWithValue("@MaxClients", DBNull.Value);
        }

        if (record.ExpiryDate.HasValue)
        {
            command.Parameters.AddWithValue("@ExpiryDate", record.ExpiryDate.Value);
        }
        else
        {
            command.Parameters.AddWithValue("@ExpiryDate", DBNull.Value);
        }

        var idParameter = new SqlParameter("@Id", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(idParameter);

        connection.Open();
        command.ExecuteNonQuery();

        return (int)idParameter.Value;
    }

    public void IncrementUsage(int policyId)
    {
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        using var command = new SqlCommand("dbo.EnrollmentPolicies_IncrementUsage", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", policyId);

        connection.Open();
        command.ExecuteNonQuery();
    }
}
