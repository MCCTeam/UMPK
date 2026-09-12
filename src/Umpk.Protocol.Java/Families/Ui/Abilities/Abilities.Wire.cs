using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    /// <summary>Legacy 1.8 serverbound player abilities (47): flags plus fly/walk speeds.</summary>
    public static readonly PacketCodec<ServerboundLegacyPlayerAbilitiesPacket> ServerLegacyAbilitiesV1_8 =
        PacketCodec<ServerboundLegacyPlayerAbilitiesPacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyPlayerAbilitiesPacket p, PacketCodecContext _) =>
            {
                w.WriteByte(p.Flags);
                w.WriteFloat(p.FlyingSpeed);
                w.WriteFloat(p.WalkingSpeed);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundLegacyPlayerAbilitiesPacket(r.ReadByte(), r.ReadFloat(), r.ReadFloat()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAbilities(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyAbilities)
            .From(JavaProtocols.V1_8, UiMiscCodecs.PlayerAbilitiesV1_8);

        bindings.Packet(UiPackets.Serverbound.LegacyAbilities)
            .From(JavaProtocols.V1_8, UiMiscCodecs.ServerLegacyAbilitiesV1_8);
    }
}
