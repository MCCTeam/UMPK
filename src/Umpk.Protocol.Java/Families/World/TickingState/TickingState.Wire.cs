using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>1.20.3 onward: a float tick rate followed by the freeze flag. Original readers and writers preserve this layout through 26.3.</summary>
    public static readonly PacketCodec<ClientboundTickingStatePacket> TickingStateV1_20_3 =
        PacketCodec<ClientboundTickingStatePacket>.Of(
            static (ref PacketWriter writer, ClientboundTickingStatePacket packet, PacketCodecContext _) =>
            {
                writer.WriteFloat(packet.TickRate);
                writer.WriteBool(packet.IsFrozen);
            },
            static (ref PacketReader reader, PacketCodecContext _) => new ClientboundTickingStatePacket(reader.ReadFloat(), reader.ReadBool()),
            WireShape.Of("float,bool"));

    internal static void DeclareTickingState(PacketBindings bindings) =>
        bindings.Packet(WorldPackets.Clientbound.TickingState)
            .From(JavaProtocols.V1_20_3, WorldStateCodecs.TickingStateV1_20_3);
}
