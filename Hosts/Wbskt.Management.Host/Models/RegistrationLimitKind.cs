namespace Wbskt.Management.Host.Models;

/// <summary>Which limit refused a registration, as <c>dbo.RegistrationLimitNotices</c> records it.</summary>
public enum RegistrationLimitKind
{
    /// <summary>The account's plan limit on registered clients.</summary>
    Plan,

    /// <summary>The policy's own limit.</summary>
    Policy
}
