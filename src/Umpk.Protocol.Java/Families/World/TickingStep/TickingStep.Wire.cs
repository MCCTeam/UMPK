using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>1.20.3 onward: a signed VarInt step counter. Original readers and writers preserve negative values through 26.3.</summary>
    public static readonly PacketCodec<ClientboundTickingStepPacket> TickingStepV1_20_3 =
        PacketCodec<ClientboundTickingStepPacket>.Of(
            static (ref PacketWriter writer, ClientboundTickingStepPacket packet, PacketCodecContext _) => writer.WriteVarInt(packet.TickSteps),
            static (ref PacketReader reader, PacketCodecContext _) => new ClientboundTickingStepPacket(reader.ReadVarInt()),
            WireShape.Of("varint"));

    internal static void DeclareTickingStep(PacketBindings bindings) =>
        bindings.Packet(WorldPackets.Clientbound.TickingStep)
            .From(JavaProtocols.V1_20_3, WorldStateCodecs.TickingStepV1_20_3);
}
