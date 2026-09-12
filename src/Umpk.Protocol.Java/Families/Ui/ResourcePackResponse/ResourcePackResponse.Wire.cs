using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ResourcePackCodecs
{
    /// <summary>Legacy 1.8 resource pack status (47-110): the pack hash then the VarInt result.</summary>
    public static readonly PacketCodec<ServerboundLegacyResourcePackPacket> ServerLegacyResourcePackV1_8 =
        PacketCodec<ServerboundLegacyResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyResourcePackPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Hash ?? throw new ProtocolViolationException("A 47-110 resource-pack response requires the pack hash."), 40);
                w.WriteVarInt((int)p.Action);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundLegacyResourcePackPacket(r.ReadString(40), (ResourcePackAction)r.ReadVarInt()));

    /// <summary>Resource pack status, 210-404 (1.10-1.13.2): the VarInt result ONLY. 1.10 dropped the leading pack hash the 1.8/1.9 form echoed back, and the packet stayed a bare result until 1.20.3 replaced it with the uuid-keyed modern form.</summary>
    /// <remarks>Sending the 1.9 form to a 1.10+ server prefixes the result with a stray hash string, which the server rejects as an oversized packet.</remarks>
    public static readonly PacketCodec<ServerboundLegacyResourcePackPacket> ServerLegacyResourcePackV1_10 =
        PacketCodec<ServerboundLegacyResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyResourcePackPacket p, PacketCodecContext _) =>
                w.WriteVarInt((int)p.Action),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundLegacyResourcePackPacket(Hash: null, (ResourcePackAction)r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackResponse(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Serverbound.LegacyResourcePack)
            .From(JavaProtocols.V1_8, ResourcePackCodecs.ServerLegacyResourcePackV1_8);
    }
}
