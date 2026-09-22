using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Play-phase post effects: a VarInt-counted list of shader identifiers.</summary>
    public static readonly PacketCodec<ClientboundPostEffectsPacket> PostEffects =
        PacketCodec<ClientboundPostEffectsPacket>.Of(
            static (ref PacketWriter w, ClientboundPostEffectsPacket p, PacketCodecContext _) =>
                w.WriteList(p.Effects, static (ref PacketWriter sw, Identifier id) => WriteIdentifier(ref sw, id)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundPostEffectsPacket(r.ReadList(static (ref PacketReader sr) => ReadIdentifier(ref sr))),
            WireShape.Of("varint*identifier"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePostEffectsPlay(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Clientbound.PostEffects)
            .From(JavaProtocols.V26_3, PlayCommonCodecs.PostEffects);
    }
}
