using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Clear dialog (<c>minecraft:clear_dialog</c>), clientbound (1.21.6+).</summary>
        public static readonly PacketType<ClientboundConfigClearDialogPacket> ClearDialog =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("clear_dialog"));
    }
}

/// <summary>Configuration clear-dialog (1.21.6+): an empty packet, identical to the play-phase <see cref="ClientboundClearDialogPacket"/>.</summary>
public sealed record ClientboundConfigClearDialogPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ClearDialog;
}
