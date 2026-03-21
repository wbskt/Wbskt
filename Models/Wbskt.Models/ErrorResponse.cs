namespace Wbskt.Models;

public record ErrorResponse(
    string Message, 
    string Type, 
    string TraceId
);
