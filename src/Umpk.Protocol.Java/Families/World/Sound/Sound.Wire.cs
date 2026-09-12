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
    /// <summary>Modern sound: holder-or-inline sound event, source enum VarInt, fixed-point x/y/z ints, volume, pitch, seed.</summary>
    public static readonly PacketCodec<ClientboundSoundPacket> SoundV1_19 =
        PacketCodec<ClientboundSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundSoundPacket p, PacketCodecContext _) =>
            {
                SoundCodec.WriteHolder(ref w, p.Sound);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteFloat(p.Pitch);
                w.WriteLong(p.Seed);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSoundPacket(
                SoundCodec.ReadHolder(ref r), r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(),
                r.ReadFloat(), r.ReadFloat(), r.ReadLong()));

    /// <summary>1.14-1.15.2 (477-578) sound: a plain VarInt sound-event registry id (no holder-or-inline scheme, which is 1.19.3+), source enum VarInt, fixed-point int x/y/z, float volume/pitch, and no seed.</summary>
    public static readonly PacketCodec<ClientboundSoundPacket> SoundV1_10 =
        PacketCodec<ClientboundSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Sound.SoundId);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteFloat(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSoundPacket(
                new SoundEventHolder(r.ReadVarInt(), InlineName: null, FixedRange: null),
                r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadFloat(), r.ReadFloat(), Seed: 0L));

    /// <summary>1.9-1.9.4 (107-110) sound: VarInt sound-event registry id, source enum VarInt, fixed-point int x/y/z, float volume, and a PITCH BYTE. 1.10 widened the pitch to a float and nothing else changed, which is why <see cref="SoundV1_10"/> covers 210 onward. The 1.9 sender packs the pitch as <c>(int)(pitch * 63.5F)</c> clamped to 0..255.</summary>
    /// <remarks>The shared packet record carries a float pitch, so this era holds the raw wire byte in it: decode widens the 0..255 byte, encode narrows it back. That is byte-exact for every value the wire can carry, and it keeps the 1.9 band on the same record as every other era rather than inventing a parallel one. It is the same convention <see cref="NamedSoundV1_8"/> already uses for the 1.8 named-sound pitch.</remarks>
    public static readonly PacketCodec<ClientboundSoundPacket> SoundV1_9 =
        PacketCodec<ClientboundSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Sound.SoundId);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteByte((byte)p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSoundPacket(
                new SoundEventHolder(r.ReadVarInt(), InlineName: null, FixedRange: null),
                r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadFloat(), r.ReadByte(), Seed: 0L));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSound(PacketBindings bindings)
    {
        // 1.9-1.9.4 (107-110): VarInt sound id + source + fixed-point int position + volume float and a PITCH BYTE. 1.10 widened the pitch to a float and nothing else moved, so one form covers 210-578. 1.16-1.18.2 reshaped it (seed) with no codec yet, so it relays verbatim there; the shared modern form lands at 1.19. The marker step overrides the earlier bind rather than extending it, which is the whole of the resolution rule in one packet.
        // BEGIN GENERATED BANDS Play Clientbound minecraft:sound
        //   107-110  WorldEffectCodecs.SoundV1_9
        //   210-578  WorldEffectCodecs.SoundV1_10
        //   735-758  marker
        //   759-     WorldEffectCodecs.SoundV1_19
        // END GENERATED BANDS
        bindings.Packet(WorldPackets.Clientbound.Sound)
            .From(JavaProtocols.V1_9, WorldEffectCodecs.SoundV1_9)
            .From(JavaProtocols.V1_10, WorldEffectCodecs.SoundV1_10)
            .MarkerFrom(
                JavaProtocols.V1_16,
                MarkerReason.WrongCodecWouldBeWorse,
                "1.16 reshaped the positional sound packet, which gained the random seed, and no codec models that form, so the band relays verbatim. Both neighbouring codecs mis-frame it, and being bound on BOTH sides of a marker band is exactly the shape a real misbinding has, so the reason matters more here than anywhere else.")
            .From(JavaProtocols.V1_19, WorldEffectCodecs.SoundV1_19);
    }
}
