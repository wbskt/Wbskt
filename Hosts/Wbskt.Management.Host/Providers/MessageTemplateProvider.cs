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

    public async Task<MessageTemplate> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.MessageTemplate_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapTemplate,
            new NotFoundException($"Message template with RefId {refId} not found."),
            cancellationToken
        );
    }

    public async Task<MessageTemplate> InsertAsync(int workspaceId, int? policyId, string name, string messageType,
        string payloadJson, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.MessageTemplate_Create", p =>
        {
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@PolicyId", (object?)policyId ?? DBNull.Value);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@MessageType", messageType);
            p.AddWithValue("@PayloadJson", payloadJson);

            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
            p.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        }, cancellationToken);

        var refId = (Guid)parameters["@RefId"].Value;
        return await GetByRefIdAsync(refId, cancellationToken);
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
            PolicyId = reader.IsDBNull(reader.GetOrdinal("PolicyId")) ? null : reader.GetInt32(reader.GetOrdinal("PolicyId")),
            PolicyRefId = reader.IsDBNull(reader.GetOrdinal("PolicyRefId")) ? null : reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            MessageType = reader.GetString(reader.GetOrdinal("MessageType")),
            PayloadJson = reader.GetString(reader.GetOrdinal("PayloadJson")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
    }
}
