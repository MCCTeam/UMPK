namespace Umpk.Auth;

/// <summary>The information a host must display to complete a Microsoft device-code login: the user visits <see cref="VerificationUri"/> and enters <see cref="UserCode"/>. <see cref="Message"/> is the ready-made English instruction Microsoft returns.</summary>
public sealed record DeviceCodePrompt(
    string UserCode,
    Uri VerificationUri,
    string Message,
    DateTimeOffset ExpiresAt);
