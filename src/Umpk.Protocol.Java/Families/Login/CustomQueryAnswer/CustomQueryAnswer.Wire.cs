using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginChannelCodecs
{
    /// <summary>Login plugin answer (custom_query_answer): VarInt id, optional (present flag + remaining bytes).</summary>
    public static readonly PacketCodec<ServerboundLoginCustomQueryAnswerPacket> CustomQueryAnswer =
        PacketCodec<ServerboundLoginCustomQueryAnswerPacket>.Of(
            static (ref PacketWriter w, ServerboundLoginCustomQueryAnswerPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TransactionId);
                if (p.Data is null)
                    w.WriteBool(false);

                else
                {
                    w.WriteBool(true);
                    w.WriteBytes(p.Data);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int tx = r.ReadVarInt();
                bool present = r.ReadBool();
                byte[]? data = present ? r.ReadRemaining().ToArray() : null;
                return new ServerboundLoginCustomQueryAnswerPacket(tx, data);
            },
            WireShape.Of("varint,bool,raw_tail"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomQueryAnswer(PacketBindings bindings)
    {
        // The login plugin ANSWER is the same wire from 1.13 onward (VarInt transaction id, then a present flag and the rest of the frame); 1.20.2 only renamed the packet, so the 1.13-1.20.1 datasets spell it custom_query and 1.20.2+ spell it custom_query_answer. The payload is a VarInt id followed by a nullable remaining-byte block. The legacy alias is required for the 1.13-1.20.1 datasets.
        bindings.Packet(LoginFamilyPackets.Login.CustomQueryAnswer)
            .From(JavaProtocols.V1_13, LoginChannelCodecs.CustomQueryAnswer)
            .AliasedAs(Identifier.Minecraft("custom_query"), JavaProtocols.V1_13, JavaProtocols.V1_20);
    }
}
