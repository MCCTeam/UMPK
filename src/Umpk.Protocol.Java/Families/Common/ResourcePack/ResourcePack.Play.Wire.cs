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
    /// <summary>Legacy 1.8 resource pack send (47).</summary>
    public static readonly PacketCodec<ClientboundLegacyResourcePackPacket> LegacyResourcePackV1_8 =
        PacketCodec<ClientboundLegacyResourcePackPacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyResourcePackPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Url);
                w.WriteString(p.Hash, 40);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundLegacyResourcePackPacket(r.ReadString(), r.ReadString(40)));

    /// <summary>Resource pack response (770/776).</summary>
    public static readonly PacketCodec<ServerboundResourcePackPacket> ServerResourcePackV1_20_3 =
        PacketCodec<ServerboundResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundResourcePackPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackResponse(ref w, p.Id, (int)p.Action, hasId: true),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Guid id, int action) = CommonPayloads.ReadResourcePackResponse(ref r, hasId: true);
                return new ServerboundResourcePackPacket(id, (ResourcePackAction)action);
            });

    /// <summary>Resource pack response, 477-764 (1.14-1.20.2): the VarInt action ordinal ALONE. The pack uuid the modern codec writes is a 1.20.3 addition, so sending the uuid form to a 1.14-1.20.2 server puts 16 bytes in front of the ordinal and the server reads uuid bytes as the action.</summary>
    /// <remarks>Through 1.20.2 the payload is a single action enum; 1.20.3 adds a UUID before it. That is the same 765 boundary the configuration-phase copy models, so the two phases move together. The record is <see cref="ServerboundResourcePackPacket"/> in both eras and the id is simply not written here, so one applier path answers every protocol from 1.14 on.</remarks>
    public static readonly PacketCodec<ServerboundResourcePackPacket> ServerResourcePackV1_14 =
        PacketCodec<ServerboundResourcePackPacket>.Of(
            static (ref PacketWriter w, ServerboundResourcePackPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackResponse(ref w, p.Id, (int)p.Action, hasId: false),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (Guid id, int action) = CommonPayloads.ReadResourcePackResponse(ref r, hasId: false);
                return new ServerboundResourcePackPacket(id, (ResourcePackAction)action);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackPlay(PacketBindings bindings)
    {
        // minecraft:resource_pack (clientbound) is the PACK REQUEST, and it exists only on 47-764: the 1.20.3 split replaced it with resource_pack_push / resource_pack_pop. Three wire generations:
        //   47-754  url + hash
        //   755-764 the same two strings plus a required bool and an OPTIONAL JSON-string prompt
        //   765+    the identifier is gone
        // Protocols 477-764 decode into the push record with an empty pack uuid so one applier path can answer every era. The serverbound half uses the matching uuid-less response on the same band.
        bindings.Packet(UiPackets.Clientbound.LegacyResourcePack)
            .From(JavaProtocols.V1_8, ResourcePackCodecs.LegacyResourcePackV1_8)
            .FromAs(JavaProtocols.V1_14, UiPackets.Clientbound.ResourcePackPush, ResourcePackCodecs.ResourcePackPushV1_14)
            .FromAs(JavaProtocols.V1_17, UiPackets.Clientbound.ResourcePackPush, ResourcePackCodecs.ResourcePackPushV1_17);

        // 107-110 echo the pack hash before the result (the 1.8 body under the modern identifier);
        // 1.10 drops the hash and leaves a bare VarInt result, which runs to 764. The uuid-keyed modern form arrives at 1.20.3, not at 1.14: through 1.20.2 the action enum is alone, and from 1.20.3 it is preceded by a UUID. Sending the UUID form before 1.20.3 puts 16 unexpected bytes before the action ordinal, so the server cannot read the response.
        bindings.Packet(UiPackets.Serverbound.ResourcePack)
            .FromAs(JavaProtocols.V1_9, UiPackets.Serverbound.LegacyResourcePack, ResourcePackCodecs.ServerLegacyResourcePackV1_8)
            .FromAs(JavaProtocols.V1_10, UiPackets.Serverbound.LegacyResourcePack, ResourcePackCodecs.ServerLegacyResourcePackV1_10)
            .From(JavaProtocols.V1_14, ResourcePackCodecs.ServerResourcePackV1_14)
            .From(JavaProtocols.V1_20_3, ResourcePackCodecs.ServerResourcePackV1_20_3);
    }
}
