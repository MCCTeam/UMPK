using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Legacy 1.8 chat message (<c>minecraft:chat</c>).</summary>
        public static readonly PacketType<ClientboundLegacyChatPacket> LegacyChat =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("chat"));
    }

    public static partial class Serverbound
    {
        /// <summary>Legacy 1.8 chat message (<c>minecraft:chat</c>).</summary>
        public static readonly PacketType<ServerboundLegacyChatPacket> LegacyChat =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat"));

        /// <summary>Signed chat message (<c>minecraft:chat</c>, 1.19.1+ / v3 signing).</summary>
        public static readonly PacketType<ServerboundSignedChatPacket> SignedChat =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat"));
    }
}

/// <summary>The pre-1.19 clientbound chat (<c>minecraft:chat</c>): a JSON component plus a position byte (0 chat, 1 system, 2 action bar). From 1.16 (protocol 735) the packet also carries the sender UUID; it is <see langword="null"/> on 1.8-1.15.2 (protocols 47-578, no sender field) and retained otherwise so a structural re-encode reproduces the wire exactly.</summary>
/// <param name="Message">The parsed chat component (what the client surfaces).</param>
/// <param name="Position">The position byte (0 chat, 1 system, 2 action bar).</param>
/// <param name="Sender">The sender UUID (1.16+); <see langword="null"/> on 1.8-1.15.2 and code-built packets.</param>
/// <param name="RawJson">The exact JSON string the component was decoded from, retained so a decoded packet re-encodes byte-for-byte (the component JSON round-trip is lossy: key order and simple-component collapse are normalised). <see langword="null"/> for a code-built packet, whose <paramref name="Message"/> is serialised instead. This is the chat mirror of the chunk two-path design (verbatim wire alongside a decoded view).</param>
public sealed record ClientboundLegacyChatPacket(Component Message, byte Position, Guid? Sender = null, string? RawJson = null) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.LegacyChat;
}

/// <summary>The 1.8 serverbound chat: a single string (max 100 chars).</summary>
public sealed record ServerboundLegacyChatPacket(string Message) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.LegacyChat;
}

/// <summary>The 1.19+ signed chat message shape: message text, timestamp, salt, optional signature, and the last-seen acknowledgment window. Signing crypto lives in <c>Signing/</c>; this declaration models the payload fields so the wire round-trips (an unsigned message uses a null signature).</summary>
/// <remarks><see cref="LastSeen"/> is the 1.19.3+ (v3) window. The 1.19.1/1.19.2 (v2) acknowledgment has a different shape and rides <see cref="LegacyLastSeen"/> / <see cref="LegacyLastReceived"/>; 1.19 (v1) carries no acknowledgment at all and leaves all three at their defaults.</remarks>
public sealed record ServerboundSignedChatPacket(
    string Message,
    long TimestampMillis,
    long Salt,
    byte[]? Signature,
    LastSeenMessagesUpdate LastSeen) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.SignedChat;

    /// <summary>v2 (protocol 760) only: the last-seen list this message acknowledges, newest first, exactly the window the signed body was built over. Empty on every other era.</summary>
    public IReadOnlyList<LastSeenMessageEntry> LegacyLastSeen { get; init; } = [];

    /// <summary>v2 (protocol 760) only: the optional trailing "last received" entry. Vanilla's client leaves it absent whenever the last-seen list already covers what it saw, which is the steady state, so this is null on everything UMPK sends; it is modeled so a decoded third-party frame re-encodes exactly.</summary>
    public LastSeenMessageEntry? LegacyLastReceived { get; init; }
}
