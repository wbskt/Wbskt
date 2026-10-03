namespace Wbskt.Client.Sdk.Models;

/// <param name="OfflineBufferSize">
/// How many messages <see cref="IWbsktClient.SendAsync"/> keeps in memory while the device is
/// offline; they are sent in order, with their original send time, once it reconnects. When the
/// buffer is full the oldest message is dropped. 0 turns buffering off, and sending while
/// offline throws instead. The buffer is not persisted, so it is lost if the process exits.
/// </param>
public record ClientConfig(
    string BaseApiUrl,    
    string BaseSocketUrl, 
    string DeviceName,    
    string? PolicyPin = null,
    int OfflineBufferSize = 1000
);
