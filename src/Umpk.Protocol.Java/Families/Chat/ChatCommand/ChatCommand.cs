using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Unsigned chat command (<c>minecraft:chat_command</c>, 1.20.5+ / protocol 766 onward): the bare command string. This is the identity the 1.20.5 split left on <c>chat_command</c> once the signed fields moved to <see cref="ChatCommandSigned"/>.</summary>
        public static readonly PacketType<ServerboundChatCommandPacket> ChatCommand =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat_command"));

        /// <summary>Signed chat command carried under <c>minecraft:chat_command</c> on 1.19-1.20.4 (protocols 759-765), where the single <c>chat_command</c> packet still carried the argument signatures. It shares the <c>chat_command</c> identity with <see cref="ChatCommand"/> the way <see cref="SignedChat"/> shares <c>chat</c> with <see cref="LegacyChat"/>.</summary>
        public static readonly PacketType<ServerboundSignedChatCommandPacket> SignedChatCommand =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat_command"));
    }
}

/// <summary>The 1.20.5+ unsigned chat command (<c>minecraft:chat_command</c>): just the command text, without the leading slash. This is the packet vanilla's client sends when the parsed command has no signable arguments; a server only rejects it when <c>enforce-secure-profile</c> is on AND the command does have signable arguments.</summary>
/// <remarks>A command must go out on this packet family (or <see cref="ServerboundSignedChatCommandPacket"/> / <see cref="ServerboundChatCommandSignedPacket"/>) to be executed. From 1.19 onward a leading slash sent over <c>minecraft:chat</c> is broadcast verbatim instead of run.</remarks>
public sealed record ServerboundChatCommandPacket(string Command) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ChatCommand;
}

/// <summary>The 1.19-1.20.4 signed chat command (<c>minecraft:chat_command</c>, protocols 759-765): command text, timestamp, salt, the per-argument signatures, and the last-seen acknowledgement window. Fields are the era superset; each era codec writes only what its version carries (1.19 has no last-seen window at all and folds the salt into the argument-signature block, 1.19.1/1.19.2 add a list-shaped window, 1.19.3 onward uses the offset + 20-bit bitset window).</summary>
public sealed record ServerboundSignedChatCommandPacket(
    string Command,
    long TimestampMillis,
    long Salt,
    IReadOnlyList<SignedCommandArgument> ArgumentSignatures,
    LastSeenMessagesUpdate LastSeen) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.SignedChatCommand;

    /// <summary>v2 (protocol 760) only: the last-seen list the argument signatures were built over, newest first. Empty on 759 (which has no acknowledgment) and on 761+ (which use <see cref="LastSeen"/>).</summary>
    public IReadOnlyList<LastSeenMessageEntry> LegacyLastSeen { get; init; } = [];

    /// <summary>v2 (protocol 760) only: the optional trailing "last received" entry; see <see cref="ServerboundSignedChatPacket.LegacyLastReceived"/>.</summary>
    public LastSeenMessageEntry? LegacyLastReceived { get; init; }
}
