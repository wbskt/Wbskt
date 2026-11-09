using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Billing;

internal sealed class WorkspaceCreditCapService(IWorkspaceCreditCapProvider caps, IEventBus eventBus, TimeProvider clock) : IWorkspaceCreditCapService
{
    // DECIMAL(18,4) holds up to 10^14; a cap anywhere near that is no cap at all.
    private const decimal MaxCap = 1_000_000_000_000m;

    public async Task<Result<WorkspaceCreditCapResponse>> SetAsync(int workspaceId, WorkspaceCreditCapRequest request, CancellationToken ct)
    {
        if (request.MonthlyCreditCap is < 0 or > MaxCap)
        {
            return Result<WorkspaceCreditCapResponse>.Failure(Error.Validation(
                "WORKSPACE_CREDIT_CAP_INVALID", $"A monthly credit cap is between 0 and {MaxCap:N0} credits, or null for none."));
        }

        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        decimal spent = await caps.SetAsync(workspaceId, request.MonthlyCreditCap, monthStart, ct);
        await eventBus.PublishAsync(new WorkspaceCreditCapSetEvent(workspaceId, request.MonthlyCreditCap), ct);

        // A cap lowered to or below what was already spent refuses new runs at once; the response says so.
        return Result<WorkspaceCreditCapResponse>.Success(new WorkspaceCreditCapResponse(
            request.MonthlyCreditCap, spent, request.MonthlyCreditCap is { } cap && spent >= cap));
    }
}
