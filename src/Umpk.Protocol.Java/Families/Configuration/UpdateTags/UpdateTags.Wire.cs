using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Update tags

    private static readonly WriterAction<TagEntry> WriteTagEntry =
        static (ref PacketWriter w, TagEntry e) =>
        {
            w.WriteString(e.TagId.ToString());
            w.WriteVarInt(e.Ids.Count);
            for (int i = 0; i < e.Ids.Count; i++)
                w.WriteVarInt(e.Ids[i]);

        };

    private static readonly ReaderFunc<TagEntry> ReadTagEntry =
        static (ref PacketReader r) =>
        {
            Identifier tagId = Identifier.Parse(r.ReadString());
            int count = r.ReadVarInt();
            var ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = r.ReadVarInt();

            return new TagEntry(tagId, ids);
        };

    private static readonly WriterAction<TagRegistry> WriteTagRegistry =
        static (ref PacketWriter w, TagRegistry g) =>
        {
            w.WriteString(g.Registry.ToString());
            w.WriteList(g.Tags, WriteTagEntry);
        };

    private static readonly ReaderFunc<TagRegistry> ReadTagRegistry =
        static (ref PacketReader r) =>
        {
            Identifier registry = Identifier.Parse(r.ReadString());
            return new TagRegistry(registry, r.ReadList(ReadTagEntry));
        };

    /// <summary>Configuration update-tags (map of registry -> tag payloads, encoded as a VarInt-prefixed list).</summary>
    public static readonly PacketCodec<ClientboundConfigUpdateTagsPacket> UpdateTags =
        PacketCodec<ClientboundConfigUpdateTagsPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigUpdateTagsPacket p, PacketCodecContext _) =>
                w.WriteList(p.Registries, WriteTagRegistry),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigUpdateTagsPacket(r.ReadList(ReadTagRegistry)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateTags(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.UpdateTags)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.UpdateTags);
    }
}
