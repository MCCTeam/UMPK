using Umpk.Text;

namespace Umpk.Protocol.Java.Signing;

/// <summary>The three server-side chat filter verdicts, in the order indexed by the wire VarInt.</summary>
public enum ChatFilterMaskType
{
    /// <summary>Nothing was filtered; the message is displayed as sent.</summary>
    PassThrough = 0,

    /// <summary>The whole message was filtered; vanilla displays NOTHING at all.</summary>
    FullyFiltered = 1,

    /// <summary>Individual characters were filtered. The only mask type carrying a trailing bitset, one bit per character index of the SIGNED message content.</summary>
    PartiallyFiltered = 2,
}

/// <summary>A decoded chat filter mask and the rule for applying it to a message body. A server can mask individual characters in a player message; this type implements the client half of that contract.</summary>
/// <remarks>
/// <para>Pass-through masks return the text unchanged. Fully filtered masks return <c>null</c>. Partial masks replace each selected character with <c>'#'</c>. <see cref="FilteredStyle"/> renders each masked run in dark gray with a <c>chat.filtered</c> hover tooltip.</para>
/// <para>The mask indexes UTF-16 code units, so a surrogate pair occupies two bits. This must not be normalized to Unicode scalars because the server builds the mask against the same UTF-16 indices.</para>
/// <para>Wire era. The mask is on the clientbound <c>player_chat</c> frame from protocol 760 (1.19.1) onward; protocol 759 (1.19.0) has no mask field at all, so that era always decodes to <see cref="PassThrough"/>. <see cref="Read"/> takes the already-decoded type and bitset words from <c>ChatCodecs</c> rather than reading the buffer itself, because the codec's read order is what keeps the frame in sync.</para>
/// </remarks>
public sealed record ChatFilterMask
{
    /// <summary>The mask a message with no filtering carries; also the pre-1.19.1 era's only value.</summary>
    public static readonly ChatFilterMask PassThrough = new(ChatFilterMaskType.PassThrough, []);

    /// <summary>The mask that suppresses the whole message.</summary>
    public static readonly ChatFilterMask FullyFiltered = new(ChatFilterMaskType.FullyFiltered, []);

    /// <summary>The style for a masked run: dark gray with a <c>chat.filtered</c> hover tooltip.</summary>
    public static readonly Style FilteredStyle = Style.Empty with
    {
        Color = TextColor.DarkGray,
        HoverEvent = new HoverShowText(Component.Translatable("chat.filtered")),
    };

    /// <summary>The masked-character replacement.</summary>
    public const char MaskCharacter = '#';

    private readonly long[] _words;

    private ChatFilterMask(ChatFilterMaskType type, long[] words)
    {
        Type = type;
        _words = words;
    }

    /// <summary>The server's verdict for this message.</summary>
    public ChatFilterMaskType Type { get; }

    /// <summary>Builds a mask from the wire values <c>ChatCodecs</c> decoded: the type VarInt and, for a partially-filtered mask, the bitset words. An unknown type value is treated as <see cref="PassThrough"/> rather than throwing, because a mask this client does not understand must not be able to fault a session over a chat line.</summary>
    public static ChatFilterMask Read(int type, IReadOnlyList<long> bits) => type switch
    {
        (int)ChatFilterMaskType.FullyFiltered => FullyFiltered,
        (int)ChatFilterMaskType.PartiallyFiltered => new ChatFilterMask(
            ChatFilterMaskType.PartiallyFiltered, [.. bits]),
        _ => PassThrough,
    };

    /// <summary>True when nothing was filtered.</summary>
    public bool IsEmpty => Type == ChatFilterMaskType.PassThrough;

    /// <summary>True when the whole message was filtered.</summary>
    public bool IsFullyFiltered => Type == ChatFilterMaskType.FullyFiltered;

    /// <summary>True when bit <paramref name="index"/> of the character bitset is set.</summary>
    public bool IsFiltered(int index)
    {
        if (index < 0)
            return false;

        int word = index >> 6;
        return word < _words.Length && (_words[word] & (1L << (index & 63))) != 0;
    }

    /// <summary>The highest set bit index plus one, which is where the apply loop stops.</summary>
    public int BitLength
    {
        get
        {
            for (int w = _words.Length - 1; w >= 0; w--)
                if (_words[w] != 0)
                    return (w << 6) + (64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)_words[w]));

            return 0;
        }
    }

    /// <summary>Returns the text for a pass-through mask, null for a fully filtered one, and the text with every masked character replaced by <see cref="MaskCharacter"/> for a partial one.</summary>
    public string? Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (Type)
        {
            case ChatFilterMaskType.PassThrough:
                return text;
            case ChatFilterMaskType.FullyFiltered:
                return null;
            default:
                char[] chars = text.ToCharArray();
                int limit = Math.Min(chars.Length, BitLength);
                for (int i = 0; i < limit; i++)
                    if (IsFiltered(i))
                        chars[i] = MaskCharacter;

                return new string(chars);
        }
    }

    /// <summary>Returns null for a fully filtered message, a plain literal for a pass-through one, and otherwise a component whose masked runs are <see cref="MaskCharacter"/> repeats carrying <see cref="FilteredStyle"/> and whose unmasked runs are the original substrings.</summary>
    /// <remarks>Trailing characters past the last set bit form one final unmasked run. A bounded scan preserves those run boundaries.</remarks>
    public Component? ApplyWithFormatting(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (Type)
        {
            case ChatFilterMaskType.PassThrough:
                return Component.Text(text);
            case ChatFilterMaskType.FullyFiltered:
                return null;
            default:
                var runs = new List<Component>();
                int start = 0;
                while (start < text.Length)
                {
                    bool filtered = IsFiltered(start);
                    int end = start + 1;
                    while (end < text.Length && IsFiltered(end) == filtered)
                        end++;

                    runs.Add(filtered
                        ? new Component(new TextContent(new string(MaskCharacter, end - start)), FilteredStyle)
                        : Component.Text(text[start..end]));
                    start = end;
                }

                return new Component(TextContent.Empty, Style.Empty, runs);
        }
    }
}
