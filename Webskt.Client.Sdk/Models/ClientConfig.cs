namespace Webskt.Client.Sdk.Models;

public record ClientConfig(
    string BaseApiUrl,    
    string BaseSocketUrl, 
    string DeviceName,    
    string? PolicyPin = null
);