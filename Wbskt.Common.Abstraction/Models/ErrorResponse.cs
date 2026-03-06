namespace Wbskt.Common.Abstraction.Models;

public record ErrorResponse(
    string Message, 
    string Type, 
    string TraceId
);
