using Microsoft.Extensions.Configuration;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class ConnectionStringProvider : IConnectionStringProvider
{
    private readonly IConfiguration _configuration;

    public ConnectionStringProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string ConnectionString => _configuration["ConnectionStrings:DefaultConnection"]!;
}
