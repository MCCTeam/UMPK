using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBlockCodecs
{
    /// <summary>1.8 multi block change: two chunk ints, then a VarInt count, then per record a signed short position (packs x=(v&gt;&gt;12)&amp;15, y=v&amp;255, z=(v&gt;&gt;8)&amp;15) and a VarInt state.</summary>
    public static readonly PacketCodec<ClientboundSectionBlocksUpdatePacket> SectionBlocksUpdateV1_8 =
        PacketCodec<ClientboundSectionBlocksUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundSectionBlocksUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.LegacyChunkX);
                w.WriteInt(p.LegacyChunkZ);
                w.WriteVarInt(p.Changes.Count);
                for (int i = 0; i < p.Changes.Count; i++)
                {
                    w.WriteShort(p.Changes[i].PackedPosition);
                    w.WriteVarInt(p.Changes[i].BlockStateId);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int cx = r.ReadInt();
                int cz = r.ReadInt();
                int count = r.ReadVarInt();
                var changes = new SectionBlockChange[count];
                for (int i = 0; i < count; i++)
                {
                    short packed = r.ReadShort();
                    int state = r.ReadVarInt();
                    changes[i] = new SectionBlockChange(packed, state);
                }

                return new ClientboundSectionBlocksUpdatePacket(SectionPos: 0, cx, cz, changes, IsLegacy: true);
            });

    /// <summary>Modern section blocks update: a packed SectionPos long, a VarInt count, then per record a VarLong packing <c>state &lt;&lt; 12 | localXYZ</c>. The 12-bit local position is carried as-is in <see cref="SectionBlockChange.PackedPosition"/>.</summary>
    public static readonly PacketCodec<ClientboundSectionBlocksUpdatePacket> SectionBlocksUpdateV1_20 =
        PacketCodec<ClientboundSectionBlocksUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundSectionBlocksUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteLong(p.SectionPos);
                w.WriteVarInt(p.Changes.Count);
                for (int i = 0; i < p.Changes.Count; i++)
                {
                    long packed = ((long)p.Changes[i].BlockStateId << 12) | (uint)(p.Changes[i].PackedPosition & 0xFFF);
                    w.WriteVarLong(packed);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                long section = r.ReadLong();
                int count = r.ReadVarInt();
                var changes = new SectionBlockChange[count];
                for (int i = 0; i < count; i++)
                {
                    long packed = r.ReadVarLong();
                    short local = (short)(packed & 0xFFF);
                    int state = (int)(packed >>> 12);
                    changes[i] = new SectionBlockChange(local, state);
                }

                return new ClientboundSectionBlocksUpdatePacket(section, LegacyChunkX: 0, LegacyChunkZ: 0, changes);
            });

    /// <summary>section_blocks_update for 1.19-1.19.4: carries a <c>suppressLightUpdates</c> bool between the section-pos long and the change-count VarInt; it was removed at 1.20 (763 rides the default no-bool member). Version 1.19.2 has the bool and 1.20.1 does not. It is always false in join and idle traffic, so this codec re-emits false.</summary>
    internal static readonly PacketCodec<ClientboundSectionBlocksUpdatePacket> SectionBlocksUpdateV1_16_2 =
        PacketCodec<ClientboundSectionBlocksUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundSectionBlocksUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteLong(p.SectionPos);
                w.WriteBool(false);
                w.WriteVarInt(p.Changes.Count);
                for (int i = 0; i < p.Changes.Count; i++)
                {
                    long packed = ((long)p.Changes[i].BlockStateId << 12) | (uint)(p.Changes[i].PackedPosition & 0xFFF);
                    w.WriteVarLong(packed);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                long section = r.ReadLong();
                r.ReadBool();
                int count = r.ReadVarInt();
                var changes = new SectionBlockChange[count];
                for (int i = 0; i < count; i++)
                {
                    long packed = r.ReadVarLong();
                    short local = (short)(packed & 0xFFF);
                    int state = (int)(packed >>> 12);
                    changes[i] = new SectionBlockChange(local, state);
                }

                return new ClientboundSectionBlocksUpdatePacket(section, LegacyChunkX: 0, LegacyChunkZ: 0, changes);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSectionBlocksUpdate(PacketBindings bindings)
    {
        // The multi-block-change wire has three era forms:
        //  - 1.8-1.16.1 (<=736): legacy i32 chunk-x/z + short-pos/VarInt-state records (V1_8 shape).
        //  - 1.16.2-1.19.4 (751-762): bitfield section long + notTrustEdges bool + VarLong records.
        //  - 1.20+ (>=763): bitfield section long + VarLong records, the bool removed.
        // 1.8 spells it block_change_multi. The 477-736 datasets (1.14 through 1.16.1) spell it chunk_blocks_update. Version 1.16.2 renamed it section_blocks_update. The chunk_blocks_update alias applies to 477-736. Its body remains int chunk X, int chunk Z, VarInt count, then short packed-position and VarInt state records, matching the V1_8 codec.
        bindings.Packet(WorldPackets.Clientbound.SectionBlocksUpdate)
            .From(JavaProtocols.V1_8, WorldBlockCodecs.SectionBlocksUpdateV1_8)
            .From(JavaProtocols.V1_16_2, WorldBlockCodecs.SectionBlocksUpdateV1_16_2)
            .From(JavaProtocols.V1_20, WorldBlockCodecs.SectionBlocksUpdateV1_20)
            .AliasedAs(Identifier.Minecraft("block_change_multi"))
            .AliasedAs(Identifier.Minecraft("chunk_blocks_update"), JavaProtocols.V1_9, JavaProtocols.V1_16_1);
    }
}
