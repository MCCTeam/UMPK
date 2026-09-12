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
    /// <summary>1.8 / 1.21.5 default spawn: packed BlockPos (+ angle float on 1.21.5).</summary>
    public static readonly PacketCodec<ClientboundSetDefaultSpawnPositionPacket> SetDefaultSpawnV1_8 =
        MakeSetDefaultSpawn(BlockPosLayout.PrePacked114, hasAngle: false);

    /// <summary>1.14 through 1.16.5 default spawn: still a bare BlockPos with no angle, but under the 1.14 long packing. A frame-length check cannot see this change because both layouts use eight bytes and only the field order inside the long moves: the new packed value is <c>x &lt;&lt; X_OFFSET | y | z &lt;&lt; Z_OFFSET</c>, while the 1.8 layout puts y in the middle. Only the decoded coordinates show the difference. Versions 1.14 through 1.16.5 stop after the packed position. Version 1.17 is the first to append an angle float.</summary>
    public static readonly PacketCodec<ClientboundSetDefaultSpawnPositionPacket> SetDefaultSpawnV1_14 =
        MakeSetDefaultSpawn(BlockPosLayout.Packed114, hasAngle: false);

    /// <summary>1.21.5 default spawn: packed BlockPos + angle float.</summary>
    public static readonly PacketCodec<ClientboundSetDefaultSpawnPositionPacket> SetDefaultSpawnV1_17 =
        MakeSetDefaultSpawn(BlockPosLayout.Packed114, hasAngle: true);

    /// <summary>1.21.9+ default spawn: a respawn-data block = dimension resource key + packed BlockPos, then yaw float + pitch float. The rework landed at 1.21.9, not 26.2. The layout is unchanged through 1.21.11, 26.1, and 26.2, so protocols 773/774/775/776 all share this shape.</summary>
    public static readonly PacketCodec<ClientboundSetDefaultSpawnPositionPacket> SetDefaultSpawnV1_21_9 =
        PacketCodec<ClientboundSetDefaultSpawnPositionPacket>.Of(
            static (ref PacketWriter w, ClientboundSetDefaultSpawnPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Dimension ?? throw new ProtocolViolationException("1.21.9+ default spawn requires a dimension key."));
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteFloat(p.Angle);
                w.WriteFloat(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string dimension = r.ReadString();
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
                float yaw = r.ReadFloat();
                float pitch = r.ReadFloat();
                return new ClientboundSetDefaultSpawnPositionPacket(pos, yaw, dimension, pitch);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetDefaultSpawnPosition(PacketBindings bindings)
    {
        // A bare packed position before 1.17, which appends a float spawn angle; the dimension-aware rework then lands at 1.21.9 (1.21.9/1.21.11/26.1/26.2 are wire-identical; only symbol names differ). 1.8 spells it spawn_position; 107-404 and 477-578 spell it set_spawn_position. The block-position packing changes at 1.14, moving Y from the middle of the long to the low bits, so 477-578 needs the 1.14-packed no-angle form. The same-length encodings make this distinction invisible to length checks.
        bindings.Packet(WorldPackets.Clientbound.SetDefaultSpawnPosition)
            .From(JavaProtocols.V1_8, WorldStateCodecs.SetDefaultSpawnV1_8)
            .From(JavaProtocols.V1_14, WorldStateCodecs.SetDefaultSpawnV1_14)
            .From(JavaProtocols.V1_17, WorldStateCodecs.SetDefaultSpawnV1_17)
            .From(JavaProtocols.V1_21_9, WorldStateCodecs.SetDefaultSpawnV1_21_9)
            .AliasedAs(Identifier.Minecraft("set_spawn_position"), JavaProtocols.V1_9, JavaProtocols.V1_15_2)
            .AliasedAs(Identifier.Minecraft("spawn_position"));
    }
}
