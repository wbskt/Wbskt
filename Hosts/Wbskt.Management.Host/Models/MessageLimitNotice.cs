namespace Wbskt.Management.Host.Models;

/// <summary>A workspace found past a share of the message allowance it is recorded against.</summary>
public sealed record MessageLimitNotice(int WorkspaceId, long Allowance);
