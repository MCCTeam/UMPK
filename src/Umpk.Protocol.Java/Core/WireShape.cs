namespace Umpk.Protocol.Java.Codecs;

/// <summary>What a codec reads, as data it carries. Built once at codec construction, rendered into the codec-identity pin, and never consulted on the hot path.</summary>
/// <remarks>The point is provenance, not completeness. <see cref="BoundPacketCodec.CodecIdentity"/> is the bind site's own source text, so a rename moves it on every line that names the codec. A shape is computed from the codec OBJECT, so a rename can never move it, and a rebinding across two codecs with DIFFERENT wire shapes cannot avoid moving it. Two codecs that declare the same shape are twins for this column by construction, which is what <c>IntentionalTwins</c> exists to make visible.</remarks>
public sealed class WireShape
{
    // PascalCase, not s_unknownShape: no private static readonly field in this package uses the s_ prefix, and matching twenty-one neighbours beats matching a style rule nothing enforces here.
    private static readonly WireShape UnknownShape = new("opaque");

    private WireShape(string token) => Token = token;

    /// <summary>The rendered description, for example <c>sbyte,varint,short|components/3f9a12c7</c>.</summary>
    public string Token { get; }

    /// <summary>A codec that has not yet declared its field list. The ratchet's starting state.</summary>
    public static WireShape Opaque => UnknownShape;

    /// <summary>Declares a field list, optionally with a token contributed by an era value.</summary>
    /// <param name="fields">The field list, comma separated, in wire order.</param>
    /// <param name="eraToken">A token derived from the era shape or table this codec closed over.</param>
    /// <returns>The shape.</returns>
    public static WireShape Of(string fields, string? eraToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fields);
        return new WireShape(Check(eraToken is null ? fields : $"{fields}|{eraToken}"));
    }

    /// <summary>Declares a shape whose whole content is an era value's own description.</summary>
    /// <param name="kind">The family token, for example <c>chunk_modern</c>.</param>
    /// <param name="era">The era shape this codec closed over.</param>
    /// <typeparam name="TEra">The era shape's type.</typeparam>
    /// <returns>The shape.</returns>
    public static WireShape OfEra<TEra>(string kind, TEra era)
        where TEra : struct
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        // By value, not by `in`: an era shape is 12 to 16 bytes and the guidance to pass by reference is
        // for large structs. This runs once per codec at static initialisation and never on a frame,
        // which is why the interpolation is acceptable here and would not be inside a codec body.
        return new WireShape(Check($"{kind}({era.ToString()})"));
    }

    /// <inheritdoc />
    public override string ToString() => Token;

    /// <summary>The pin appends a token as the last whitespace-separated field of a line, and the additive check reads it back by stripping exactly one field. A token carrying whitespace would split the column and make that check pass over a line it never actually compared.</summary>
    private static string Check(string token)
    {
        foreach (char c in token)
            if (char.IsWhiteSpace(c))
                throw new ArgumentException($"A wire-shape token cannot contain whitespace: '{token}'.", nameof(token));

        return token;
    }
}
