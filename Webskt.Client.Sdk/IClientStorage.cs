namespace Webskt.Client.Sdk;

public interface IClientStorage
{
    Task SaveCredentialsAsync(Guid refId, string secret);
    Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync();
    Task ClearCredentialsAsync();
}
