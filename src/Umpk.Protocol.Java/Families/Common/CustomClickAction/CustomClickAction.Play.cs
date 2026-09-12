using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Serverbound
    {
        /// <summary>Custom click action (<c>minecraft:custom_click_action</c>, 776).</summary>
        public static readonly PacketType<ServerboundCustomClickActionPacket> CustomClickAction =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("custom_click_action"));
    }
}

/// <summary>Custom click action (776 serverbound): an action id and an optional NBT payload. The wire uses the null-NBT convention (a bare End byte), preserved here as a null <see cref="Umpk.Nbt.NbtTag"/>.</summary>
public sealed record ServerboundCustomClickActionPacket(Identifier Id, Umpk.Nbt.NbtTag? Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.CustomClickAction;
}
