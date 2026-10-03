using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class SharedVariableProvider : BaseSqlProvider, ISharedVariableProvider
{
    // THROW 50023 in SharedVariable_Increment/_Decrement: the variable exists but is not an integer counter.
    private const int NotACounterError = 50023;

    public SharedVariableProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.SharedVariable_GetBy_WorkflowRefId_Name",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@VarName", varName);
            },
            Map,
            new KeyNotFoundException($"SharedVariable with WorkflowRefId={workflowRefId} VarName={varName} not found."),
            ct
        );
    }

    public async Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.SharedVariable_Initialize",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@VarName", varName);
                p.AddWithValue("@VarType", varType);
                p.AddWithValue("@ValueJson", valueJson);
            },
            Map,
            new InvalidOperationException("SharedVariable_Initialize did not return a row."),
            ct
        );
    }

    public async Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.SharedVariable_Set",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@VarName", varName);
                p.AddWithValue("@ValueJson", valueJson);
            },
            Map,
            // SharedVariable_Set is UPDATE-only: no row means the variable was never initialised.
            null,
            ct
        );
    }

    public async Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
    {
        try
        {
            return await ExecuteSingleAsync(
                "dbo.SharedVariable_Increment",
                p =>
                {
                    p.AddWithValue("@WorkflowRefId", workflowRefId);
                    p.AddWithValue("@VarName", varName);
                    p.AddWithValue("@Delta", delta);
                },
                r => r.GetString(0),
                new InvalidOperationException("SharedVariable_Increment did not return a value."),
                ct
            );
        }
        catch (SqlException ex) when (ex.Number == NotACounterError)
        {
            throw new SharedVariableNotACounterException(varName, ex);
        }
    }

    public async Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
    {
        try
        {
            return await ExecuteSingleAsync(
                "dbo.SharedVariable_Decrement",
                p =>
                {
                    p.AddWithValue("@WorkflowRefId", workflowRefId);
                    p.AddWithValue("@VarName", varName);
                    p.AddWithValue("@Delta", delta);
                },
                r => r.GetString(0),
                new InvalidOperationException("SharedVariable_Decrement did not return a value."),
                ct
            );
        }
        catch (SqlException ex) when (ex.Number == NotACounterError)
        {
            throw new SharedVariableNotACounterException(varName, ex);
        }
    }

    public async Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.SharedVariable_CompareAndSet",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@VarName", varName);
                p.AddWithValue("@Expected", expected);
                p.AddWithValue("@NewValue", newValue);
            },
            r => r.GetInt32(0),
            new InvalidOperationException("SharedVariable_CompareAndSet did not return a value."),
            ct
        );
    }

    internal static SharedVariableRow Map(DbDataReader reader)
    {
        return new SharedVariableRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            VarName = reader.GetString(reader.GetOrdinal("VarName")),
            VarType = reader.GetString(reader.GetOrdinal("VarType")),
            ValueJson = reader.GetString(reader.GetOrdinal("ValueJson")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
