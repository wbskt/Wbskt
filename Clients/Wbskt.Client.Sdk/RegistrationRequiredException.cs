namespace Wbskt.Client.Sdk;

/// <summary>
/// The device has no stored credentials and no policy PIN to register with, so it can never connect.
/// </summary>
public sealed class RegistrationRequiredException()
    : InvalidOperationException("No saved credentials and no policy PIN to register with.");
