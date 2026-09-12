using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Set passengers (modern): vehicle VarInt, VarInt list of passenger ids.</summary>
    public static readonly PacketCodec<ClientboundSetPassengersPacket> SetPassengers =
        PacketCodec<ClientboundSetPassengersPacket>.Of(
            static (ref PacketWriter w, ClientboundSetPassengersPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.VehicleId);
                w.WriteList(p.Passengers, static (ref PacketWriter ww, int id) => ww.WriteVarInt(id));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetPassengersPacket(r.ReadVarInt(), r.ReadList(static (ref PacketReader rr) => rr.ReadVarInt())));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetPassengers(PacketBindings bindings)
    {
        // Arrives with the 1.9 rider rework and never changes: VarInt vehicle + VarInt-array passengers.
        bindings.Packet(EntityPackets.Clientbound.SetPassengers)
            .From(JavaEras.Combat, EntityStateCodecs.SetPassengers);
    }
}
