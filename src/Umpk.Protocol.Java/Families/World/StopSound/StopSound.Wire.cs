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
    /// <summary>Modern stop sound: a flags byte (bit1 source present, bit2 name present) then the present fields.</summary>
    public static readonly PacketCodec<ClientboundStopSoundPacket> StopSoundV1_13 =
        PacketCodec<ClientboundStopSoundPacket>.Of(
            static (ref PacketWriter w, ClientboundStopSoundPacket p, PacketCodecContext _) =>
            {
                int flags = (p.Source is not null ? 1 : 0) | (p.Name is not null ? 2 : 0);
                w.WriteByte((byte)flags);
                if (p.Source is { } src)
                    w.WriteVarInt(src);

                if (p.Name is { } name)
                    w.WriteString(name);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                byte flags = r.ReadByte();
                int? source = (flags & 1) != 0 ? r.ReadVarInt() : null;
                string? name = (flags & 2) != 0 ? r.ReadString() : null;
                return new ClientboundStopSoundPacket(source, name);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareStopSound(PacketBindings bindings)
    {
        // The dedicated stop-sound packet arrives at 1.13 (before that the client used the MC|StopSound plugin channel) and its wire never changed: a flags byte, then the VarInt source only when bit 0 is set and the resource-location name only when bit 1 is set. The 393-404 marker was a gap rather than an era.
        bindings.Packet(WorldPackets.Clientbound.StopSound)
            .From(JavaEras.Flattening, WorldEffectCodecs.StopSoundV1_13);
    }
}
