namespace Wbskt.Infrastructure.Security;

public interface IIdentityService
{
    UserIdentity GetUserIdentity();
    IDisposable BeginScope(UserIdentity scope);
}
