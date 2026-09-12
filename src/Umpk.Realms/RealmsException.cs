namespace Umpk.Realms;

/// <summary>Base type for all failures raised by the Realms client. Messages carry operation names, HTTP status codes, and the classified <see cref="Kind"/> only; the session access token and any other secret are never included. Mirrors the <c>Umpk.Auth.AuthException</c> pattern.</summary>
public class RealmsException : Exception
{
    public RealmsException(string message)
        : base(message)
    {
    }

    public RealmsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates a classified Realms failure not shaped by a specific HTTP call (see <see cref="RealmsServiceException"/> for those).</summary>
    public RealmsException(RealmsErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>Creates a classified Realms failure wrapping an underlying cause.</summary>
    public RealmsException(RealmsErrorKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>The classified reason for the failure. Defaults to <see cref="RealmsErrorKind.ServiceError"/> (the enum's default value) when constructed through one of the two unclassified ctors above.</summary>
    public RealmsErrorKind Kind { get; }
}

/// <summary>A Realms REST call returned a non-success HTTP status or an unparseable body. <see cref="Operation"/> names the call (for example <c>worlds</c>, <c>join</c>, <c>tos</c>, <c>compatible</c>); <see cref="RealmsException.Kind"/> is the outcome of classifying <see cref="StatusCode"/> and the response body through <c>RealmsErrorParser</c>. <see cref="ErrorCode"/> and <see cref="Reason"/> carry the raw body fields the classification was made from, when the body had them.</summary>
public sealed class RealmsServiceException : RealmsException
{
    public RealmsServiceException(string operation, int statusCode, RealmsErrorKind kind, string message)
        : base(kind, message)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    public RealmsServiceException(string operation, int statusCode, RealmsErrorKind kind, string message, Exception innerException)
        : base(kind, message, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>The Realms operation that failed.</summary>
    public string Operation { get; }

    /// <summary>The HTTP status code, or 0 when the failure was not HTTP-status shaped.</summary>
    public int StatusCode { get; }

    /// <summary>The body's <c>errorCode</c> field, when the body was JSON and carried one. Null for a status the parser classifies before reading the body (401, 429, 503, 277), for a non-JSON body, and for a JSON body without the field.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>The body's <c>reason</c> field, when the body was JSON and carried one; else null.</summary>
    public string? Reason { get; init; }
}
