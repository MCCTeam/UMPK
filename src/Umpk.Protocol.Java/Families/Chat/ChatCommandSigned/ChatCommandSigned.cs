using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Signed chat command (<c>minecraft:chat_command_signed</c>, 1.20.5+ / protocol 766 onward): the packet 1.20.5 split out of <c>chat_command</c> to carry the timestamp, salt, argument signatures, and last-seen acknowledgement.</summary>
        public static readonly PacketType<ServerboundChatCommandSignedPacket> ChatCommandSigned =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat_command_signed"));
    }
}

/// <summary>The 1.20.5+ signed chat command (<c>minecraft:chat_command_signed</c>, protocol 766 onward): the same payload <see cref="ServerboundSignedChatCommandPacket"/> carries, on the separate wire identity the 1.20.5 split introduced. Vanilla's client sends this form when the parsed command has signable arguments and the plain <see cref="ServerboundChatCommandPacket"/> otherwise.</summary>
public sealed record ServerboundChatCommandSignedPacket(
    string Command,
    long TimestampMillis,
    long Salt,
    IReadOnlyList<SignedCommandArgument> ArgumentSignatures,
    LastSeenMessagesUpdate LastSeen) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ChatCommandSigned;
}
