using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Health (all versions): health float, food VarInt, saturation float.</summary>
    public static readonly PacketCodec<ClientboundSetHealthPacket> SetHealth =
        PacketCodec<ClientboundSetHealthPacket>.Of(
            static (ref PacketWriter w, ClientboundSetHealthPacket p, PacketCodecContext _) =>
            {
                w.WriteFloat(p.Health);
                w.WriteVarInt(p.Food);
                w.WriteFloat(p.Saturation);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetHealthPacket(r.ReadFloat(), r.ReadVarInt(), r.ReadFloat()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetHealth(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.SetHealth)
            .From(JavaProtocols.V1_8, EntityStateCodecs.SetHealth)
            .AliasedAs(Identifier.Minecraft("update_health"));
    }
}
