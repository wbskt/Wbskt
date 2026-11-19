namespace Wbskt.Events;

/// <summary>
/// Left out of the live feed but kept in the audit log, unlike <see cref="SignalRPrivateAttribute"/>: a
/// figure the feed's audience is broader than, such as an account's pooled credits, which reach every
/// member of every workspace of the tenant while only billing readers may see them.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SignalRWithheldAttribute : Attribute;
