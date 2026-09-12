using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Show dialog (<c>minecraft:show_dialog</c>), clientbound (1.21.6+).</summary>
        public static readonly PacketType<ClientboundConfigShowDialogPacket> ShowDialog =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("show_dialog"));
    }
}

/// <summary>
/// Configuration show-dialog (1.21.6+): the dialog body as a bare unnamed-root NBT tag.
/// <para>This is not the play-phase wire. Configuration has no dialog registry access, so the body is always an inline tag with no holder id. The play form writes a leading holder VarInt. The configuration form is identical in 1.21.6, 1.21.9, 26.1, and 26.2.</para>
/// </summary>
/// <param name="Dialog">The inline dialog body, preserved raw.</param>
public sealed record ClientboundConfigShowDialogPacket(NbtTag Dialog) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ShowDialog;
}
