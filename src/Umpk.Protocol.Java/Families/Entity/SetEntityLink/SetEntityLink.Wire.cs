using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>1.8 attach entity: source int, destination int, and leash boolean.</summary>
    public static readonly PacketCodec<ClientboundSetEntityLinkPacket> SetEntityLinkV1_8 =
        PacketCodec<ClientboundSetEntityLinkPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEntityLinkPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.SourceId);
                w.WriteInt(p.DestId);
                w.WriteBool(p.LegacyLeash ?? false);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetEntityLinkPacket(r.ReadInt(), r.ReadInt(), r.ReadBool()));

    /// <summary>Modern set entity link: source int, dest int.</summary>
    public static readonly PacketCodec<ClientboundSetEntityLinkPacket> SetEntityLinkV1_9 =
        PacketCodec<ClientboundSetEntityLinkPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEntityLinkPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.SourceId);
                w.WriteInt(p.DestId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetEntityLinkPacket(r.ReadInt(), r.ReadInt(), null));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetEntityLink(PacketBindings bindings)
    {
        // 1.8 carries a trailing leash BOOL; 1.9 dropped it, so 107 onward is the bare int pair the modern codec already reads. Binding the 1.8 codec on 107-404 would have consumed one byte of the next frame.
        bindings.Packet(EntityPackets.Clientbound.SetEntityLink)
            .From(JavaProtocols.V1_8, EntityStateCodecs.SetEntityLinkV1_8)
            .From(JavaProtocols.V1_9, EntityStateCodecs.SetEntityLinkV1_9)
            .AliasedAs(Identifier.Minecraft("attach_entity"));
    }
}
