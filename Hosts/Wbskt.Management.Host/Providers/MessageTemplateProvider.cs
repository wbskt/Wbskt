using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Providers;

internal sealed class MessageTemplateProvider : BaseSqlProvider, IMessageTemplateProvider
{
    public MessageTemplateProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IPagedList<MessageTemplate>> GetAllAsync(int workspaceId, int? policyId, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.MessageTemplate_GetAll",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@PolicyId", (object?)policyId ?? DBNull.Value);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapTemplate,
            cancellationToken
        );
    }

    public async Task<MessageTemplate?> FindByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteFindAsync(
            "dbo.MessageTemplate_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapTemplate,
            cancellationToken
        );
    }

    public async Task<MessageTemplate> InsertAsync(int workspaceId, int? policyId, string name, string messageType,
        string payloadJson, int maxPerWorkspace, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.MessageTemplate_Create", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@PolicyId", (object?)policyId ?? DBNull.Value);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@MessageType", messageType);
            p.AddWithValue("@PayloadJson", payloadJson);
            p.AddWithValue("@MaxPerWorkspace", maxPerWorkspace);

            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        var refId = (Guid)parameters["@RefId"].Value;
        return await FindByRefIdAsync(refId, cancellationToken)
            ?? throw new InvalidOperationException($"Message template {refId} was not found right after it was inserted.");
    }

    public async Task UpdateAsync(int workspaceId, int id, int? policyId, string name, string messageType,
        string payloadJson, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.MessageTemplate_Update", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Id", id);
            p.AddWithValue("@PolicyId", (object?)policyId ?? DBNull.Value);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@MessageType", messageType);
            p.AddWithValue("@PayloadJson", payloadJson);
        }, cancellationToken);
    }

    public async Task DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.MessageTemplate_Delete", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Id", id);
        }, cancellationToken);
    }

    private static MessageTemplate MapTemplate(SqlDataReader reader)
    {
        return new MessageTemplate
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            PolicyId = reader.GetNullableInt32("PolicyId"),
            PolicyRefId = reader.GetNullableGuid("PolicyRefId"),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            MessageType = reader.GetString(reader.GetOrdinal("MessageType")),
            PayloadJson = reader.GetString(reader.GetOrdinal("PayloadJson")),
            CreatedAt = reader.GetUtcDateTime("CreatedAt"),
            UpdatedAt = reader.GetUtcDateTime("UpdatedAt")
        };
    }
}
