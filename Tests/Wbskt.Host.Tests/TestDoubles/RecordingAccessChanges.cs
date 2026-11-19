using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests;

/// <summary>
/// A <see cref="WorkspaceAccessChanges"/> over a fake Redis that counts the announcements made, so a test
/// can tell whether a change told the management host to drop its cached workspace access.
/// </summary>
internal sealed class RecordingAccessChanges
{
    public RecordingAccessChanges()
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.StringIncrement(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Callback(() => Announced++);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(Mock.Of<ISubscriber>());
        Changes = new WorkspaceAccessChanges(redis.Object, NullLogger<WorkspaceAccessChanges>.Instance);
    }

    public WorkspaceAccessChanges Changes { get; }

    public int Announced { get; private set; }
}
