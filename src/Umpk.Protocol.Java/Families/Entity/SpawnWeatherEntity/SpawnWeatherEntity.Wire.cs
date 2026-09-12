using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 global-entity spawn.</summary>
    public static readonly PacketCodec<ClientboundAddGlobalEntityPacket> AddGlobalEntityV1_8 =
        PacketCodec<ClientboundAddGlobalEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddGlobalEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte(p.GlobalType);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddGlobalEntityPacket(r.ReadVarInt(), r.ReadByte(), UnpackLegacyPos(r.ReadInt()), UnpackLegacyPos(r.ReadInt()), UnpackLegacyPos(r.ReadInt())));

    /// <summary>1.9-1.13.2 spawn weather/global entity: id VarInt, type byte, double x/y/z.</summary>
    public static readonly PacketCodec<ClientboundAddGlobalEntityPacket> AddGlobalEntityV1_9 =
        PacketCodec<ClientboundAddGlobalEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddGlobalEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte(p.GlobalType);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddGlobalEntityPacket(r.ReadVarInt(), r.ReadByte(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSpawnWeatherEntity(PacketBindings bindings)
    {
        // The global-entity (lightning bolt) spawn: 1.8 writes fixed-point int coordinates, 1.9 writes doubles. The 1.9 codec existed but was never bound, so 1.9-1.15.2 decoded a 25-byte frame with the 13-byte 1.8 reader and left 12 bytes unread, which frame-exactness turns into a session fault on the first lightning strike. The packet was removed at 1.16, where lightning became an ordinary entity.
        bindings.Packet(EntityPackets.Clientbound.AddGlobalEntity)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddGlobalEntityV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddGlobalEntityV1_9);
    }
}
