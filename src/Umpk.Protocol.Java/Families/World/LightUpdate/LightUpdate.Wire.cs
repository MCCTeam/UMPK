using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>Modern standalone light update (1.20 onward, protocols 763+): two VarInt chunk coords + the shared light-data block, with no trust-edges boolean. The boolean leaves at 763, not 764.</summary>
    public static readonly PacketCodec<ClientboundLightUpdatePacket> LightUpdateV1_20 =
        PacketCodec<ClientboundLightUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundLightUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
                WriteLightData(ref w, p.Light);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int x = r.ReadVarInt();
                int z = r.ReadVarInt();
                return new ClientboundLightUpdatePacket(x, z, ReadLightData(ref r));
            });

    /// <summary>26.3 standalone light update (protocol 777): the 1.20 body with the four BitSet masks as VarInt-length-prefixed byte arrays (little-endian, trailing zeros stripped) instead of long arrays. The nibble-array lists are unchanged.</summary>
    public static readonly PacketCodec<ClientboundLightUpdatePacket> LightUpdateV26_3 =
        PacketCodec<ClientboundLightUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundLightUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
                WriteLightData777(ref w, p.Light);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int x = r.ReadVarInt();
                int z = r.ReadVarInt();
                return new ClientboundLightUpdatePacket(x, z, ReadLightData777(ref r));
            });

    /// <summary>Flattening-era standalone light update (1.14-1.15.2, protocols 477-578). Both versions carry two VarInt chunk coords, four single-VarInt section masks (sky/block/emptySky/emptyBlock), then one VarInt-length-prefixed 2048-byte array per set bit of the sky mask followed by one per set bit of the block mask. There is no trailing trust-edges bool (1.17+) and the masks are single VarInts, not the 1.17 BitSet long arrays. Each single-int mask is carried in a length-1 <c>long[]</c> so the shared <see cref="LightUpdateData"/> record fits.</summary>
    public static readonly PacketCodec<ClientboundLightUpdatePacket> LightUpdateV1_14 =
        PacketCodec<ClientboundLightUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundLightUpdatePacket p, PacketCodecContext _) =>
            {
                LightUpdateData d = p.Light;
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
                w.WriteVarInt((int)d.SkyYMask[0]);
                w.WriteVarInt((int)d.BlockYMask[0]);
                w.WriteVarInt((int)d.EmptySkyYMask[0]);
                w.WriteVarInt((int)d.EmptyBlockYMask[0]);
                foreach (byte[] section in d.SkyUpdates)
                    w.WriteByteArray(section);

                foreach (byte[] section in d.BlockUpdates)
                    w.WriteByteArray(section);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int x = r.ReadVarInt();
                int z = r.ReadVarInt();
                int skyMask = r.ReadVarInt();
                int blockMask = r.ReadVarInt();
                int emptySkyMask = r.ReadVarInt();
                int emptyBlockMask = r.ReadVarInt();
                byte[][] skyUpdates = new byte[System.Numerics.BitOperations.PopCount((uint)skyMask)][];
                for (int i = 0; i < skyUpdates.Length; i++)
                    skyUpdates[i] = r.ReadByteArray().ToArray();

                byte[][] blockUpdates = new byte[System.Numerics.BitOperations.PopCount((uint)blockMask)][];
                for (int i = 0; i < blockUpdates.Length; i++)
                    blockUpdates[i] = r.ReadByteArray().ToArray();

                return new ClientboundLightUpdatePacket(x, z, new LightUpdateData(
                    [skyMask], [blockMask], [emptySkyMask], [emptyBlockMask], skyUpdates, blockUpdates));
            });

    /// <summary>1.16-1.16.5 standalone light update (protocols 735-754): the <see cref="LightUpdateV1_14"/> wire with a trust-edges bool inserted after the chunk coordinates. The layout is two VarInt chunk coordinates, the bool, four VarInt masks, then a 2048-byte array for each set mask bit. Reading those frames without the boolean leaves the payload misaligned.</summary>
    public static readonly PacketCodec<ClientboundLightUpdatePacket> LightUpdateV1_16 =
        PacketCodec<ClientboundLightUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundLightUpdatePacket p, PacketCodecContext _) =>
            {
                LightUpdateData d = p.Light;
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
                w.WriteBool(d.TrustEdges);
                w.WriteVarInt((int)d.SkyYMask[0]);
                w.WriteVarInt((int)d.BlockYMask[0]);
                w.WriteVarInt((int)d.EmptySkyYMask[0]);
                w.WriteVarInt((int)d.EmptyBlockYMask[0]);
                foreach (byte[] section in d.SkyUpdates)
                    w.WriteByteArray(section);

                foreach (byte[] section in d.BlockUpdates)
                    w.WriteByteArray(section);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int x = r.ReadVarInt();
                int z = r.ReadVarInt();
                bool trustEdges = r.ReadBool();
                int skyMask = r.ReadVarInt();
                int blockMask = r.ReadVarInt();
                int emptySkyMask = r.ReadVarInt();
                int emptyBlockMask = r.ReadVarInt();
                byte[][] skyUpdates = new byte[System.Numerics.BitOperations.PopCount((uint)skyMask)][];
                for (int i = 0; i < skyUpdates.Length; i++)
                    skyUpdates[i] = r.ReadByteArray().ToArray();

                byte[][] blockUpdates = new byte[System.Numerics.BitOperations.PopCount((uint)blockMask)][];
                for (int i = 0; i < blockUpdates.Length; i++)
                    blockUpdates[i] = r.ReadByteArray().ToArray();

                return new ClientboundLightUpdatePacket(x, z, new LightUpdateData(
                    [skyMask], [blockMask], [emptySkyMask], [emptyBlockMask], skyUpdates, blockUpdates, trustEdges));
            });

    /// <summary>1.17-1.19.4 standalone light update (protocols 755-762): the modern BitSet block with the trust-edges bool still in front of it. The 1.17 chunk-height overhaul replaced the four single VarInt masks with VarInt-prefixed long arrays and the mask-derived array runs with two VarInt-counted lists. These versions read the bool before the shared light-data block. The bool is gone by 1.20.1: the protocol 763 tail is one byte shorter and parses only without it, while the protocol 762 tail parses only with it. This codec's band therefore ends at 762 and <see cref="LightUpdateV1_20"/> starts at 763, not at 764.</summary>
    public static readonly PacketCodec<ClientboundLightUpdatePacket> LightUpdateV1_17 =
        PacketCodec<ClientboundLightUpdatePacket>.Of(
            static (ref PacketWriter w, ClientboundLightUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
                w.WriteBool(p.Light.TrustEdges);
                WriteLightData(ref w, p.Light);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int x = r.ReadVarInt();
                int z = r.ReadVarInt();
                bool trustEdges = r.ReadBool();
                return new ClientboundLightUpdatePacket(x, z, ReadLightData(ref r) with { TrustEdges = trustEdges });
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLightUpdate(PacketBindings bindings)
    {
        // Standalone light packet from 1.14. Five wire eras:
        //  - 477-578 (1.14-1.15.2): single-VarInt masks, no trust-edges bool.
        //  - 735-754 (1.16-1.16.5): the same masks with a trust-edges bool after the coordinates.
        //  - 755-762 (1.17-1.19.4): BitSet masks + counted array lists, bool still present.
        //  - 763-776 (1.20-26.2): BitSet masks, bool removed.
        //  - 777+ (26.3): the masks are VarInt-length-prefixed byte arrays (BitSet.toByteArray form), bool still absent.
        // The band boundary at the top is 763, not 764: the 1.20.1 light tail parses only without the boolean, while the 1.19.4 tail parses only with it.
        bindings.Packet(WorldPackets.Clientbound.LightUpdate)
            .From(JavaProtocols.V1_14, WorldStateCodecs.LightUpdateV1_14)
            .From(JavaProtocols.V1_16, WorldStateCodecs.LightUpdateV1_16)
            .From(JavaProtocols.V1_17, WorldStateCodecs.LightUpdateV1_17)
            .From(JavaProtocols.V1_20, WorldStateCodecs.LightUpdateV1_20)
            .From(JavaProtocols.V26_3, WorldStateCodecs.LightUpdateV26_3);
    }
}
