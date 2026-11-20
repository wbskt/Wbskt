namespace Wbskt.Auth.Host.Models;

/// <summary>
/// A workspace as one of its members sees it in their list: whether they hold the account's Administrator role
/// there, and how many members it has.
/// </summary>
public sealed record MemberWorkspace(Workspace Workspace, bool IsAdmin, int MemberCount);
