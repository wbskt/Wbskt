using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// A Redis channel on which the auth host announces that someone's access to some workspace may have
/// changed (a role, group, grant, membership, suspension or ownership), so hosts that cache resolved
/// access can drop what they hold instead of serving it until it expires.
/// </summary>
/// <remarks>
/// The message carries nothing: changes are rare administrative actions, so a listener simply forgets
/// everything, which is never wrong. Delivery is best effort. A listener that was disconnected may have
/// missed messages, so it should also forget everything when its connection comes back, and its cache
/// lifetime remains the bound when Redis is not configured or down.
/// </remarks>
public sealed class WorkspaceAccessChanges
{
    internal const string Channel = "wbskt:workspace-access-changes";

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<WorkspaceAccessChanges> _logger;

    public WorkspaceAccessChanges(IConnectionMultiplexer? redis, ILogger<WorkspaceAccessChanges> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>Announces a change. Never throws and never waits on Redis.</summary>
    public void Publish()
    {
        if (_redis is null)
        {
            return;
        }

        try
        {
            _redis.GetSubscriber().Publish(RedisChannel.Literal(Channel), "1", CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Could not announce a workspace access change; cached access elsewhere expires on its own");
        }
    }

    /// <summary>
    /// Calls <paramref name="onChange"/> for every announced change, and whenever the Redis connection
    /// is restored (announcements made while it was down are lost). Returns false when Redis is not
    /// configured or the subscription failed.
    /// </summary>
    public async Task<bool> SubscribeAsync(Action onChange)
    {
        if (_redis is null)
        {
            return false;
        }

        _redis.ConnectionRestored += (_, _) => onChange();

        try
        {
            await _redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(Channel), (_, _) => onChange());
            return true;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Could not subscribe to workspace access changes; cached access is bounded by its lifetime only");
            return false;
        }
    }
}
