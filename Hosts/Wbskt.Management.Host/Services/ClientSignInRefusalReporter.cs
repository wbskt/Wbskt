using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Raises, once a minute, the refused sign-ins <see cref="ClientSignInRefusals"/> counted, one event per
/// client. Runs on every instance, the client-auth ones included, since each counts only the sign-ins it
/// refused itself.
/// </summary>
public sealed class ClientSignInRefusalReporter : PeriodicWorker
{
    private readonly ClientSignInRefusals _refusals;

    public ClientSignInRefusalReporter(
        ClientSignInRefusals refusals,
        IServiceScopeFactory scopeFactory,
        ILogger<ClientSignInRefusalReporter> logger,
        TimeProvider clock)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
    {
        _refusals = refusals;
    }

    protected override string Name => "Client sign-in refusals";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var bus = services.GetRequiredService<IEventBus>();
        foreach (var refused in _refusals.Take())
        {
            await bus.PublishAsync(refused, ct);
        }
    }
}
