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
    /// <summary>Item cooldown, 107-767 (1.9-1.21.1): a VarInt ITEM registry id and a VarInt duration in ticks. The cooldown-group identifier the modern codec writes only arrived at 1.21.2, when the packet moved from a per-item to a per-group cooldown.</summary>
    /// <remarks>The earlier form is a VarInt item id and VarInt duration; 1.21.2 changes it to a resource-location group identifier and duration. Decoding one of the earlier frames with the modern codec reads the item id's first byte as a UTF length prefix, so a common item id turns into a garbage identifier or a fault.</remarks>
    public static readonly PacketCodec<ClientboundCooldownPacket> CooldownV1_9 =
        PacketCodec<ClientboundCooldownPacket>.Of(
            static (ref PacketWriter w, ClientboundCooldownPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ItemId ?? throw new ProtocolViolationException("A 1.9-1.13.2 cooldown requires an item id."));
                w.WriteVarInt(p.Ticks);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int itemId = r.ReadVarInt();
                return new ClientboundCooldownPacket(default, r.ReadVarInt(), itemId);
            });

    /// <summary>Item cooldown (770/776): a cooldown-group identifier and duration in ticks.</summary>
    public static readonly PacketCodec<ClientboundCooldownPacket> CooldownV1_21_2 =
        PacketCodec<ClientboundCooldownPacket>.Of(
            static (ref PacketWriter w, ClientboundCooldownPacket p, PacketCodecContext _) =>
            {
                WriteIdentifier(ref w, p.CooldownGroup);
                w.WriteVarInt(p.Ticks);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundCooldownPacket(ReadIdentifier(ref r), r.ReadVarInt(), ItemId: null));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCooldown(PacketBindings bindings)
    {
        // 107-767 is a per-ITEM cooldown carrying the item's VarInt registry id; the cooldown-GROUP identifier the modern codec writes arrives at 1.21.2 (768), when the packet moved from a per-item to a per-group cooldown. Binding the group codec from 477 read the item id's first byte as a UTF length prefix, so a common item id became a garbage identifier or a fault: loud on 24 protocols.
        bindings.Packet(UiPackets.Clientbound.Cooldown)
            .From(JavaEras.Combat, UiMiscCodecs.CooldownV1_9)
            .From(JavaEras.WideIds, UiMiscCodecs.CooldownV1_21_2);
    }
}
