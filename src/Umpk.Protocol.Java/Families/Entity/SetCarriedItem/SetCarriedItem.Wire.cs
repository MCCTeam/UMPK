using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Set carried item (serverbound, 47/770/776): a slot short.</summary>
    public static readonly PacketCodec<ServerboundSetCarriedItemPacket> SetCarriedItem =
        PacketCodec<ServerboundSetCarriedItemPacket>.Of(
            static (ref PacketWriter w, ServerboundSetCarriedItemPacket p, PacketCodecContext _) => w.WriteShort(p.Slot),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundSetCarriedItemPacket(r.ReadShort()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetCarriedItem(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.SetCarriedItem)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.SetCarriedItem);
    }
}
