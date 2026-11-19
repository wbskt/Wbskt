namespace Wbskt.Auth.Host.Models;

/// <summary>What <c>AccountCredentials.CheckAsync</c> found when a password was presented for an account.</summary>
internal enum PasswordCheck
{
    /// <summary>The password is right.</summary>
    Verified,

    /// <summary>The password is right, but stored with older hashing settings; re-hash it.</summary>
    VerifiedNeedsRehash,

    /// <summary>The password is wrong. It was counted towards the lock.</summary>
    Wrong,

    /// <summary>The account is locked, or has its limit of attempts in flight. The password was not checked.</summary>
    Locked,
}
