namespace Wbskt.Core.Service.Services;

public interface IServerInfoService
{
    void UpdateServerStatus(int serverId, bool active);
}
