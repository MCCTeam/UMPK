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
    /// <summary>1.8 combined world border: a VarInt action then the action-specific fields. Action ordinals: SET_SIZE 0, LERP_SIZE 1, SET_CENTER 2, INITIALIZE 3, SET_WARNING_TIME 4, SET_WARNING_BLOCKS 5. This ordering matches the protocol 47 layout.</summary>
    public static readonly PacketCodec<ClientboundWorldBorderPacket> WorldBorderV1_8 =
        PacketCodec<ClientboundWorldBorderPacket>.Of(
            static (ref PacketWriter w, ClientboundWorldBorderPacket p, PacketCodecContext _) =>
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
            },
            static (ref PacketReader r, PacketCodecContext _) =>
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

                return new ClientboundWorldBorderPacket(action, centerX, centerZ, oldSize, newSize, lerpTime, portal, warningTime, warningBlocks);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareWorldBorder(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.WorldBorder)
            .From(JavaProtocols.V1_8, WorldBorderCodecs.WorldBorderV1_8);
    }
}
