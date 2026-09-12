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
    /// <summary>1.8 named sound: sound name string, fixed-point x/y/z ints, volume float, pitch byte (x63).</summary>
    public static readonly PacketCodec<ClientboundNamedSoundPacket> NamedSoundV1_8 =
        PacketCodec<ClientboundNamedSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundNamedSoundPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.SoundName, 256);
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                w.WriteFloat(p.Volume);
                w.WriteByte(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundNamedSoundPacket(
                r.ReadString(256), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadFloat(), r.ReadByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSoundEffect(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.NamedSound)
            .From(JavaProtocols.V1_8, WorldEffectCodecs.NamedSoundV1_8);
    }
}
