namespace Wbskt.Socket.Host.Infrastructure;

public interface IRevocationCache
{
    void Revoke(Guid clientRefId);
    void Clear(Guid clientRefId);
    bool IsRevoked(Guid clientRefId);
}
