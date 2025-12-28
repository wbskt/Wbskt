namespace Wbskt.Common.Services;

public interface ICancellationService
{
    void InvokeOnShutdown(Action action);

    Task Cancel();

    CancellationToken GetToken();
}

public sealed class CancellationService : ICancellationService
{
    private readonly CancellationTokenSource _ctx = new();

    public void InvokeOnShutdown(Action action)
    {
        _ctx.Token.Register(action);
    }

    public async Task Cancel()
    {
        await _ctx.CancelAsync();
        _ctx.Dispose();
    }

    public CancellationToken GetToken()
    {
        return _ctx.Token;
    }
}
