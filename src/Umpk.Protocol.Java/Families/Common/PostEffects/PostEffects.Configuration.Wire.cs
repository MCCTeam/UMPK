using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Post effects (clientbound): a VarInt-counted list of shader identifiers.</summary>
    public static readonly PacketCodec<ClientboundConfigPostEffectsPacket> PostEffects =
        PacketCodec<ClientboundConfigPostEffectsPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigPostEffectsPacket p, PacketCodecContext _) =>
                w.WriteList(p.Effects, static (ref PacketWriter sw, Identifier id) => WriteIdentifier(ref sw, id)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigPostEffectsPacket(r.ReadList(static (ref PacketReader sr) => ReadIdentifier(ref sr))),
            WireShape.Of("varint*identifier"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePostEffectsConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.PostEffects)
            .From(JavaProtocols.V26_3, ConfigurationCodecs.PostEffects);
    }
}
