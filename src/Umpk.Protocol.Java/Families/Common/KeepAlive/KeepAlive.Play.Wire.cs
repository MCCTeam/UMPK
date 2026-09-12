using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Play-phase keep-alive codecs. 1.8 uses a VarInt id; 1.9+ uses a long.</summary>
public static partial class PlayKeepAliveCodecs
{
    /// <summary>1.8 clientbound keep-alive (VarInt id widened to long).</summary>
    public static readonly PacketCodec<ClientboundPlayKeepAlivePacket> ClientV1_8 =
        PacketCodec<ClientboundPlayKeepAlivePacket>.Of(
            static (ref PacketWriter w, ClientboundPlayKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteLegacyKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPlayKeepAlivePacket(CommonPayloads.ReadLegacyKeepAliveId(ref r)),
            WireShape.Of("varint"));

    /// <summary>1.8 serverbound keep-alive (VarInt id).</summary>
    public static readonly PacketCodec<ServerboundPlayKeepAlivePacket> ServerV1_8 =
        PacketCodec<ServerboundPlayKeepAlivePacket>.Of(
            static (ref PacketWriter w, ServerboundPlayKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteLegacyKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPlayKeepAlivePacket(CommonPayloads.ReadLegacyKeepAliveId(ref r)),
            WireShape.Of("varint"));

    /// <summary>Modern clientbound keep-alive (long id).</summary>
    public static readonly PacketCodec<ClientboundPlayKeepAlivePacket> ClientV1_12_2 =
        PacketCodec<ClientboundPlayKeepAlivePacket>.Of(
            static (ref PacketWriter w, ClientboundPlayKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundPlayKeepAlivePacket(CommonPayloads.ReadKeepAliveId(ref r)),
            WireShape.Of("long"));

    /// <summary>Modern serverbound keep-alive (long id).</summary>
    public static readonly PacketCodec<ServerboundPlayKeepAlivePacket> ServerV1_12_2 =
        PacketCodec<ServerboundPlayKeepAlivePacket>.Of(
            static (ref PacketWriter w, ServerboundPlayKeepAlivePacket p, PacketCodecContext _) => CommonPayloads.WriteKeepAliveId(ref w, p.Id),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPlayKeepAlivePacket(CommonPayloads.ReadKeepAliveId(ref r)),
            WireShape.Of("long"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareKeepAlivePlay(PacketBindings bindings)
    {
        // keep-alive ---- VarInt id through 1.12.1, widened to a Long at 1.12.2.
        bindings.Packet(PlayPackets.Clientbound.KeepAlive)
            .From(JavaProtocols.V1_8, PlayKeepAliveCodecs.ClientV1_8)
            .From(JavaProtocols.V1_12_2, PlayKeepAliveCodecs.ClientV1_12_2);

        bindings.Packet(PlayPackets.Serverbound.KeepAlive)
            .From(JavaProtocols.V1_8, PlayKeepAliveCodecs.ServerV1_8)
            .From(JavaProtocols.V1_12_2, PlayKeepAliveCodecs.ServerV1_12_2);
    }
}
