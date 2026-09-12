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
    /// <summary>Tracked waypoint (1.21.6+).</summary>
    public static readonly PacketCodec<ClientboundWaypointPacket> WaypointV1_21_6 =
        PacketCodec<ClientboundWaypointPacket>.Of(
            static (ref PacketWriter w, ClientboundWaypointPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt((int)p.Operation);
                // identifier: Either<UUID, String> (writeEither: bool true => left/UUID).
                if (p.IdentifierIsUuid)
                {
                    w.WriteBool(true);
                    w.WriteUuid(p.IdentifierUuid);
                }
                else
                {
                    w.WriteBool(false);
                    w.WriteString(p.IdentifierString ?? throw new ProtocolViolationException("A string-identified waypoint requires a string."));
                }

                // icon: style id + optional packed-RGB color (three bytes).
                WriteIdentifier(ref w, p.IconStyle);
                w.WriteOptionalStruct(p.IconColor, static (ref PacketWriter sw, int c) =>
                {
                    sw.WriteByte((byte)((c >> 16) & 0xFF));
                    sw.WriteByte((byte)((c >> 8) & 0xFF));
                    sw.WriteByte((byte)(c & 0xFF));
                });
                w.WriteVarInt((int)p.Kind);
                switch (p.Kind)
                {
                    case WaypointKind.Vec3i:
                        w.WriteVarInt(p.X);
                        w.WriteVarInt(p.Y);
                        w.WriteVarInt(p.Z);
                        break;
                    case WaypointKind.Chunk:
                        w.WriteVarInt(p.X);
                        w.WriteVarInt(p.Z);
                        break;
                    case WaypointKind.Azimuth:
                        w.WriteFloat(p.Azimuth);
                        break;
                    case WaypointKind.Empty:
                    default:
                        break;
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                var op = (WaypointOperation)r.ReadVarInt();
                bool isUuid = r.ReadBool();
                Guid uuid = isUuid ? r.ReadUuid() : Guid.Empty;
                string? str = isUuid ? null : r.ReadString();
                Identifier style = ReadIdentifier(ref r);
                int? color = r.ReadBool()
                    ? (r.ReadByte() << 16) | (r.ReadByte() << 8) | r.ReadByte()
                    : null;
                var kind = (WaypointKind)r.ReadVarInt();
                int x = 0, y = 0, z = 0;
                float azimuth = 0f;
                switch (kind)
                {
                    case WaypointKind.Vec3i:
                        x = r.ReadVarInt();
                        y = r.ReadVarInt();
                        z = r.ReadVarInt();
                        break;
                    case WaypointKind.Chunk:
                        x = r.ReadVarInt();
                        z = r.ReadVarInt();
                        break;
                    case WaypointKind.Azimuth:
                        azimuth = r.ReadFloat();
                        break;
                    case WaypointKind.Empty:
                    default:
                        break;
                }

                return new ClientboundWaypointPacket(op, isUuid, uuid, str, style, color, kind, x, y, z, azimuth);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareWaypoint(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.Waypoint)
            .From(JavaProtocols.V1_21_6, UiMiscCodecs.WaypointV1_21_6);
    }
}
