namespace Wbskt.Common.Contracts;

public class ClientConnectionRequest
{
    /// <summary>
    /// Details of all the channels the client is subscribed to
    /// </summary>
    public ClientChannel[] Channels { get; set; } = [];

    /// <summary>
    /// Human-readable name for the client
    /// </summary>
    public required string ClientName { get; set; }

    /// <summary>
    /// This id is unique for a particular client. This is an identifier for this specific client throughout the platform
    /// </summary>
    public required Guid ClientUniqueId { get; set;}
}

public class ClientConnection : ClientConnectionRequest
{
    /// <summary>
    /// Internal ID
    /// </summary>
    public int ClientId { get; set; }

    /// <summary>
    /// ID of the user who created this client
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// ID of the socket server to which this connection is associated
    /// </summary>
    public int ServerId { get; set; }
}

public class ClientChannel
{
    /// <summary>
    /// Used for a client to connect to a channel
    /// </summary>
    public Guid ChannelRef { get; set; }

    /// <summary>
    /// This is a secret string provided by the user while creation of a channel
    /// </summary>
    public required string ChannelSecret { get; set; }
}
