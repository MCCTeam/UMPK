using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Custom click action (<c>minecraft:custom_click_action</c>), serverbound (1.21.6+).</summary>
        public static readonly PacketType<ServerboundConfigCustomClickActionPacket> CustomClickAction =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("custom_click_action"));
    }
}

/// <summary>Configuration custom-click action (1.21.6+ serverbound): an action id and an optional NBT payload. The configuration wire carries the same VarInt byte length around the optional tag as the play-phase <see cref="ServerboundCustomClickActionPacket"/>. The maximum body length is 65536 bytes. An absent payload is a length of 1 carrying a single TAG_End byte, not a length of 0.</summary>
public sealed record ServerboundConfigCustomClickActionPacket(Identifier Id, NbtTag? Payload) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.CustomClickAction;
}
