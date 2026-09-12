using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 use entity: entity id, action VarInt, and interact-at floats with no secondary flag.</summary>
    public static readonly PacketCodec<ServerboundInteractPacket> InteractV1_8 = Interact(hasHand: false, hasSecondary: false);

    /// <summary>1.16+ interact: entity id, action VarInt, interact-at floats (action 2), hand VarInt (action 0/2), trailing using-secondary-action (sneak) bool. The bool was added at 1.16 exactly; the wire is then unchanged through 26.2.</summary>
    public static readonly PacketCodec<ServerboundInteractPacket> InteractV1_16 = Interact(hasHand: true, hasSecondary: true);

    /// <summary>1.9-1.15.2 interact: entity id, action VarInt, interact-at floats (action 2), hand VarInt (action 0/2). 1.9 added the hand; the using-secondary-action bool is 1.16+ (not present here).</summary>
    public static readonly PacketCodec<ServerboundInteractPacket> InteractV1_9 = Interact(hasHand: true, hasSecondary: false);

    private static PacketCodec<ServerboundInteractPacket> Interact(bool hasHand, bool hasSecondary) =>
        PacketCodec<ServerboundInteractPacket>.Of(
            (ref PacketWriter w, ServerboundInteractPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.Action);
                if (p.Action == 2)
                {
                    Vec3d at = p.InteractAt ?? Vec3d.Zero;
                    w.WriteFloat((float)at.X); w.WriteFloat((float)at.Y); w.WriteFloat((float)at.Z);
                }

                if (hasHand && p.Action != 1)
                    w.WriteVarInt(p.Hand ?? 0);

                if (hasSecondary)
                    w.WriteBool(p.UsingSecondaryAction ?? false);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int action = r.ReadVarInt();
                Vec3d? at = null;
                if (action == 2)
                    at = new Vec3d(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

                int? hand = null;
                if (hasHand && action != 1)
                    hand = r.ReadVarInt();

                bool? secondary = hasSecondary ? r.ReadBool() : null;
                return new ServerboundInteractPacket(id, action, hand, at, secondary);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareInteract(PacketBindings bindings)
    {
        // The using-secondary-action bool begins at 1.16. Writing it on earlier protocols appends a trailing byte that makes the packet oversized.
        bindings.Packet(EntityPackets.Serverbound.Interact)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.InteractV1_8)
            .From(JavaProtocols.V1_9, EntityServerboundCodecs.InteractV1_9)
            .From(JavaProtocols.V1_16, EntityServerboundCodecs.InteractV1_16);
    }
}
