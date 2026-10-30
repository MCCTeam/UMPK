using Umpk.Game.Registries;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Shared tag payloads: three fixed registries in 1.13, four in 1.14–1.16.5, and a keyed registry map from 1.17.</summary>
internal static class RegistryTagCodecs
{
    private static readonly Identifier[] FixedRegistryNames =
        [RegistryIds.Block, RegistryIds.Item, Identifier.Minecraft("fluid"), RegistryIds.EntityType];

    private static readonly ReaderFunc<int> ReadMember = static (ref PacketReader reader) => reader.ReadVarInt();
    private static readonly ReaderFunc<TagEntry> ReadTag = static (ref PacketReader reader) =>
        new(Identifier.Parse(reader.ReadString()), reader.ReadList(ReadMember));
    private static readonly WriterAction<TagEntry> WriteTag = static (ref PacketWriter writer, TagEntry tag) =>
    {
        writer.WriteString(tag.TagId.ToString());
        writer.WriteVarInt(tag.Ids.Count);
        foreach (int member in tag.Ids)
            writer.WriteVarInt(member);
    };
    private static readonly ReaderFunc<TagRegistry> ReadRegistry = static (ref PacketReader reader) =>
        new(Identifier.Parse(reader.ReadString()), reader.ReadList(ReadTag));
    private static readonly WriterAction<TagRegistry> WriteRegistry = static (ref PacketWriter writer, TagRegistry registry) =>
    {
        writer.WriteString(registry.Registry.ToString());
        writer.WriteList(registry.Tags, WriteTag);
    };

    public static readonly PacketCodec<IReadOnlyList<TagRegistry>> FixedThree = Fixed(3);
    public static readonly PacketCodec<IReadOnlyList<TagRegistry>> FixedFour = Fixed(4);
    public static readonly PacketCodec<IReadOnlyList<TagRegistry>> Keyed = PacketCodec<IReadOnlyList<TagRegistry>>.Of(
        static (ref PacketWriter writer, IReadOnlyList<TagRegistry> groups, PacketCodecContext _) => writer.WriteList(groups, WriteRegistry),
        static (ref PacketReader reader, PacketCodecContext _) => reader.ReadList(ReadRegistry),
        WireShape.Of("list(registry,list(tag,list(varint)))"));

    private static PacketCodec<IReadOnlyList<TagRegistry>> Fixed(int count) => PacketCodec<IReadOnlyList<TagRegistry>>.Of(
        (ref PacketWriter writer, IReadOnlyList<TagRegistry> groups, PacketCodecContext _) =>
        {
            foreach (TagRegistry supplied in groups)
            {
                int index = Array.IndexOf(FixedRegistryNames, supplied.Registry);
                if (index < 0 || index >= count)
                    throw new ProtocolViolationException("This tag layout cannot represent the supplied registry.");
            }
            for (int index = 0; index < count; index++)
            {
                IReadOnlyList<TagEntry>? tags = null;
                foreach (TagRegistry group in groups)
                    if (group.Registry == FixedRegistryNames[index])
                    {
                        if (tags is not null)
                            throw new ProtocolViolationException("Duplicate fixed tag registry.");
                        tags = group.Tags;
                    }
                writer.WriteList(tags ?? [], WriteTag);
            }
        },
        (ref PacketReader reader, PacketCodecContext _) =>
        {
            var groups = new TagRegistry[count];
            for (int index = 0; index < count; index++)
                groups[index] = new TagRegistry(FixedRegistryNames[index], reader.ReadList(ReadTag));
            return groups;
        },
        WireShape.Of(count == 3 ? "tags(block),tags(item),tags(fluid)" : "tags(block),tags(item),tags(fluid),tags(entity_type)"));
}
