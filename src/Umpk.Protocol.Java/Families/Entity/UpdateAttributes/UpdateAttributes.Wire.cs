using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityEquipmentCodecs
{
    /// <summary>1.8 update attributes: 4-byte count, string keys, and UUID modifiers.</summary>
    public static readonly PacketCodec<ClientboundUpdateAttributesPacket> UpdateAttributesV1_8 =
        PacketCodec<ClientboundUpdateAttributesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateAttributesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteInt(p.Attributes.Count);
                foreach (AttributeSnapshot a in p.Attributes)
                {
                    w.WriteString(a.LegacyKey ?? string.Empty);
                    w.WriteDouble(a.BaseValue);
                    w.WriteList(a.Modifiers, static (ref PacketWriter ww, AttributeModifierEntry m) =>
                    {
                        ww.WriteUuid(m.LegacyUuid);
                        ww.WriteDouble(m.Amount);
                        ww.WriteByte((byte)m.Operation);
                    });
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int count = r.ReadInt();
                var list = new AttributeSnapshot[count];
                for (int i = 0; i < count; i++)
                {
                    string key = r.ReadString();
                    double value = r.ReadDouble();
                    var mods = r.ReadList(static (ref PacketReader rr) =>
                        new AttributeModifierEntry(rr.ReadUuid(), null, rr.ReadDouble(), rr.ReadByte()));
                    list[i] = new AttributeSnapshot(key, 0, value, mods);
                }

                return new ClientboundUpdateAttributesPacket(id, list);
            });

    /// <summary>Modern update attributes: VarInt count, VarInt holder id, namespaced modifier ids.</summary>
    public static readonly PacketCodec<ClientboundUpdateAttributesPacket> UpdateAttributesV1_21 =
        PacketCodec<ClientboundUpdateAttributesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateAttributesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteList(p.Attributes, static (ref PacketWriter ww, AttributeSnapshot a) =>
                {
                    ww.WriteVarInt(a.ModernId);
                    ww.WriteDouble(a.BaseValue);
                    ww.WriteList(a.Modifiers, static (ref PacketWriter www, AttributeModifierEntry m) =>
                    {
                        www.WriteString(m.ModernId ?? string.Empty);
                        www.WriteDouble(m.Amount);
                        www.WriteByte((byte)m.Operation);
                    });
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                var list = r.ReadList(static (ref PacketReader rr) =>
                {
                    int holder = rr.ReadVarInt();
                    double value = rr.ReadDouble();
                    var mods = rr.ReadList(static (ref PacketReader rrr) =>
                        new AttributeModifierEntry(Guid.Empty, rrr.ReadString(), rrr.ReadDouble(), rrr.ReadVarInt()));
                    return new AttributeSnapshot(null, holder, value, mods);
                });
                return new ClientboundUpdateAttributesPacket(id, list);
            });

    /// <summary>update_attributes for 1.20.5/1.20.6 (protocols 766): the attribute id is the modern registry holder VarInt (1.20.5 moved attributes into a registry), but the modifier still carries a UUID and a byte operation - the modifier id only became a namespaced Identifier (and the operation a VarInt) at 1.21. So this is the V1_16 modifier shape with the V1_21_5 holder-VarInt attribute id.</summary>
    public static readonly PacketCodec<ClientboundUpdateAttributesPacket> UpdateAttributesV1_20_5 =
        PacketCodec<ClientboundUpdateAttributesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateAttributesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteList(p.Attributes, static (ref PacketWriter ww, AttributeSnapshot a) =>
                {
                    ww.WriteVarInt(a.ModernId);
                    ww.WriteDouble(a.BaseValue);
                    ww.WriteList(a.Modifiers, static (ref PacketWriter www, AttributeModifierEntry m) =>
                    {
                        www.WriteUuid(m.LegacyUuid);
                        www.WriteDouble(m.Amount);
                        www.WriteByte((byte)m.Operation);
                    });
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                var list = r.ReadList(static (ref PacketReader rr) =>
                {
                    int holder = rr.ReadVarInt();
                    double value = rr.ReadDouble();
                    var mods = rr.ReadList(static (ref PacketReader rrr) =>
                        new AttributeModifierEntry(rrr.ReadUuid(), null, rrr.ReadDouble(), rrr.ReadByte()));
                    return new AttributeSnapshot(null, holder, value, mods);
                });
                return new ClientboundUpdateAttributesPacket(id, list);
            });

    /// <summary>1.16-1.20.4 update attributes: VarInt id + list of { string attribute key (ResourceLocation) + double base + list of { UUID + double amount + byte operation } }. This is the 1.8 body but with the outer count as a VarInt list (the 1.8 member uses a 4-byte int count, so it cannot be reused). Routed for 764/765 ONLY: 766/767 (1.20.5+) encode the attribute id as a holder VarInt and correctly ride the 1.21.5 member.</summary>
    public static readonly PacketCodec<ClientboundUpdateAttributesPacket> UpdateAttributesV1_17 =
        PacketCodec<ClientboundUpdateAttributesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateAttributesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteList(p.Attributes, static (ref PacketWriter ww, AttributeSnapshot a) =>
                {
                    ww.WriteString(a.LegacyKey ?? string.Empty);
                    ww.WriteDouble(a.BaseValue);
                    ww.WriteList(a.Modifiers, static (ref PacketWriter www, AttributeModifierEntry m) =>
                    {
                        www.WriteUuid(m.LegacyUuid);
                        www.WriteDouble(m.Amount);
                        www.WriteByte((byte)m.Operation);
                    });
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                var list = r.ReadList(static (ref PacketReader rr) =>
                {
                    string key = rr.ReadString();
                    double value = rr.ReadDouble();
                    var mods = rr.ReadList(static (ref PacketReader rrr) =>
                        new AttributeModifierEntry(rrr.ReadUuid(), null, rrr.ReadDouble(), rrr.ReadByte()));
                    return new AttributeSnapshot(key, 0, value, mods);
                });
                return new ClientboundUpdateAttributesPacket(id, list);
            });

    /// <summary>update_attributes for 1.16-1.16.5 (protocols 735-754): identical to <see cref="UpdateAttributesV1_17"/> except the OUTER attribute list uses a 4-byte int32 count, not a VarInt (the list codec switched to VarInt-counted <c>readList</c> at 1.17). The inner modifier list is VarInt-counted on all of them.</summary>
    public static readonly PacketCodec<ClientboundUpdateAttributesPacket> UpdateAttributesV1_16IntCount =
        PacketCodec<ClientboundUpdateAttributesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateAttributesPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteInt(p.Attributes.Count);
                foreach (AttributeSnapshot a in p.Attributes)
                {
                    w.WriteString(a.LegacyKey ?? string.Empty);
                    w.WriteDouble(a.BaseValue);
                    w.WriteList(a.Modifiers, static (ref PacketWriter www, AttributeModifierEntry m) =>
                    {
                        www.WriteUuid(m.LegacyUuid);
                        www.WriteDouble(m.Amount);
                        www.WriteByte((byte)m.Operation);
                    });
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int count = r.ReadInt();
                var list = new List<AttributeSnapshot>(count);
                for (int i = 0; i < count; i++)
                {
                    string key = r.ReadString();
                    double value = r.ReadDouble();
                    var mods = r.ReadList(static (ref PacketReader rrr) =>
                        new AttributeModifierEntry(rrr.ReadUuid(), null, rrr.ReadDouble(), rrr.ReadByte()));
                    list.Add(new AttributeSnapshot(key, 0, value, mods));
                }

                return new ClientboundUpdateAttributesPacket(id, list);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateAttributes(PacketBindings bindings)
    {
        // 1.8-1.15.2 share the int-count member; the outer attribute list becomes VarInt-counted at 1.17;
        // 1.20.5 uses the holder-VarInt id with a still-UUID modifier; 1.21 moves the modifier to the Identifier + VarInt-operation form.
        bindings.Packet(EntityPackets.Clientbound.UpdateAttributes)
            .From(JavaProtocols.V1_8, EntityEquipmentCodecs.UpdateAttributesV1_8)
            .From(JavaProtocols.V1_16, EntityEquipmentCodecs.UpdateAttributesV1_16IntCount)
            .From(JavaProtocols.V1_17, EntityEquipmentCodecs.UpdateAttributesV1_17)
            .From(JavaProtocols.V1_20_5, EntityEquipmentCodecs.UpdateAttributesV1_20_5)
            .From(JavaProtocols.V1_21, EntityEquipmentCodecs.UpdateAttributesV1_21);
    }
}
