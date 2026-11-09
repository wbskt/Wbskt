using Wbskt.Infrastructure;
using Wbskt.Management.Models;

namespace Wbskt.Management.Host.Services.Billing;

public interface IBusinessOverageService
{
    Task<Result<BusinessOverageReport>> GetAsync(int year, int month, CancellationToken ct);
}
