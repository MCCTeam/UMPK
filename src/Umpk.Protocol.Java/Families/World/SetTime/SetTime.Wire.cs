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
    /// <summary>1.8 time update: world age long + day time long.</summary>
    public static readonly PacketCodec<ClientboundSetTimePacket> SetTimeV1_8 =
        PacketCodec<ClientboundSetTimePacket>.Of(
            static (ref PacketWriter w, ClientboundSetTimePacket p, PacketCodecContext _) =>
            {
                w.WriteLong(p.GameTime);
                w.WriteLong(p.DayTime);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetTimePacket(r.ReadLong(), r.ReadLong(), TickDayTime: false, ClockUpdates: []));

    /// <summary>1.21.5 time update: game time long, day time long, tick-day-time flag.</summary>
    public static readonly PacketCodec<ClientboundSetTimePacket> SetTimeV1_21_2 =
        PacketCodec<ClientboundSetTimePacket>.Of(
            static (ref PacketWriter w, ClientboundSetTimePacket p, PacketCodecContext _) =>
            {
                w.WriteLong(p.GameTime);
                w.WriteLong(p.DayTime);
                w.WriteBool(p.TickDayTime);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetTimePacket(r.ReadLong(), r.ReadLong(), r.ReadBool(), ClockUpdates: []));

    /// <summary>26.1+ time update: game time long, then a per-world-clock map. The clock map is host state (a WorldClock registry not synced through the codec context); the payload is captured as raw bytes so it round-trips frame-exact. The clock-map rework landed at 26.1, not 26.2. Version 1.21.11 still has the long/long/bool form, while 26.1 and 26.2 use the same clock-map form.</summary>
    public static readonly PacketCodec<ClientboundSetTimePacket> SetTimeV26_1 =
        PacketCodec<ClientboundSetTimePacket>.Of(
            static (ref PacketWriter w, ClientboundSetTimePacket p, PacketCodecContext _) =>
            {
                w.WriteLong(p.GameTime);
                w.WriteBytes(p.ClockUpdates ?? []);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                long gameTime = r.ReadLong();
                byte[] clockUpdates = r.ReadRemaining().ToArray();
                return new ClientboundSetTimePacket(gameTime, DayTime: 0, TickDayTime: false, clockUpdates);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetTime(PacketBindings bindings)
    {
        // Two longs through 1.21.1; the trailing tick-day-time bool arrived at 1.21.2, and the clock-map rework at 26.1 (26.1 == 26.2; 1.21.11 still carries the long/long/bool form). 1.8 spells it update_time.
        bindings.Packet(WorldPackets.Clientbound.SetTime)
            .From(JavaProtocols.V1_8, WorldStateCodecs.SetTimeV1_8)
            .From(JavaProtocols.V1_21_2, WorldStateCodecs.SetTimeV1_21_2)
            .From(JavaProtocols.V26_1, WorldStateCodecs.SetTimeV26_1)
            .AliasedAs(Identifier.Minecraft("update_time"));
    }
}
