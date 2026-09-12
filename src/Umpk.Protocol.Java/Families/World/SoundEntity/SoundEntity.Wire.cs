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
    /// <summary>Modern entity sound: holder-or-inline sound, source enum, VarInt entity id, volume, pitch, seed.</summary>
    public static readonly PacketCodec<ClientboundSoundEntityPacket> SoundEntityV1_14 =
        PacketCodec<ClientboundSoundEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundSoundEntityPacket p, PacketCodecContext _) =>
            {
                SoundCodec.WriteHolder(ref w, p.Sound);
                w.WriteVarInt(p.Source);
                w.WriteVarInt(p.EntityId);
                w.WriteFloat(p.Volume);
                w.WriteFloat(p.Pitch);
                w.WriteLong(p.Seed);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSoundEntityPacket(
                SoundCodec.ReadHolder(ref r), r.ReadVarInt(), r.ReadVarInt(), r.ReadFloat(), r.ReadFloat(), r.ReadLong()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSoundEntity(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SoundEntity)
            .From(JavaEras.Palettes, WorldEffectCodecs.SoundEntityV1_14);
    }
}
