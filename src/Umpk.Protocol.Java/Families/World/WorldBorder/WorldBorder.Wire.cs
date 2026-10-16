using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBorderCodecs
{
    /// <summary>1.8 combined world border, with a VarInt action selecting its payload.</summary>
    public static readonly PacketCodec<ClientboundWorldBorderPacket> WorldBorderV1_8 =
        PacketCodec<ClientboundWorldBorderPacket>.Of(
            static (ref PacketWriter w, ClientboundWorldBorderPacket p, PacketCodecContext _) =>
                WriteCombined(ref w, new BorderPayload(p.Action, p.CenterX, p.CenterZ, p.OldSize, p.NewSize, p.LerpTime, p.PortalTeleportBoundary, p.WarningTime, p.WarningBlocks)),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                BorderPayload p = ReadCombined(ref r);
                return new ClientboundWorldBorderPacket(p.Action, p.CenterX, p.CenterZ, p.OldSize, p.NewSize, p.LerpTime, p.PortalTeleportBoundary, p.WarningTime, p.WarningBlocks);
            });

    /// <summary>1.9 through 1.16.5 combined border. Native readers and writers in 1.14.4 through 1.16.5 preserve the 1.8 action order and payload layout.</summary>
    public static readonly PacketCodec<ClientboundSetBorderPacket> SetBorderV1_9 =
        PacketCodec<ClientboundSetBorderPacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderPacket p, PacketCodecContext _) =>
                WriteCombined(ref w, new BorderPayload(p.Action, p.CenterX, p.CenterZ, p.OldSize, p.NewSize, p.LerpTime, p.PortalTeleportBoundary, p.WarningTime, p.WarningBlocks)),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                BorderPayload p = ReadCombined(ref r);
                return new ClientboundSetBorderPacket(p.Action, p.CenterX, p.CenterZ, p.OldSize, p.NewSize, p.LerpTime, p.PortalTeleportBoundary, p.WarningTime, p.WarningBlocks);
            });

    private readonly record struct BorderPayload(WorldBorderAction Action, double CenterX, double CenterZ, double OldSize, double NewSize, long LerpTime, int PortalTeleportBoundary, int WarningTime, int WarningBlocks);

    private static void WriteCombined(ref PacketWriter w, BorderPayload p)
    {
        w.WriteVarInt((int)p.Action);
        switch (p.Action)
        {
            case WorldBorderAction.SetSize:
                w.WriteDouble(p.NewSize);
                break;
            case WorldBorderAction.LerpSize:
                w.WriteDouble(p.OldSize);
                w.WriteDouble(p.NewSize);
                w.WriteVarLong(p.LerpTime);
                break;
            case WorldBorderAction.SetCenter:
                w.WriteDouble(p.CenterX);
                w.WriteDouble(p.CenterZ);
                break;
            case WorldBorderAction.Initialize:
                w.WriteDouble(p.CenterX);
                w.WriteDouble(p.CenterZ);
                w.WriteDouble(p.OldSize);
                w.WriteDouble(p.NewSize);
                w.WriteVarLong(p.LerpTime);
                w.WriteVarInt(p.PortalTeleportBoundary);
                w.WriteVarInt(p.WarningBlocks);
                w.WriteVarInt(p.WarningTime);
                break;
            case WorldBorderAction.SetWarningTime:
                w.WriteVarInt(p.WarningTime);
                break;
            case WorldBorderAction.SetWarningBlocks:
                w.WriteVarInt(p.WarningBlocks);
                break;
            default:
                throw new ProtocolViolationException($"Unknown world-border action {p.Action}.");
        }
    }

    private static BorderPayload ReadCombined(ref PacketReader r)
    {
        var action = (WorldBorderAction)r.ReadVarInt();
        double centerX = 0, centerZ = 0, oldSize = 0, newSize = 0;
        long lerpTime = 0;
        int portal = 0, warningBlocks = 0, warningTime = 0;
        switch (action)
        {
            case WorldBorderAction.SetSize:
                newSize = r.ReadDouble();
                break;
            case WorldBorderAction.LerpSize:
                oldSize = r.ReadDouble();
                newSize = r.ReadDouble();
                lerpTime = r.ReadVarLong();
                break;
            case WorldBorderAction.SetCenter:
                centerX = r.ReadDouble();
                centerZ = r.ReadDouble();
                break;
            case WorldBorderAction.Initialize:
                centerX = r.ReadDouble();
                centerZ = r.ReadDouble();
                oldSize = r.ReadDouble();
                newSize = r.ReadDouble();
                lerpTime = r.ReadVarLong();
                portal = r.ReadVarInt();
                warningBlocks = r.ReadVarInt();
                warningTime = r.ReadVarInt();
                break;
            case WorldBorderAction.SetWarningTime:
                warningTime = r.ReadVarInt();
                break;
            case WorldBorderAction.SetWarningBlocks:
                warningBlocks = r.ReadVarInt();
                break;
            default:
                throw new ProtocolViolationException($"Unknown world-border action {(int)action}.");
        }

        return new BorderPayload(action, centerX, centerZ, oldSize, newSize, lerpTime, portal, warningTime, warningBlocks);
    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareWorldBorder(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.WorldBorder)
            .From(JavaProtocols.V1_8, WorldBorderCodecs.WorldBorderV1_8);
        bindings.Packet(WorldPackets.Clientbound.SetBorder)
            .From(JavaProtocols.V1_9, WorldBorderCodecs.SetBorderV1_9);
    }
}
