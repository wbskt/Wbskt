namespace Wbskt.Workflow.Engine.Host.Middleware;

// Marks a controller as one of the engine's inbound trigger endpoints. LeaderOnlyMiddleware gates
// on this attribute's presence in endpoint metadata rather than on a hardcoded "/api/inbound" path
// prefix, so a new inbound controller is held back on a standby by construction.
// InboundApiKeyMiddleware does not rely on it: it requires the key on every endpoint that is not
// [AllowAnonymous], so a controller that forgets this attribute is still closed.
[AttributeUsage(AttributeTargets.Class)]
public sealed class InboundEndpointAttribute : Attribute;
