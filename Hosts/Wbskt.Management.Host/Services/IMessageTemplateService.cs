using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IMessageTemplateService
{
    Task<Result<IPagedList<MessageTemplateResponse>>> GetAllAsync(int workspaceId, Guid? policyRefId, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<Result<MessageTemplateResponse>> CreateAsync(int workspaceId, MessageTemplateRequest request,
        CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(int workspaceId, Guid refId, MessageTemplateRequest request,
        CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default);
}
