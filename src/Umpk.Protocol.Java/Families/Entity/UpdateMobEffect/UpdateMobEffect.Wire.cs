using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityEffectCodecs
{
    /// <summary>1.8 entity effect: effect byte, amplifier byte, VarInt duration, and hide-particles byte.</summary>
    public static readonly PacketCodec<ClientboundUpdateMobEffectPacket> UpdateMobEffectV1_8 =
        PacketCodec<ClientboundUpdateMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte((byte)p.EffectId);
                w.WriteByte((byte)p.Amplifier);
                w.WriteVarInt(p.Duration);
                w.WriteByte(p.Flags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundUpdateMobEffectPacket(r.ReadVarInt(), r.ReadByte(), r.ReadByte(), r.ReadVarInt(), r.ReadByte()));

    /// <summary>1.18-1.18.2 update mob effect (protocols 757-758): VarInt entity, VarInt effect id, i8 amplifier, VarInt duration, i8 flags. The effect id widened from a byte to a VarInt at 1.18; the trailing nullable factor-data NBT is 1.19+ only.</summary>
    public static readonly PacketCodec<ClientboundUpdateMobEffectPacket> UpdateMobEffectV1_18 =
        PacketCodec<ClientboundUpdateMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.EffectId);
                w.WriteByte((byte)p.Amplifier);
                w.WriteVarInt(p.Duration);
                w.WriteByte(p.Flags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundUpdateMobEffectPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadByte(), r.ReadVarInt(), r.ReadByte()));

    /// <summary>1.19-1.20.1 update mob effect (protocols 759-763): VarInt entity, VarInt effect id, i8 amplifier, VarInt duration, i8 flags, then a nullable network-NBT <c>factorData</c> compound (present only for the darkness blend; a normal effect sends the false boolean). The compound uses the named-root network-NBT framing of this era (the root-name removal is 1.20.2).</summary>
    public static readonly PacketCodec<ClientboundUpdateMobEffectPacket> UpdateMobEffectV1_19 =
        PacketCodec<ClientboundUpdateMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.EffectId);
                w.WriteByte((byte)p.Amplifier);
                w.WriteVarInt(p.Duration);
                w.WriteByte(p.Flags);
                if (p.FactorData is { } factor)
                {
                    w.WriteBool(true);
                    w.WriteNbt(factor, NbtWireFormat.JavaNamedRoot);
                }
                else
                    w.WriteBool(false);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int entityId = r.ReadVarInt();
                int effectId = r.ReadVarInt();
                byte amplifier = r.ReadByte();
                int duration = r.ReadVarInt();
                byte flags = r.ReadByte();
                NbtTag? factor = r.ReadBool() ? r.ReadNbt(NbtWireFormat.JavaNamedRoot) : null;
                return new ClientboundUpdateMobEffectPacket(entityId, effectId, amplifier, duration, flags, factor);
            });

    /// <summary>Modern update mob effect: effect holder VarInt, VarInt amplifier, VarInt duration, flags byte.</summary>
    public static readonly PacketCodec<ClientboundUpdateMobEffectPacket> UpdateMobEffectV1_20_5 =
        PacketCodec<ClientboundUpdateMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.EffectId);
                w.WriteVarInt(p.Amplifier);
                w.WriteVarInt(p.Duration);
                w.WriteByte(p.Flags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundUpdateMobEffectPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt(), r.ReadByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateMobEffect(PacketBindings bindings)
    {
        // Byte effect id through 1.17.1, widened to a VarInt at 1.18. The 1.19-1.20.4 form still carries a trailing nullable FactorData NBT and a byte amplifier; both went at 1.20.5 (VarInt amplifier, FactorData removed). 1.8 spells it entity_effect.
        bindings.Packet(EntityPackets.Clientbound.UpdateMobEffect)
            .From(JavaProtocols.V1_8, EntityEffectCodecs.UpdateMobEffectV1_8)
            .From(JavaProtocols.V1_18, EntityEffectCodecs.UpdateMobEffectV1_18)
            .From(JavaProtocols.V1_19, EntityEffectCodecs.UpdateMobEffectV1_19)
            .From(JavaProtocols.V1_20_5, EntityEffectCodecs.UpdateMobEffectV1_20_5)
            .AliasedAs(Identifier.Minecraft("entity_effect"));
    }
}
