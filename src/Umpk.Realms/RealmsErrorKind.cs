namespace Umpk.Realms;

/// <summary>Classifies why a Realms operation failed from its response body and HTTP status. Statuses 401, 503, and 277 are handled before the body is parsed. The internal <c>RealmsErrorParser</c> is the only place that assigns this type.</summary>
/// <remarks>
/// <see cref="ServiceError"/> is the default value (0), unlike a string-decoded sibling enum such as <c>Umpk.Auth.XstsErrorReason</c> whose default member is an explicit "Unknown" for a code the parser could not recognize. <c>RealmsErrorParser</c> has no such gap: every status/body pair it sees resolves to exactly one kind, so there is no "could not classify" state to name. <see cref="ServiceError"/> instead means "classified, and the classification is the generic bucket": a body error code that does not map to one of the specific kinds below, a non-JSON or empty body at a status the parser has no special case for, or a JSON body that fails to parse.
/// <para>The Realms body error codes 6003 (download limit), 6004 (upload limit), 6007 (too many realms), 6008 (invalid world name), and 6009 (invalid world description) do not get their own member. Each names an operation <see cref="IRealmsClient"/> does not implement (uploading or downloading a world backup, creating or renaming one). They still reach the caller through <see cref="RealmsServiceException.ErrorCode"/> as <see cref="ServiceError"/>, so they stay distinguishable by the numeric code even without a named kind.</para>
/// </remarks>
public enum RealmsErrorKind
{
    /// <summary>A body error code (or its absence) that does not map to a more specific kind below, or a body that could not be parsed at all. The generic classification.</summary>
    ServiceError = 0,

    /// <summary>The session backing the request is not a Microsoft account. Realms is Microsoft-only; this kind is never assigned by the wire-level parser, only by callers that build a credential from a session (see <see cref="RealmsSessionCredential.FromSession"/>).</summary>
    RequiresMicrosoftAccount,

    /// <summary>The service rejected the session outright (HTTP 401). Checked before any body is read; a body error code never overrides this. The token is stale, invalid, or revoked.</summary>
    Unauthorized,

    /// <summary>The account has not accepted the Realms terms of service (body error code 6002), or the response was a 403 with no recognizable body, the actionable fallback for that case.</summary>
    TermsNotAgreed,

    /// <summary>The reported client version is rejected as outdated (body error code 6001).</summary>
    ClientOutdated,

    /// <summary>The world is locked (body error code 6005).</summary>
    WorldLocked,

    /// <summary>The world is out of date and needs the owner to open it in a current client first (body error code 6006).</summary>
    WorldOutOfDate,

    /// <summary>No world matched the requested selector. Never assigned by the wire-level parser; assigned by <c>RealmsClientExtensions.ResolveWorldAsync</c> when a listing has no matching entry.</summary>
    WorldNotFound,

    /// <summary>The service is temporarily too busy to serve the request (HTTP 429, 503, or 277). Checked before any body is read for 503/277; 429 is checked after, but still ahead of the body.</summary>
    ServiceBusy,

    /// <summary>A response that looked successful could not actually be understood: malformed JSON on an otherwise successful status, or a required field (such as the join endpoint's address) missing from it.</summary>
    InvalidResponse,
}
