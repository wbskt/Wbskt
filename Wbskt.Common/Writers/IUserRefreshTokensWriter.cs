using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IUserRefreshTokensWriter
{
    Task InsertAsync(RefreshTokenRecord refreshToken, CancellationToken cancellationToken);
    Task UpdateAsync(RefreshTokenRecord refreshToken, CancellationToken cancellationToken);
}
