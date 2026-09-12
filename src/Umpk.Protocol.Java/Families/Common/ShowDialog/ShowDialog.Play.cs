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
        /// <summary>Show dialog (<c>minecraft:show_dialog</c>, 776).</summary>
        public static readonly PacketType<ClientboundShowDialogPacket> ShowDialog =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("show_dialog"));
    }
}

/// <summary>Show dialog (776): a dialog holder (<c>ByteBufCodecs.holder(Registries.DIALOG,...)</c>). The wire is VarInt(id): 0 means an inline dialog whose network-NBT body follows (preserved raw as an <see cref="Umpk.Nbt.NbtTag"/> in <see cref="InlineDialog"/>); a positive value is a registry reference of id-1 (<see cref="RegistryId"/> = value-1, inline dialog null).</summary>
public sealed record ClientboundShowDialogPacket(int? RegistryId, Umpk.Nbt.NbtTag? InlineDialog) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ShowDialog;
}
