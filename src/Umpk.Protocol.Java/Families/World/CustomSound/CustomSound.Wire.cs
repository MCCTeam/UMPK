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
    /// <summary>1.9 through 1.9.4 custom sound: sound name string, source enum, fixed-point x/y/z ints, volume float, and a PITCH BYTE.</summary>
    /// <remarks>The custom sound is a genuinely different packet from <c>minecraft:sound</c>, not a rename of it: it addresses the sound by resource location where <c>sound</c> uses a registry id, and vanilla carried both side by side from 1.9 until the 1.19.3 holder merge. The custom form begins with a string source and the registry form with a VarInt id; their remaining fields are identical. It is also not the 1.8 <c>sound_effect</c>, which has no source field at all. Pitch follows the same 1.9-versus-1.10 widening as <see cref="SoundV1_9"/> and uses the same raw-byte-held-in-a-float convention, exact for every value the wire can carry.</remarks>
    public static readonly PacketCodec<ClientboundCustomSoundPacket> CustomSoundV1_9 =
        PacketCodec<ClientboundCustomSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundCustomSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.SoundName, 256);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteByte((byte)p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundCustomSoundPacket(
                r.ReadString(256), r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(),
                r.ReadFloat(), r.ReadByte(), Seed: 0L));

    /// <summary>1.10 through 1.18.2 custom sound: the 1.9 body with the pitch widened to a float, and nothing else moved across the whole band.</summary>
    public static readonly PacketCodec<ClientboundCustomSoundPacket> CustomSoundV1_10 =
        PacketCodec<ClientboundCustomSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundCustomSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.SoundName, 256);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteFloat(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundCustomSoundPacket(
                r.ReadString(256), r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(),
                r.ReadFloat(), r.ReadFloat(), Seed: 0L));

    /// <summary>1.19 and 1.19.1 custom sound: the 1.10 body plus the trailing variant-selection seed long that 1.19 added to the sound family, appended where 1.18.2's stops. The packet leaves the protocol at 1.19.3.</summary>
    public static readonly PacketCodec<ClientboundCustomSoundPacket> CustomSoundV1_19 =
        PacketCodec<ClientboundCustomSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundCustomSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.SoundName, 256);
                w.WriteVarInt(p.Source);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteFloat(p.Pitch);
                w.WriteLong(p.Seed);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundCustomSoundPacket(
                r.ReadString(256), r.ReadVarInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(),
                r.ReadFloat(), r.ReadFloat(), r.ReadLong()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomSound(PacketBindings bindings)
    {
        // custom_sound was a 32-protocol marker (107-760) and it is a REAL era gap, not an alias: it addresses the sound by resource location where the bound minecraft:sound uses a registry id, and vanilla shipped the two side by side until the 1.19.3 holder merge removed this one. So it gets its own timeline, and that timeline splits exactly where the sibling's does: pitch byte through 1.9.4, pitch float from 1.10, plus the 1.19 seed long. See the codec remarks for the per-era anchors. What a consumer gains is every server-defined (resource-pack) sound, which is most of what a modded or plugin server plays.
        bindings.Packet(WorldPackets.Clientbound.CustomSound)
            .From(JavaProtocols.V1_9, WorldEffectCodecs.CustomSoundV1_9)
            .From(JavaProtocols.V1_10, WorldEffectCodecs.CustomSoundV1_10)
            .From(JavaProtocols.V1_19, WorldEffectCodecs.CustomSoundV1_19);
    }
}
