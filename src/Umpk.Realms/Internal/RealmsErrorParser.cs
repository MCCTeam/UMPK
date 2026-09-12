using System.Text.Json;

namespace Umpk.Realms.Internal;

/// <summary>
/// Classifies a Realms HTTP failure into a <see cref="RealmsErrorKind"/> with this precedence:
/// <list type="bullet">
/// <item><description>
/// Statuses 503 and 277 return a retry signal before the body is read. Status 401 returns an authentication error before the body is decoded.
/// </description></item>
/// <item><description>
/// Status 429 returns the service-busy error. An empty body classifies by status alone; otherwise the body is parsed leniently for <c>reason</c>/<c>errorMsg</c>/<c>errorCode</c>, and a parse failure degrades to a raw-payload error rather than throwing.
/// </description></item>
/// </list>
/// This type is total (every status/body pair maps to a <see cref="RealmsErrorKind"/>) and never throws.
/// </summary>
internal static class RealmsErrorParser
{
    public static RealmsErrorClassification Classify(int statusCode, string body)
    {
        // These statuses are retry signals, regardless of the body.
        if (statusCode is 503 or 277)
            return new RealmsErrorClassification(RealmsErrorKind.ServiceBusy, null, null);

        // Authentication failure wins over any error code in the body.
        if (statusCode == 401)
            return new RealmsErrorClassification(RealmsErrorKind.Unauthorized, null, null);

        // Rate limiting wins over any error code in the body.
        if (statusCode == 429)
            return new RealmsErrorClassification(RealmsErrorKind.ServiceBusy, null, null);

        (int? errorCode, string? reason) = TryReadBody(body);
        if (errorCode is { } code)
        {
            RealmsErrorKind kind = code switch
            {
                6001 => RealmsErrorKind.ClientOutdated,
                6002 => RealmsErrorKind.TermsNotAgreed,
                6005 => RealmsErrorKind.WorldLocked,
                6006 => RealmsErrorKind.WorldOutOfDate,
                _ => RealmsErrorKind.ServiceError,
            };
            return new RealmsErrorClassification(kind, code, reason);
        }

        // A body-less 403 is mapped to the only concrete user action available: accepting the terms in the launcher. Recognized body error codes still take precedence above.
        if (statusCode == 403)
            return new RealmsErrorClassification(RealmsErrorKind.TermsNotAgreed, null, reason);

        return new RealmsErrorClassification(RealmsErrorKind.ServiceError, null, reason);
    }

    private static (int? ErrorCode, string? Reason) TryReadBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return (null, null);

        try
        {
            RealmsErrorDto? dto = JsonSerializer.Deserialize(body, RealmsJsonContext.Default.RealmsErrorDto);
            return (dto?.ErrorCode, dto?.Reason);
        }
        catch (JsonException)
        {
            // Malformed bodies degrade to a generic error; text/html responses land here too.
            return (null, null);
        }
    }
}

/// <summary>The result of <see cref="RealmsErrorParser.Classify"/>: the assigned kind plus whatever the body carried.</summary>
internal readonly record struct RealmsErrorClassification(RealmsErrorKind Kind, int? ErrorCode, string? Reason);
