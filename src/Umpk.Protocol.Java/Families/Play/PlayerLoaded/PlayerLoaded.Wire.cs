using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Player loaded: an empty body (<c>StreamCodec.unit</c>).</summary>
    public static readonly PacketCodec<ServerboundPlayerLoadedPacket> PlayerLoaded =
        PacketCodec<ServerboundPlayerLoadedPacket>.Of(
            static (ref PacketWriter _, ServerboundPlayerLoadedPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundPlayerLoadedPacket(),
            WireShape.Of("empty"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerLoaded(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Serverbound.PlayerLoaded)
            .From(JavaProtocols.V1_21_4, PlayCommonCodecs.PlayerLoaded);
    }
}
