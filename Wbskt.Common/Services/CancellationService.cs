namespace Wbskt.Common.Services;

public interface ICancellationService
{
    void InvokeOnShutdown(Action action);

    Task Cancel();

    CancellationToken GetToken();
}

public sealed class CancellationService : ICancellationService
{
    private readonly CancellationTokenSource ctx = new();

    public void InvokeOnShutdown(Action action)
    {
        ctx.Token.Register(action);
    }

    public async Task Cancel()
    {
        await ctx.CancelAsync();
        ctx.Dispose();
    }

    public CancellationToken GetToken()
    {
        return ctx.Token;
    }
}
