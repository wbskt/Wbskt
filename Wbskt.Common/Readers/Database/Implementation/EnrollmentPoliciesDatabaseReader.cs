using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class EnrollmentPoliciesDatabaseReader(ILogger<EnrollmentPoliciesDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IEnrollmentPoliciesDatabaseReader
{
    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll(DateTime lastModified)
    {
        var policies = new List<EnrollmentPolicyReadRecord>();

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        using var command = new SqlCommand("dbo.EnrollmentPolicies_GetAll", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@LastModified", lastModified);

        connection.Open();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            policies.Add(MapToRecord(reader));
        }

        return policies;
    }

    internal void RegisterSqlDependency(OnChangeEventHandler onDatabaseChange)
    {
        try
        {
            using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
            using var command = new SqlCommand("SELECT LastModified FROM dbo.EnrollmentPolicies", connection);

            var dependency = new SqlDependency(command);
            dependency.OnChange += onDatabaseChange;

            connection.Open();
            using var reader = command.ExecuteReader();
        }
        catch (SqlException sex)
        {
            logger.LogError(sex, "failed to register SQL dependency. {message}", sex.Message);
        }
    }

    private static EnrollmentPolicyReadRecord MapToRecord(SqlDataReader reader)
    {
        return new EnrollmentPolicyReadRecord
        {
            Id = reader.GetInt32("Id"),
            UserId = reader.GetInt32("UserId"),
            PolicyRef = reader.GetGuid("PolicyRef"),
            Name = reader.GetString("Name"),
            PolicyType = (EnrollmentPolicyType)reader.GetInt32("PolicyType"),
            MaxClients = reader.IsDBNull("MaxClients") ? null : reader.GetInt32("MaxClients"),
            ExpiryDate = reader.IsDBNull("ExpiryDate") ? null : reader.GetDateTime("ExpiryDate"),
            CurrentUsage = reader.GetInt32("CurrentUsage"),
            IsActive = reader.GetBoolean("IsActive"),
            LastModified = reader.GetDateTime("LastModified")
        };
    }
}
