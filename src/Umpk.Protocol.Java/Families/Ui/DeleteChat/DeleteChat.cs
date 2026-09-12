using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Delete chat (<c>minecraft:delete_chat</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundDeleteChatPacket> DeleteChat =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("delete_chat"));
    }
}

/// <summary>Delete chat (770/776): a packed message signature. The wire is VarInt(id+1): value 0 means a full 256-byte signature follows (<see cref="FullSignature"/> set, <see cref="Id"/> = -1); otherwise <see cref="Id"/> is the cache index and <see cref="FullSignature"/> is null.</summary>
public sealed record ClientboundDeleteChatPacket(int Id, byte[]? FullSignature) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.DeleteChat;
}
