using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class EnrollmentPoliciesDatabaseReader(ILogger<EnrollmentPoliciesDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IEnrollmentPoliciesDatabaseReader
{
    public IReadOnlyCollection<EnrollmentPolicyReadRecord> GetAll(DateTime lastModified)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.EnrollmentPolicies_GetAll";

        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<EnrollmentPolicyReadRecord>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result.AsReadOnly();
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

    private static EnrollmentPolicyReadRecord ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        return new EnrollmentPolicyReadRecord
        {
            Id = reader.GetInt32(mapping.Id),
            UserId = reader.GetInt32(mapping.UserId),
            PolicyRef = reader.GetGuid(mapping.PolicyRef),
            Name = reader.GetString(mapping.Name),
            PolicyType = (EnrollmentPolicyType)reader.GetInt32(mapping.PolicyType),
            MaxClients = reader.IsDBNull(mapping.MaxClients) ? null : reader.GetInt32(mapping.MaxClients),
            ExpiryDate = reader.IsDBNull(mapping.ExpiryDate) ? null : reader.GetDateTime(mapping.ExpiryDate),
            CurrentUsage = reader.GetInt32(mapping.CurrentUsage),
            IsActive = reader.GetBoolean(mapping.IsActive),
            LastModified = reader.GetDateTime(mapping.LastModified)
        };
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        return new OrdinalColumnMapping
        {
            Id = reader.GetOrdinal("Id"),
            UserId = reader.GetOrdinal("UserId"),
            PolicyRef = reader.GetOrdinal("PolicyRef"),
            Name = reader.GetOrdinal("Name"),
            PolicyType = reader.GetOrdinal("PolicyType"),
            MaxClients = reader.GetOrdinal("MaxClients"),
            ExpiryDate = reader.GetOrdinal("ExpiryDate"),
            CurrentUsage = reader.GetOrdinal("CurrentUsage"),
            IsActive = reader.GetOrdinal("IsActive"),
            LastModified = reader.GetOrdinal("LastModified")
        };
    }

    private class OrdinalColumnMapping
    {
        public int CurrentUsage;
        public int ExpiryDate;
        public int Id;
        public int IsActive;
        public int LastModified;
        public int MaxClients;
        public int Name;
        public int PolicyRef;
        public int PolicyType;
        public int UserId;
    }
}
