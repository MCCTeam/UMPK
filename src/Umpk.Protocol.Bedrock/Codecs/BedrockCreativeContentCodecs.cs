using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockCreativeItemInstance : IEquatable<BedrockCreativeItemInstance>
{
    internal BedrockCreativeItemInstance(int networkId, ushort count, uint metadataValue, int blockRuntimeId, byte[] extraData)
    {
        ArgumentNullException.ThrowIfNull(extraData);
        NetworkId = networkId;
        Count = count;
        MetadataValue = metadataValue;
        BlockRuntimeId = blockRuntimeId;
        ExtraData = (byte[])extraData.Clone();
    }

    internal int NetworkId { get; }

    internal ushort Count { get; }

    internal uint MetadataValue { get; }

    internal int BlockRuntimeId { get; }

    internal byte[] ExtraData { get; }

    public bool Equals(BedrockCreativeItemInstance? other) =>
        other is not null
        && NetworkId == other.NetworkId
        && Count == other.Count
        && MetadataValue == other.MetadataValue
        && BlockRuntimeId == other.BlockRuntimeId
        && ExtraData.SequenceEqual(other.ExtraData);

    public override bool Equals(object? obj) => Equals(obj as BedrockCreativeItemInstance);

    public override int GetHashCode() => HashCode.Combine(NetworkId, Count, MetadataValue, BlockRuntimeId);
}

internal sealed class BedrockCreativeGroup : IEquatable<BedrockCreativeGroup>
{
    internal BedrockCreativeGroup(byte category, string name, BedrockCreativeItemInstance iconItem)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(iconItem);
        Category = category;
        Name = name;
        IconItem = iconItem;
    }

    internal byte Category { get; }

    internal string Name { get; }

    internal BedrockCreativeItemInstance IconItem { get; }

    public bool Equals(BedrockCreativeGroup? other) =>
        other is not null && Category == other.Category && Name == other.Name && IconItem.Equals(other.IconItem);

    public override bool Equals(object? obj) => Equals(obj as BedrockCreativeGroup);

    public override int GetHashCode() => HashCode.Combine(Category, Name);
}

internal sealed class BedrockCreativeEntry : IEquatable<BedrockCreativeEntry>
{
    internal BedrockCreativeEntry(int entryId, BedrockCreativeItemInstance item, int groupIndex)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(entryId);
        ArgumentOutOfRangeException.ThrowIfNegative(groupIndex);
        EntryId = entryId;
        Item = item;
        GroupIndex = groupIndex;
    }

    internal int EntryId { get; }

    internal BedrockCreativeItemInstance Item { get; }

    internal int GroupIndex { get; }

    public bool Equals(BedrockCreativeEntry? other) =>
        other is not null && EntryId == other.EntryId && Item.Equals(other.Item) && GroupIndex == other.GroupIndex;

    public override bool Equals(object? obj) => Equals(obj as BedrockCreativeEntry);

    public override int GetHashCode() => HashCode.Combine(EntryId, GroupIndex);
}

internal sealed class BedrockCreativeContentPacket : IEquatable<BedrockCreativeContentPacket>
{
    internal BedrockCreativeContentPacket(IReadOnlyList<BedrockCreativeGroup> groups, IReadOnlyList<BedrockCreativeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(entries);
        Groups = [.. groups];
        Entries = [.. entries];
    }

    internal ImmutableArray<BedrockCreativeGroup> Groups { get; }

    internal ImmutableArray<BedrockCreativeEntry> Entries { get; }

    public bool Equals(BedrockCreativeContentPacket? other) =>
        other is not null && Groups.SequenceEqual(other.Groups) && Entries.SequenceEqual(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as BedrockCreativeContentPacket);

    public override int GetHashCode() => HashCode.Combine(Groups.Length, Entries.Length);
}

internal static class BedrockCreativeContentCodecs
{
    internal const int MaxGroupsPerPacket = 1024;
    internal const int MaxEntriesPerPacket = 8192;

