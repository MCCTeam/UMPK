using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>Set simulation distance: a VarInt distance.</summary>
    public static readonly PacketCodec<ClientboundSetSimulationDistancePacket> SetSimulationDistanceV1_18 =
        PacketCodec<ClientboundSetSimulationDistancePacket>.Of(
            static (ref PacketWriter w, ClientboundSetSimulationDistancePacket p, PacketCodecContext _) => w.WriteVarInt(p.SimulationDistance),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetSimulationDistancePacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetSimulationDistance(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetSimulationDistance)
            .From(JavaProtocols.V1_18, WorldStateCodecs.SetSimulationDistanceV1_18);
    }
}
