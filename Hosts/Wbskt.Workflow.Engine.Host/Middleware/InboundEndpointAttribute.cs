namespace Wbskt.Workflow.Engine.Host.Middleware;

// Marks a controller as one of the engine's inbound trigger endpoints. InboundApiKeyMiddleware
// and LeaderOnlyMiddleware gate on this attribute's presence in endpoint metadata rather than on
// a hardcoded "/api/inbound" path prefix, so a new inbound controller is protected by construction
// - forgetting this attribute is a routing/wiring mistake the controller can't otherwise hide from,
// whereas forgetting to update a hardcoded string in two unrelated middlewares is easy to miss.
[AttributeUsage(AttributeTargets.Class)]
public sealed class InboundEndpointAttribute : Attribute;
