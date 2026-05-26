using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class RunCountersProvider : BaseSqlProvider, IRunCountersProvider
{
    private readonly string _connectionString;

    public RunCountersProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<int> IncrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.RunCounters_IncrementActiveBranches", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@Delta", delta);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetInt32(0);
        }

        throw new InvalidOperationException("RunCounters_IncrementActiveBranches did not return a value.");
    }

    public async Task<int> DecrementActiveBranchesAsync(int runId, int delta, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.RunCounters_DecrementActiveBranches", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@Delta", delta);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetInt32(0);
        }

        throw new InvalidOperationException("RunCounters_DecrementActiveBranches did not return a value.");
    }

    public async Task<decimal> AddCreditsConsumedAsync(int runId, decimal cost, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.RunCounters_AddCreditsConsumed", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@Cost", cost);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetDecimal(0);
        }

        throw new InvalidOperationException("RunCounters_AddCreditsConsumed did not return a value.");
    }
}
