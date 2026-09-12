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
    /// <summary>1.8 game state change: state byte + float value.</summary>
    public static readonly PacketCodec<ClientboundGameEventPacket> GameEventV1_8 = MakeGameEvent();

    /// <summary>Modern game event: event byte + float param (identical wire to 1.8).</summary>
    public static readonly PacketCodec<ClientboundGameEventPacket> GameEventV1_14 = MakeGameEvent();

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareGameEvent(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.GameEvent)
            .From(JavaProtocols.V1_8, WorldStateCodecs.GameEventV1_8)
            .From(JavaProtocols.V1_14, WorldStateCodecs.GameEventV1_14)
            .AliasedAs(Identifier.Minecraft("game_state_change"));
    }
}
