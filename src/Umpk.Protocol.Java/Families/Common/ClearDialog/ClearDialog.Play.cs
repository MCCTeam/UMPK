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
        /// <summary>Clear dialog (<c>minecraft:clear_dialog</c>, 776).</summary>
        public static readonly PacketType<ClientboundClearDialogPacket> ClearDialog =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("clear_dialog"));
    }
}

/// <summary>Clear dialog (776): an empty packet.</summary>
public sealed record ClientboundClearDialogPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ClearDialog;
}
