using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase transfer (<c>minecraft:transfer</c>, 1.20.5+).</summary>
        public static readonly PacketType<ClientboundTransferPacket> Transfer =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("transfer"));
    }
}

/// <summary>Play-phase transfer (1.20.5+): the server hands the client off to another host and port. Vanilla's client reconnects there carrying its cookies.</summary>
public sealed record ClientboundTransferPacket(string Host, int Port) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.Transfer;
}
