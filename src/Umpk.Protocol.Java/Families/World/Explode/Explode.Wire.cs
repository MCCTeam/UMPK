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
    /// <summary>1.8 explosion: float x/y/z, float strength, int record count, sbyte offsets, float motion x/y/z.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_8 =
        PacketCodec<ClientboundExplodePacket>.Of(
            static (ref PacketWriter w, ClientboundExplodePacket p, PacketCodecContext _) =>
            {
                w.WriteFloat((float)p.Center.X);
                w.WriteFloat((float)p.Center.Y);
                w.WriteFloat((float)p.Center.Z);
                w.WriteFloat(p.LegacyStrength);
                w.WriteInt(p.LegacyBlocks.Count);
                for (int i = 0; i < p.LegacyBlocks.Count; i++)
                {
                    w.WriteSByte(p.LegacyBlocks[i].Dx);
                    w.WriteSByte(p.LegacyBlocks[i].Dy);
                    w.WriteSByte(p.LegacyBlocks[i].Dz);
                }

                w.WriteFloat(p.LegacyMotionX);
                w.WriteFloat(p.LegacyMotionY);
                w.WriteFloat(p.LegacyMotionZ);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                float x = r.ReadFloat();
                float y = r.ReadFloat();
                float z = r.ReadFloat();
                float strength = r.ReadFloat();
                int count = r.ReadInt();
                var blocks = new ExplosionBlock[count];
                for (int i = 0; i < count; i++)
                    blocks[i] = new ExplosionBlock(r.ReadSByte(), r.ReadSByte(), r.ReadSByte());

                float mx = r.ReadFloat();
                float my = r.ReadFloat();
                float mz = r.ReadFloat();
                return new ClientboundExplodePacket(new Vec3d(x, y, z), strength, blocks, mx, my, mz,
                    Knockback: null, Particle: null, Sound: null, Radius: 0f, BlockCount: 0, BlockParticles: []);
            });

    /// <summary>1.17-1.19.2 (755-760) explosion: the 1.8 body with the block-offset list's count widened from a bare <c>int</c> to the VarInt <c>readList</c> prefix.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_17 = LegacyExplode(doubleCenter: false);

    /// <summary>1.19.3-1.20.2 (761-764) explosion: the 1.17 body with the center widened from three floats to three doubles while the radius remains a float. The form is unchanged through 1.20.2.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_19_3 = LegacyExplode(doubleCenter: true);

    /// <summary>1.20.3-1.21.1 (765-767) explosion: the 1.19.3 body plus a VarInt block-interaction ordinal and a trailing (small particle, large particle, sound event) group captured verbatim.</summary>
    /// <remarks>The tail is the only part that differs across 765/766/767 and the only part that needs a per-version particle option-shape table, so capturing it as bytes covers all three eras with one codec and keeps every field ahead of it decoded. A structural decode is blocked on a 1.20.3/1.20.4 particle shape table: the 766 table is NOT reusable, since 1.20.5 renumbered the particle registry. TODO(particle-shapes-765): decode the group once that table exists.</remarks>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_20_3 =
        PacketCodec<ClientboundExplodePacket>.Of(
            static (ref PacketWriter w, ClientboundExplodePacket p, PacketCodecContext _) =>
            {
                WriteLegacyExplosionBody(ref w, p, doubleCenter: true);
                w.WriteVarInt(p.BlockInteraction);
                w.WriteBytes(p.ParticleSoundTail ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                ClientboundExplodePacket body = ReadLegacyExplosionBody(ref r, doubleCenter: true);
                int interaction = r.ReadVarInt();
                byte[] tail = r.ReadRemaining().ToArray();
                return body with { BlockInteraction = interaction, ParticleSoundTail = tail };
            });

    /// <summary>The shared 47-767 explosion body: a center, the strength, the destroyed-block offsets, and the bare player-motion floats. <paramref name="doubleCenter"/> selects the 761+ double center.</summary>
    private static PacketCodec<ClientboundExplodePacket> LegacyExplode(bool doubleCenter) =>
        PacketCodec<ClientboundExplodePacket>.Of(
            (ref PacketWriter w, ClientboundExplodePacket p, PacketCodecContext _) =>
                WriteLegacyExplosionBody(ref w, p, doubleCenter),
            (ref PacketReader r, PacketCodecContext _) => ReadLegacyExplosionBody(ref r, doubleCenter));

    private static void WriteLegacyExplosionBody(ref PacketWriter w, ClientboundExplodePacket p, bool doubleCenter)
    {
        if (doubleCenter)
        {
            w.WriteDouble(p.Center.X);
            w.WriteDouble(p.Center.Y);
            w.WriteDouble(p.Center.Z);
        }
        else
        {
            w.WriteFloat((float)p.Center.X);
            w.WriteFloat((float)p.Center.Y);
            w.WriteFloat((float)p.Center.Z);
        }

        w.WriteFloat(p.LegacyStrength);
        w.WriteVarInt(p.LegacyBlocks.Count);
        for (int i = 0; i < p.LegacyBlocks.Count; i++)
        {
            w.WriteSByte(p.LegacyBlocks[i].Dx);
            w.WriteSByte(p.LegacyBlocks[i].Dy);
            w.WriteSByte(p.LegacyBlocks[i].Dz);
        }

        w.WriteFloat(p.LegacyMotionX);
        w.WriteFloat(p.LegacyMotionY);
        w.WriteFloat(p.LegacyMotionZ);
    }

    private static ClientboundExplodePacket ReadLegacyExplosionBody(ref PacketReader r, bool doubleCenter)
    {
        double x = doubleCenter ? r.ReadDouble() : r.ReadFloat();
        double y = doubleCenter ? r.ReadDouble() : r.ReadFloat();
        double z = doubleCenter ? r.ReadDouble() : r.ReadFloat();
        float strength = r.ReadFloat();
        int count = r.ReadVarInt();
        if (count < 0 || count > (r.Remaining / 3) + 1)
            throw new ProtocolViolationException(
                $"Explosion block count {count} is implausible for {r.Remaining} remaining bytes.");

        var blocks = new ExplosionBlock[count];
        for (int i = 0; i < count; i++)
            blocks[i] = new ExplosionBlock(r.ReadSByte(), r.ReadSByte(), r.ReadSByte());

        float mx = r.ReadFloat();
        float my = r.ReadFloat();
        float mz = r.ReadFloat();
        return new ClientboundExplodePacket(new Vec3d(x, y, z), strength, blocks, mx, my, mz,
            Knockback: null, Particle: null, Sound: null, Radius: 0f, BlockCount: 0, BlockParticles: []);
    }

    // The modern explosion (768 onward) reads a particle mid-frame, so it takes BOTH of the era axes ParticleCodec documents, and it takes them at the same boundaries level_particles does:
    //
    //   axis 1, the particle_type registry ordering:
    //           768 (111 entries), 769 (112), 770=771=772 (114), 773=774 (115), 775 (117), 776 (125).
    //   axis 2, the item-component table, because the "item" particle option carries a whole
    //           component stack: 768, 769, 770, 771(=772), 773, 774, 775, 776 are each their own.
    //
    // Each divergence needs its own codec because a changed particle id may select a different option payload width. The default explosion particle makes the wrong 776 table fatal rather than cosmetic on 775: minecraft:explosion_emitter is id 22 and minecraft:explosion is id 23 there, and the 776 table calls those two ids dust_color_transition (12 option bytes) and effect (8), so an ordinary 26.1 explosion over-read its particle and then read the sound holder from the wrong offset. On 768/769/773/774 the two default ids happen to carry no options in both tables, so only a server- or datapack-chosen explosion particle diverged there.

    /// <summary>1.21.2/1.21.3 (768) explosion: Vec3 center, optional knockback Vec3, an explosion particle, and a holder-or-inline sound event.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_2 =
        ModernExplode(ParticleCodec.ModernV1_21_2, ItemPacketCodecShared.Table768);

    /// <summary>1.21.4 (769) explosion: the 768 frame with the 769 particle ordering and component table.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_4 =
        ModernExplode(ParticleCodec.ModernV1_21_4, ItemPacketCodecShared.Table769);

    /// <summary>1.21.5 (770) explosion: the same four-member composite, with the 770 particle ordering and the 770 component table. This form cannot decode protocols 477-767 because their fields differ.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_5 =
        ModernExplode(ParticleCodec.ModernV1_21_5, ItemPacketCodecShared.Table770);

    /// <summary>1.21.6-1.21.8 (771/772) explosion: the 770 particle ordering with the 771 component table.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_6 =
        ModernExplode(ParticleCodec.ModernV1_21_5, ItemPacketCodecShared.Table771);

    /// <summary>The 768-772 explosion body, parameterized by the two particle-facing era tables.</summary>
    private static PacketCodec<ClientboundExplodePacket> ModernExplode(
        IReadOnlyDictionary<int, ParticleOptionShape> shapes, ItemComponentTable components) =>
        PacketCodec<ClientboundExplodePacket>.Of(
            (ref PacketWriter w, ClientboundExplodePacket p, PacketCodecContext _) =>
            {
                WriteVec3(ref w, p.Center);
                w.WriteOptionalStruct(p.Knockback, static (ref PacketWriter ww, Vec3d v) => WriteVec3(ref ww, v));
                ParticleCodec.WriteModern(ref w, p.Particle ?? throw new ProtocolViolationException("A 1.21.2 explosion requires an explosion particle."));
                SoundCodec.WriteHolder(ref w, p.Sound ?? throw new ProtocolViolationException("A 1.21.2 explosion requires an explosion sound."));
            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                Vec3d center = ReadVec3(ref r);
                Vec3d? knockback = ReadOptionalVec3(ref r);
                ParticleData particle = ParticleCodec.ReadModern(ref r, shapes, components, ctx);
                SoundEventHolder sound = SoundCodec.ReadHolder(ref r);
                return new ClientboundExplodePacket(center, 0f, [], 0f, 0f, 0f, knockback, particle, sound, 0f, 0, []);
            });

    /// <summary>1.21.9/1.21.10 (773) explosion: Vec3 center, radius float, block-count int, optional knockback Vec3, an explosion particle, a holder-or-inline sound, and a weighted list of block-particle infos.</summary>
    /// <remarks>The seven-member form starts at 1.21.9, not at 26.1. The particle-info entry carries a particle, float scaling, and float speed; each weighted entry then carries a VarInt weight. This framing is unchanged through 26.2.</remarks>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_9 =
        RadiusExplode(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table773);

    /// <summary>1.21.11 (774) explosion: the 773 frame and particle ordering with the 774 component table.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV1_21_11 =
        RadiusExplode(ParticleCodec.ModernV1_21_9, ItemPacketCodecShared.Table774);

    /// <summary>26.1 (775) explosion: the 773 frame with the 775 particle ordering and component table. 775 has its own particle registry (117 entries against 774's 115) and, unlike 774, uses template stacks for item-particle options. The 776 table shifts every id from 7 onward and cannot be reused.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV26_1 =
        RadiusExplode(ParticleCodec.ModernV26_1, ItemPacketCodecShared.Table775);

    /// <summary>26.2 (776) explosion: the 773 frame with the 776 particle ordering (the geyser family) and the 776 component table.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV26_2 =
        RadiusExplode(ParticleCodec.ModernV26_2, ItemPacketCodecShared.Table776);

    /// <summary>26.3 (777) explosion: the 773 frame plus a trailing bool playSound after the weighted block-particle list, with the 777 particle ordering and the 777 component table.</summary>
    public static readonly PacketCodec<ClientboundExplodePacket> ExplodeV26_3 =
        RadiusExplode(ParticleCodec.ModernV26_3, ItemPacketCodecShared.Table777, hasPlaySoundField: true);

    /// <summary>The 773-777 explosion body, parameterized by the two particle-facing era tables. <paramref name="hasPlaySoundField"/> selects the 777 trailing bool after the weighted block-particle list; 773-776 carry no such field and always play the sound.</summary>
    private static PacketCodec<ClientboundExplodePacket> RadiusExplode(
        IReadOnlyDictionary<int, ParticleOptionShape> shapes, ItemComponentTable components, bool hasPlaySoundField = false) =>
        PacketCodec<ClientboundExplodePacket>.Of(
            (ref PacketWriter w, ClientboundExplodePacket p, PacketCodecContext _) =>
            {
                WriteVec3(ref w, p.Center);
                w.WriteFloat(p.Radius);
                w.WriteInt(p.BlockCount);
                w.WriteOptionalStruct(p.Knockback, static (ref PacketWriter ww, Vec3d v) => WriteVec3(ref ww, v));
                ParticleCodec.WriteModern(ref w, p.Particle ?? throw new ProtocolViolationException("A 1.21.9 explosion requires an explosion particle."));
                SoundCodec.WriteHolder(ref w, p.Sound ?? throw new ProtocolViolationException("A 1.21.9 explosion requires an explosion sound."));
                w.WriteList(p.BlockParticles, static (ref PacketWriter ew, ExplosionParticleInfo info) =>
                {
                    ParticleCodec.WriteModern(ref ew, info.Particle);
                    ew.WriteFloat(info.Scaling);
                    ew.WriteFloat(info.Speed);
                    ew.WriteVarInt(info.Weight);
                });
                if (hasPlaySoundField)
                    w.WriteBool(p.PlaySound);
            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                Vec3d center = ReadVec3(ref r);
                float radius = r.ReadFloat();
                int blockCount = r.ReadInt();
                Vec3d? knockback = ReadOptionalVec3(ref r);
                ParticleData particle = ParticleCodec.ReadModern(ref r, shapes, components, ctx);
                SoundEventHolder sound = SoundCodec.ReadHolder(ref r);
                ExplosionParticleInfo[] blockParticles = r.ReadList((ref PacketReader er) =>
                {
                    ParticleData bp = ParticleCodec.ReadModern(ref er, shapes, components, ctx);
                    return new ExplosionParticleInfo(bp, er.ReadFloat(), er.ReadFloat(), er.ReadVarInt());
                });
                bool play = !hasPlaySoundField || r.ReadBool();
                return new ClientboundExplodePacket(center, 0f, [], 0f, 0f, 0f, knockback, particle, sound, radius, blockCount, blockParticles) { PlaySound = play };
            },
            hasPlaySoundField
                ? WireShape.Of("vec3,float,int,optional_vec3,particle,sound,weighted_particles,bool")
                : WireShape.Opaque);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareExplode(PacketBindings bindings)
    {
        // The explosion frame has seven wire eras:
        //   47-754  1.8-1.16.5    float center, strength, INT-counted offsets, float motion
        //   755-760 1.17-1.19.2   the same with a VarInt-counted list
        //   761-764 1.19.3-1.20.2 the same with a DOUBLE center
        //   765-767 1.20.3-1.21.1 + block-interaction ordinal + (particle, particle, sound) tail
        //   768-772 1.21.2-1.21.8 center, optional knockback, particle, sound holder
        //   773-776 1.21.9-26.2   + radius, block count, weighted block-particle list
        //   777     26.3          + trailing bool playSound
        //
        // From 768, particle ids and item-particle payloads also follow the active particle registry and item-component table. The bindings therefore use the same boundaries as level-particles.
        bindings.Packet(WorldPackets.Clientbound.Explode)
            .From(JavaProtocols.V1_8, WorldEffectCodecs.ExplodeV1_8)
            .From(JavaProtocols.V1_17, WorldEffectCodecs.ExplodeV1_17)
            .From(JavaProtocols.V1_19_3, WorldEffectCodecs.ExplodeV1_19_3)
            .From(JavaProtocols.V1_20_3, WorldEffectCodecs.ExplodeV1_20_3)
            .From(JavaProtocols.V1_21_2, WorldEffectCodecs.ExplodeV1_21_2)
            .From(JavaProtocols.V1_21_4, WorldEffectCodecs.ExplodeV1_21_4)
            .From(JavaProtocols.V1_21_5, WorldEffectCodecs.ExplodeV1_21_5)
            .From(JavaProtocols.V1_21_6, WorldEffectCodecs.ExplodeV1_21_6)
            .From(JavaProtocols.V1_21_9, WorldEffectCodecs.ExplodeV1_21_9)
            .From(JavaProtocols.V1_21_11, WorldEffectCodecs.ExplodeV1_21_11)
            .From(JavaProtocols.V26_1, WorldEffectCodecs.ExplodeV26_1)
            .From(JavaProtocols.V26_2, WorldEffectCodecs.ExplodeV26_2)
            .From(JavaProtocols.V26_3, WorldEffectCodecs.ExplodeV26_3);
    }
}
