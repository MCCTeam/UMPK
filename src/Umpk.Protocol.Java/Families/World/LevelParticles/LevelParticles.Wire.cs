using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldEffectCodecs
{
    /// <summary>1.8 particles: int particle id, bool long-distance, float x/y/z, float xd/yd/zd, float speed, int count, then the fixed per-id trailing VarInt arguments.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_8 =
        PacketCodec<ClientboundLevelParticlesPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelParticlesPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.Particle.TypeId);
                w.WriteBool(p.OverrideLimiter); // long-distance
                w.WriteFloat((float)p.X);
                w.WriteFloat((float)p.Y);
                w.WriteFloat((float)p.Z);
                w.WriteFloat(p.XDist);
                w.WriteFloat(p.YDist);
                w.WriteFloat(p.ZDist);
                w.WriteFloat(p.MaxSpeed);
                w.WriteInt(p.Count);
                w.WriteBytes(p.Particle.Options ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int typeId = r.ReadInt();
                bool longDistance = r.ReadBool();
                float x = r.ReadFloat();
                float y = r.ReadFloat();
                float z = r.ReadFloat();
                float xd = r.ReadFloat();
                float yd = r.ReadFloat();
                float zd = r.ReadFloat();
                float speed = r.ReadFloat();
                int count = r.ReadInt();
                ParticleData particle = ReadLegacyParticleArgs(ref r, typeId);
                return new ClientboundLevelParticlesPacket(longDistance, AlwaysShow: false, x, y, z, xd, yd, zd, speed, count, particle);
            });

    /// <summary>1.13-1.13.2 (393-404) particles: the same header as <see cref="LevelParticlesV1_8"/> (int type id, long-distance bool, seven floats, int count) but the trailing per-particle payload is no longer a fixed run of VarInt arguments. The flattening replaced the numeric argument list with a type-dispatched particle body: a block state VarInt, an item stack, or three floats plus a scale for dust. Versions 1.9 through 1.12.2 instead carry a fixed number of VarInt arguments for each particle type.</summary>
    /// <remarks>UMPK has no 1.13 particle-options table, so the payload is captured as the frame remainder for a byte-exact re-encode rather than decoded into typed fields. That is honest and lossless on the wire; the alternative, reusing the 1.8 fixed-VarInt reader, would silently mis-frame every block, item and dust particle on those three protocols.</remarks>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_13 =
        PacketCodec<ClientboundLevelParticlesPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelParticlesPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.Particle.TypeId);
                w.WriteBool(p.OverrideLimiter);
                w.WriteFloat((float)p.X);
                w.WriteFloat((float)p.Y);
                w.WriteFloat((float)p.Z);
                w.WriteFloat(p.XDist);
                w.WriteFloat(p.YDist);
                w.WriteFloat(p.ZDist);
                w.WriteFloat(p.MaxSpeed);
                w.WriteInt(p.Count);
                w.WriteBytes(p.Particle.Options ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int typeId = r.ReadInt();
                bool longDistance = r.ReadBool();
                float x = r.ReadFloat();
                float y = r.ReadFloat();
                float z = r.ReadFloat();
                float xd = r.ReadFloat();
                float yd = r.ReadFloat();
                float zd = r.ReadFloat();
                float speed = r.ReadFloat();
                int count = r.ReadInt();
                byte[] options = r.ReadRemaining().ToArray();
                return new ClientboundLevelParticlesPacket(longDistance, AlwaysShow: false, x, y, z, xd, yd, zd, speed, count, new ParticleData(typeId, options));
            });

    /// <summary>1.14-1.14.4 (477-498) particles: int type id FIRST, limiter bool, FLOAT x/y/z, three float offsets, float speed, int count, then the type-specific options as the frame remainder.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_14 =
        MakeTypeIdFirstParticles(varIntTypeId: false, doublePosition: false);

    /// <summary>1.15-1.18.2 (573-758) particles: as 1.14 but x/y/z became doubles.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_15 =
        MakeTypeIdFirstParticles(varIntTypeId: false, doublePosition: true);

    /// <summary>1.20.5/1.20.6 (766) particles: the particle moved to the END of the frame, but the <c>alwaysShow</c> bool does not exist yet and the era's dust colours are still Vector3f triples.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_20_5 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_20_5, ItemPacketCodecShared.Table766, HasAlwaysShow: false));

    /// <summary>1.21/1.21.1 (767) particles: the 766 frame and the 766 particle registry ordering, but the 767 ITEM-COMPONENT table.</summary>
    /// <remarks>Two tables feed a modern particle and they move independently. The particle_type registry is identical between 766 and 767 (109 entries in the same order), which is why one particle table serves both. The item-component registry is NOT: 1.21 inserted <c>minecraft:jukebox_playable</c> at wire id 42, so 767 has 57 ids against 766's 56 and every id from 42 up is shifted by one. The <c>item</c> particle option carries a whole count-first component stack on 766-774, so decoding a 767 item particle through <see cref="LevelParticlesV1_20_5"/> dispatched every component at or above id 42 to the wrong codec and consumed the wrong number of bytes, which desynchronizes the rest of the frame. Every other 766/767 component-stack carrier already splits here (<c>ContainerCodecs</c>, <c>MerchantCodecs</c>, <c>AdvancementCodecs</c>); level_particles was the one that did not.</remarks>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_20_5, ItemPacketCodecShared.Table767, HasAlwaysShow: false));

    /// <summary>1.21.2/1.21.3 (768) particles: int dust colours, the duration-less trail, still no always-show bool.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_2 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_2, ItemPacketCodecShared.Table768, HasAlwaysShow: false));

    /// <summary>1.21.4 (769) particles: the always-show bool arrives, and trail gains its duration VarInt.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_4 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_4, ItemPacketCodecShared.Table769));

    /// <summary>1.21.5 particles: limiter bool, always-show bool, double x/y/z, float xd/yd/zd, float speed, int count, particle payload.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_5 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_5, ItemStackCodecs.ComponentsV1_21_5));

    /// <summary>1.21.6-1.21.8 (771/772) particles: the 770 particle ids with the 771 item component era.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_6 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_5, ItemPacketCodecShared.Table771));

    /// <summary>1.21.9/1.21.10 (773) particles: its own ids, and effect/instant_effect/dragon_breath/flash gain payloads.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_9 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table773));

    /// <summary>1.21.11 (774) particles: the 773 particle ids with the 774 item component era.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_21_11 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table774));

    /// <summary>26.1 (775) particles: its own ids, and the item particle uses the template-stack form.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV26_1 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV26_1, ItemPacketCodecShared.Table775));

    /// <summary>26.2 (776) particles: its own particle ids (the geyser family) and the 776 item component era.</summary>
    public static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV26_2 =
        MakeModernParticles(new ParticlesWire(ParticleCodec.ModernV26_2, ItemStackCodecs.ComponentsV26_2));

    /// <summary>level_particles for 1.19-1.20.4 (759-765): the particle TYPE id is a VarInt written FIRST, then overrideLimiter bool, double x/y/z, float xOff/yOff/zOff, float maxSpeed, int count, then the particle-specific data as the trailing remainder. The modern (1.20.5+) codec moves the type id into the particle payload at the end, so decoding a 1.19 frame with it re-encodes at equal length but different bytes. Versions 1.19 through 1.20.4 write the VarInt id first. Version 1.20.5 is the first to move it to the end. This member therefore covers 764 and 765 as well. The particle data is the frame remainder, captured raw for byte-exact re-encode.</summary>
    internal static readonly PacketCodec<ClientboundLevelParticlesPacket> LevelParticlesV1_19 =
        PacketCodec<ClientboundLevelParticlesPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelParticlesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Particle.TypeId);
                w.WriteBool(p.OverrideLimiter);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteFloat(p.XDist);
                w.WriteFloat(p.YDist);
                w.WriteFloat(p.ZDist);
                w.WriteFloat(p.MaxSpeed);
                w.WriteInt(p.Count);
                w.WriteBytes(p.Particle.Options ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int typeId = r.ReadVarInt();
                bool limiter = r.ReadBool();
                double x = r.ReadDouble();
                double y = r.ReadDouble();
                double z = r.ReadDouble();
                float xd = r.ReadFloat();
                float yd = r.ReadFloat();
                float zd = r.ReadFloat();
                float speed = r.ReadFloat();
                int count = r.ReadInt();
                byte[] options = r.ReadRemaining().ToArray();
                return new ClientboundLevelParticlesPacket(limiter, AlwaysShow: false, x, y, z, xd, yd, zd, speed, count, new ParticleData(typeId, options));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLevelParticles(PacketBindings bindings)
    {
        // level_particles has two independent era axes.
        //
        // Axis 1, the packet frame. The particle type id is written FIRST on every version up to 1.20.4 and only moves to the END of the frame at 1.20.5, where it becomes part of the particle payload. The frame layouts are:
        //   47-392   int id, bool, three FLOAT coords, three float offsets, float speed, int count, then
        //            a fixed run of VarInt arguments.
        //   393-404  as above, but the flattening replaced the argument run with a type-dispatched
        //            options body (kept raw, see LevelParticlesV1_13).
        //   477-498  int id first, FLOAT coordinates.
        //   573-758  int id first, DOUBLE coordinates.
        //   759-765  the id becomes a VarInt and remains FIRST.
        //   766-768  the particle moves to the END; no alwaysShow bool yet.
        //   769+     a second leading bool, alwaysShow.
        // Applying the modern frame below 766 reads the id from the wrong offset; applying the 769 form to 766-768 also consumes an alwaysShow boolean that those protocols do not carry.
        //
        // Axis 2, the particle registry and its option payloads, which is why the modern band cannot be one entry: ParticleCodec owns the per-era tables. Protocol 775 in particular was bound to the 776 table, whose ids are shifted by the geyser family.
        //
        // Axis 2 has a THIRD input that is not the particle registry: the item-component table, because the "item" particle option carries a whole component stack. It moves on its own schedule, and 767 is where the two disagree. The particle_type registry is identical on 766 and 767 (109 entries, same order), but 1.21 inserted minecraft:jukebox_playable at component wire id 42, so 767 carries 57 component ids against 766's 56 and everything from 42 up shifts. 766 and 767 therefore need different codecs despite sharing a frame and particle ordering.
        bindings.Packet(WorldPackets.Clientbound.LevelParticles)
            .From(JavaProtocols.V1_8, WorldEffectCodecs.LevelParticlesV1_8)
            .From(JavaProtocols.V1_13, WorldEffectCodecs.LevelParticlesV1_13)
            .From(JavaProtocols.V1_14, WorldEffectCodecs.LevelParticlesV1_14)
            .From(JavaProtocols.V1_15, WorldEffectCodecs.LevelParticlesV1_15)
            .From(JavaProtocols.V1_19, WorldEffectCodecs.LevelParticlesV1_19)
            .From(JavaProtocols.V1_20_5, WorldEffectCodecs.LevelParticlesV1_20_5)
            .From(JavaProtocols.V1_21, WorldEffectCodecs.LevelParticlesV1_21)
            .From(JavaProtocols.V1_21_2, WorldEffectCodecs.LevelParticlesV1_21_2)
            .From(JavaProtocols.V1_21_4, WorldEffectCodecs.LevelParticlesV1_21_4)
            .From(JavaProtocols.V1_21_5, WorldEffectCodecs.LevelParticlesV1_21_5)
            .From(JavaProtocols.V1_21_6, WorldEffectCodecs.LevelParticlesV1_21_6)
            .From(JavaProtocols.V1_21_9, WorldEffectCodecs.LevelParticlesV1_21_9)
            .From(JavaProtocols.V1_21_11, WorldEffectCodecs.LevelParticlesV1_21_11)
            .From(JavaProtocols.V26_1, WorldEffectCodecs.LevelParticlesV26_1)
            .From(JavaProtocols.V26_2, WorldEffectCodecs.LevelParticlesV26_2);
    }
}
