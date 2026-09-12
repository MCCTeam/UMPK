using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Attack entity (26.1+): a single entity id VarInt.</summary>
    public static readonly PacketCodec<ServerboundAttackPacket> Attack =
        PacketCodec<ServerboundAttackPacket>.Of(
            static (ref PacketWriter w, ServerboundAttackPacket p, PacketCodecContext _) => w.WriteVarInt(p.EntityId),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundAttackPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAttack(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.Attack)
            .From(JavaProtocols.V26_1, EntityServerboundCodecs.Attack);
    }
}
