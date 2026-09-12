using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    private static readonly WriterAction<KnownPack> WritePack = static (ref PacketWriter w, KnownPack p) =>
    {
        w.WriteString(p.Namespace);
        w.WriteString(p.Id);
        w.WriteString(p.Version);
    };

    private static readonly ReaderFunc<KnownPack> ReadPack = static (ref PacketReader r) =>
        new KnownPack(r.ReadString(), r.ReadString(), r.ReadString());

    /// <summary>Known-pack selection request.</summary>
    public static readonly PacketCodec<ClientboundSelectKnownPacksPacket> SelectPacksClient =
        PacketCodec<ClientboundSelectKnownPacksPacket>.Of(
            static (ref PacketWriter w, ClientboundSelectKnownPacksPacket p, PacketCodecContext _) => w.WriteList(p.Packs, WritePack),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSelectKnownPacksPacket(r.ReadList(ReadPack)));

    /// <summary>Known-pack selection response.</summary>
    public static readonly PacketCodec<ServerboundSelectKnownPacksPacket> SelectPacksServer =
        PacketCodec<ServerboundSelectKnownPacksPacket>.Of(
            static (ref PacketWriter w, ServerboundSelectKnownPacksPacket p, PacketCodecContext _) => w.WriteList(p.Packs, WritePack),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundSelectKnownPacksPacket(r.ReadList(ReadPack)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSelectKnownPacks(PacketBindings bindings)
    {
        bindings.Packet(ConfigurationPackets.Clientbound.SelectKnownPacks)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.SelectPacksClient);

        bindings.Packet(ConfigurationPackets.Serverbound.SelectKnownPacks)
            .From(JavaEras.ItemComponents, ConfigurationCodecs.SelectPacksServer);
    }
}
