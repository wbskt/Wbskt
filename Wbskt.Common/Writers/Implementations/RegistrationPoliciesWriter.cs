using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Events;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.EventBus;

namespace Wbskt.Common.Writers.Implementations;

/// <summary>
/// Concrete implementation for writing registration policy data to the database.
/// </summary>
internal sealed class RegistrationPoliciesWriter : IRegistrationPoliciesWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IEventBus _eventBus;
    private readonly IRegistrationPoliciesReader _policiesReader;
    private readonly ICurrentUser _currentUser;

    public RegistrationPoliciesWriter(IConnectionStringProvider connectionStringProvider, IEventBus eventBus, IRegistrationPoliciesReader policiesReader, ICurrentUser currentUser)
    {
        _connectionStringProvider = connectionStringProvider;
        _eventBus = eventBus;
        _policiesReader = policiesReader;
        _currentUser = currentUser;
    }

    public async Task<int> InsertAsync(RegistrationPolicyRecord policy, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", policy.RefId);
        command.Parameters.AddWithValue("@Name", policy.Name);
        command.Parameters.AddWithValue("@UserId", policy.UserId);
        command.Parameters.AddWithValue("@MaxClients", (object?)policy.MaxClients ?? DBNull.Value);
        command.Parameters.AddWithValue("@Expiry", (object?)policy.Expiry ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pin", policy.Pin);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        var newPolicyId = (int)idParameter.Value;

        await _eventBus.PublishAsync(new PolicyCreatedEvent { PolicyRefId = policy.RefId, UserId = policy.UserId }, cancellationToken);

        return newPolicyId;
    }

    public async Task UpdateAsync(RegistrationPolicyRecord policy, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_Update";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", policy.Id);
        command.Parameters.AddWithValue("@Name", policy.Name);
        command.Parameters.AddWithValue("@MaxClients", (object?)policy.MaxClients ?? DBNull.Value);
        command.Parameters.AddWithValue("@Expiry", (object?)policy.Expiry ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await _eventBus.PublishAsync(new PolicyUpdatedEvent { PolicyRefId = policy.RefId, UserId = policy.UserId }, cancellationToken);
    }

    public async Task DeleteAsync(Guid refId, CancellationToken cancellationToken)
    {
        var policy = await _policiesReader.GetByRefIdAsync(_currentUser.Id, refId, cancellationToken);

        if (policy == null) return;

        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_DeleteBy_RefId";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await _eventBus.PublishAsync(new PolicyDeletedEvent { PolicyRefId = refId, UserId = policy.UserId }, cancellationToken);
    }
}
