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
        // misc ui

        /// <summary>Open sign editor (<c>minecraft:open_sign_editor</c>).</summary>
        public static readonly PacketType<ClientboundOpenSignEditorPacket> OpenSignEditor =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("open_sign_editor"));
    }
}

/// <summary>Open sign editor: the sign block position, plus (770/776) a front-text flag.</summary>
public sealed record ClientboundOpenSignEditorPacket(BlockPos Pos, bool IsFrontText) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.OpenSignEditor;
}