    internal static byte[] Encode(BedrockCreativeContentPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)packet.Groups.Length));
        foreach (BedrockCreativeGroup group in packet.Groups)
            WriteGroup(writer, group);
        writer.WriteUInt32VarInt(checked((uint)packet.Entries.Length));
        foreach (BedrockCreativeEntry entry in packet.Entries)
            WriteEntry(writer, entry);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockCreativeContentPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        uint groupCount = reader.ReadUInt32VarInt();
        if (groupCount > MaxGroupsPerPacket || groupCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The creative content group count {groupCount} exceeds its element limit.");
        var groups = new List<BedrockCreativeGroup>(checked((int)Math.Min(groupCount, 16)));
        for (uint index = 0; index < groupCount; index++)
            groups.Add(ReadGroup(ref reader, actualLimits));
        uint entryCount = reader.ReadUInt32VarInt();
        if (entryCount > MaxEntriesPerPacket || entryCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The creative content entry count {entryCount} exceeds its element limit.");
        var entries = new List<BedrockCreativeEntry>(checked((int)Math.Min(entryCount, 16)));
        for (uint index = 0; index < entryCount; index++)
            entries.Add(ReadEntry(ref reader, actualLimits));
        if (!reader.End)
            throw new BedrockCodecException($"The creative content payload has {reader.Remaining} unread bytes.");
        return new BedrockCreativeContentPacket(groups, entries);
    }

    private static void WriteGroup(BedrockWriter writer, BedrockCreativeGroup group)
    {
        writer.WriteByte(group.Category);
        writer.WriteString(group.Name);
        WriteItem(writer, group.IconItem);
    }

    private static BedrockCreativeGroup ReadGroup(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        byte category = reader.ReadByte();
        string name = reader.ReadString();
        return new BedrockCreativeGroup(category, name, ReadItem(ref reader, "CreativeContent group icon", limits));
    }

    private static void WriteEntry(BedrockWriter writer, BedrockCreativeEntry entry)
    {
        writer.WriteUInt32VarInt(checked((uint)entry.EntryId));
        WriteItem(writer, entry.Item);
        writer.WriteUInt32VarInt(checked((uint)entry.GroupIndex));
    }

    private static BedrockCreativeEntry ReadEntry(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int entryId = ReadCountId(ref reader, "entry");
        BedrockCreativeItemInstance item = ReadItem(ref reader, "CreativeContent entry item", limits);
        return new BedrockCreativeEntry(entryId, item, ReadCountId(ref reader, "group index"));
    }

    private static void WriteItem(BedrockWriter writer, BedrockCreativeItemInstance item)
    {
        writer.WriteZigZag32(item.NetworkId);
        writer.WriteUInt16LittleEndian(item.Count);
        writer.WriteUInt32VarInt(item.MetadataValue);
        writer.WriteZigZag32(item.BlockRuntimeId);
        writer.WriteByteArray(item.ExtraData);
    }

    private static BedrockCreativeItemInstance ReadItem(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        int networkId = reader.ReadZigZag32();
        ushort count = reader.ReadUInt16LittleEndian();
        uint metadataValue = reader.ReadUInt32VarInt();
        int blockRuntimeId = reader.ReadZigZag32();
        byte[] extraData = reader.ReadByteArray();
        if (extraData.Length > BedrockObservationCodecs.MaxItemExtraDataBytes)
            throw new BedrockCodecException($"The {kind} extra data length {extraData.Length} exceeds its byte limit.");
        return new BedrockCreativeItemInstance(networkId, count, metadataValue, blockRuntimeId, extraData);
    }

    private static int ReadCountId(ref BedrockReader reader, string kind)
    {
        uint value = reader.ReadUInt32VarInt();
        if (value > int.MaxValue)
            throw new BedrockCodecException($"The creative content {kind} identifier {value} exceeds its value limit.");
        return (int)value;
    }
}
