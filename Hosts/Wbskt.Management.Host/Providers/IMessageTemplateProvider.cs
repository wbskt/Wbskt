using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Providers;

public interface IMessageTemplateProvider
{
    Task<IPagedList<MessageTemplate>> GetAllAsync(int workspaceId, int? policyId, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<MessageTemplate?> FindByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<MessageTemplate> InsertAsync(int workspaceId, int? policyId, string name, string messageType,
        string payloadJson, CancellationToken cancellationToken = default);
    Task UpdateAsync(int workspaceId, int id, int? policyId, string name, string messageType, string payloadJson,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
}
