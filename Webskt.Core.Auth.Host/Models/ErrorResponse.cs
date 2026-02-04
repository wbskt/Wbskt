namespace Webskt.Core.Auth.Host.Models;

public record ErrorResponse(
    string Message, 
    string Type, 
    string TraceId
);
