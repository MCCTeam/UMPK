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

/// <summary>Boss-bar (boss event) codec.</summary>
public static partial class BossBarCodecs
{
    /// <summary>
    /// Boss event, 477-764 (1.14-1.20.2): the title is a JSON-string component.
    /// <para>The body is otherwise constant across the whole 1.14-26.2 range: uuid, VarInt operation, then per operation ADD (title, float progress, VarInt color, VarInt overlay, byte flags), UPDATE_PROGRESS (float), UPDATE_NAME (title), UPDATE_STYLE (color, overlay), UPDATE_PROPERTIES (flags), REMOVE (nothing). Only the component encoding moves, which is why the three era members share one body.</para>
    /// </summary>
    public static readonly PacketCodec<ClientboundBossEventPacket> BossEventV1_9 = Make(ComponentWire.V1_8);

    /// <summary>Boss event, 765-769 (1.20.3-1.21.4): network-NBT title, legacy click/hover interactions. Both use the same NBT transport, and Version 1.21.4 still serializes <c>clickEvent</c>/<c>hoverEvent</c>.</summary>
    public static readonly PacketCodec<ClientboundBossEventPacket> BossEventV1_20_3 = Make(ComponentWire.V1_20_3);

    /// <summary>Boss event (770/776): network-NBT title, modern click/hover interactions.</summary>
    public static readonly PacketCodec<ClientboundBossEventPacket> BossEventV1_21_5 = Make(ComponentWire.V1_21_5);

    // The single boss-event body; the era's component pair is bound once at construction.
    private static PacketCodec<ClientboundBossEventPacket> Make(ComponentWire text) =>
        PacketCodec<ClientboundBossEventPacket>.Of(
            (ref PacketWriter w, ClientboundBossEventPacket p, PacketCodecContext _) =>
            {
                w.WriteUuid(p.Id);
                w.WriteVarInt((int)p.Operation);
                switch (p.Operation)
                {
                    case BossEventOperation.Add:
                        text.Write(ref w, p.Title ?? throw new ProtocolViolationException("Add boss event requires a title."));
                        w.WriteFloat(p.Progress);
                        w.WriteVarInt((int)p.Color);
                        w.WriteVarInt((int)p.Overlay);
                        w.WriteByte((byte)p.Flags);
                        break;
                    case BossEventOperation.UpdateProgress:
                        w.WriteFloat(p.Progress);
                        break;
                    case BossEventOperation.UpdateName:
                        text.Write(ref w, p.Title ?? throw new ProtocolViolationException("Update-name boss event requires a title."));
                        break;
                    case BossEventOperation.UpdateStyle:
                        w.WriteVarInt((int)p.Color);
                        w.WriteVarInt((int)p.Overlay);
                        break;
                    case BossEventOperation.UpdateProperties:
                        w.WriteByte((byte)p.Flags);
                        break;
                    case BossEventOperation.Remove:
                    default:
                        break;
                }
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Guid id = r.ReadUuid();
                var op = (BossEventOperation)r.ReadVarInt();
                Component? title = null;
                float progress = 0f;
                var color = BossBarColor.Pink;
                var overlay = BossBarOverlay.Progress;
                BossBarFlags flags = BossBarFlags.None;
                switch (op)
                {
                    case BossEventOperation.Add:
                        title = text.Read(ref r);
                        progress = r.ReadFloat();
                        color = (BossBarColor)r.ReadVarInt();
                        overlay = (BossBarOverlay)r.ReadVarInt();
                        flags = (BossBarFlags)r.ReadByte();
                        break;
                    case BossEventOperation.UpdateProgress:
                        progress = r.ReadFloat();
                        break;
                    case BossEventOperation.UpdateName:
                        title = text.Read(ref r);
                        break;
                    case BossEventOperation.UpdateStyle:
                        color = (BossBarColor)r.ReadVarInt();
                        overlay = (BossBarOverlay)r.ReadVarInt();
                        break;
                    case BossEventOperation.UpdateProperties:
                        flags = (BossBarFlags)r.ReadByte();
                        break;
                    case BossEventOperation.Remove:
                    default:
                        break;
                }

                return new ClientboundBossEventPacket(id, op, title, progress, color, overlay, flags);
            },
            WireShape.Of("uuid,varint,per_operation_fields", text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBossEvent(PacketBindings bindings)
    {
        // The title follows the component transport boundaries: JSON through 764, network NBT from 765, and the modern click/hover representation from 770. Protocols 107-764 otherwise share the UUID, VarInt operation, and per-operation field layout.
        bindings.Packet(UiPackets.Clientbound.BossEvent)
            .From(JavaEras.Combat, BossBarCodecs.BossEventV1_9)
            .From(JavaEras.ComponentNbtTransport, BossBarCodecs.BossEventV1_20_3)
            .From(JavaEras.ModernComponents, BossBarCodecs.BossEventV1_21_5);
    }
}
