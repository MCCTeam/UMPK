namespace Umpk.Auth;

/// <summary>Base type for all authentication failures raised by <see cref="Umpk.Auth"/>. Messages carry error codes and stage identifiers only; access tokens, refresh tokens, and keys are never included.</summary>
public class AuthException : Exception
{
    public AuthException(string message)
        : base(message)
    {
    }

    public AuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A step in the Microsoft or Yggdrasil chain returned a non-success HTTP status or an error payload. <see cref="Stage"/> names the step (for example <c>xbl</c>, <c>xsts</c>, <c>login_with_xbox</c>); <see cref="StatusCode"/> is the HTTP status when one is available.</summary>
public sealed class AuthServiceException : AuthException
{
    public AuthServiceException(string stage, int statusCode, string message)
        : base(message)
    {
        Stage = stage;
        StatusCode = statusCode;
    }

    /// <summary>The chain step that failed.</summary>
    public string Stage { get; }

    /// <summary>The HTTP status code, or 0 when the failure was not HTTP-status shaped.</summary>
    public int StatusCode { get; }
}

/// <summary>XSTS authorization returned a known <c>XErr</c> code identifying an account-level problem. The well-known cases (no Xbox account, child account, region ban) are surfaced through <see cref="Reason"/>; the raw code is preserved in <see cref="XErr"/>.</summary>
public sealed class XstsAuthorizationException : AuthException
{
    public XstsAuthorizationException(long xErr, XstsErrorReason reason, string message)
        : base(message)
    {
        XErr = xErr;
        Reason = reason;
    }

    /// <summary>The raw XSTS <c>XErr</c> code.</summary>
    public long XErr { get; }

    /// <summary>The classified reason for the failure.</summary>
    public XstsErrorReason Reason { get; }
}

/// <summary>The signed-in account does not own Minecraft (the entitlement check found no game items).</summary>
public sealed class NoMinecraftEntitlementException : AuthException
{
    public NoMinecraftEntitlementException(string message)
        : base(message)
    {
    }
}

/// <summary>The Microsoft device-code login could not complete because the user declined, or the code expired or timed out before authorization.</summary>
public sealed class DeviceCodeAuthorizationException : AuthException
{
    public DeviceCodeAuthorizationException(DeviceCodeFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Why the device-code flow ended without a token.</summary>
    public DeviceCodeFailure Failure { get; }
}

/// <summary>Classification of well-known XSTS <c>XErr</c> codes.</summary>
public enum XstsErrorReason
{
    /// <summary>Any code not otherwise classified.</summary>
    Unknown,

    /// <summary>2148916233: the Microsoft account has no linked Xbox account.</summary>
    NoXboxAccount,

    /// <summary>2148916235: the account is from a country where Xbox Live is unavailable.</summary>
    RegionUnavailable,

    /// <summary>2148916236 / 2148916237: adult verification (South Korea) is required.</summary>
    AdultVerificationRequired,

    /// <summary>2148916238: the account belongs to a minor and must be added to a Family.</summary>
    ChildAccount,

    /// <summary>2148916227: the account is banned or has had all its Xbox Live privileges revoked.</summary>
    AccountBanned,
}

/// <summary>How a device-code flow terminated without producing a token.</summary>
public enum DeviceCodeFailure
{
    /// <summary>The user explicitly declined authorization.</summary>
    Declined,

    /// <summary>The device code expired before the user authorized it.</summary>
    Expired,

    /// <summary>The overall wait exceeded the code lifetime.</summary>
    TimedOut,
}
