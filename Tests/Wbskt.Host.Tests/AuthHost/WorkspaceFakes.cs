using Moq;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

internal static class WorkspaceFakes
{
    /// <summary>The created-workspace outbox message with no signed-in user behind it.</summary>
    public static WorkspaceCreatedOutbox Created() => new(Mock.Of<IIdentityService>());
}
