using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockContainerAddress(int WindowId, byte ContainerId, uint? DynamicContainerId);

internal sealed class BedrockInventoryContentPacket : IEquatable<BedrockInventoryContentPacket>
{
    internal BedrockInventoryContentPacket(int windowId, IReadOnlyList<BedrockItemInstance> items, BedrockContainerAddress container, BedrockItemInstance storageItem)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(storageItem);
        if (container.WindowId != windowId)
            throw new BedrockCodecException("The inventory content container address does not match its window.");
        WindowId = windowId;
        Items = [.. items];
        Container = container;
        StorageItem = storageItem;
    }

    internal int WindowId { get; }

    internal ImmutableArray<BedrockItemInstance> Items { get; }

    internal BedrockContainerAddress Container { get; }

    internal BedrockItemInstance StorageItem { get; }

    public bool Equals(BedrockInventoryContentPacket? other) =>
        other is not null
        && WindowId == other.WindowId
        && Items.SequenceEqual(other.Items)
        && Container.Equals(other.Container)
        && StorageItem.Equals(other.StorageItem);

    public override bool Equals(object? obj) => Equals(obj as BedrockInventoryContentPacket);

    public override int GetHashCode() => HashCode.Combine(WindowId, Container, Items.Length);
}

internal sealed class BedrockInventorySlotPacket : IEquatable<BedrockInventorySlotPacket>
{
    internal BedrockInventorySlotPacket(int windowId, int slot, BedrockContainerAddress? container, BedrockItemInstance? storageItem, BedrockItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (container.HasValue && container.Value.WindowId != windowId)
            throw new BedrockCodecException("The inventory slot container address does not match its window.");
        WindowId = windowId;
        Slot = slot;
        Container = container;
        StorageItem = storageItem;
        Item = item;
    }

    internal int WindowId { get; }

    internal int Slot { get; }

    internal BedrockContainerAddress? Container { get; }

    internal BedrockItemInstance? StorageItem { get; }

    internal BedrockItemInstance Item { get; }

    public bool Equals(BedrockInventorySlotPacket? other) =>
        other is not null
        && WindowId == other.WindowId
        && Slot == other.Slot
        && Nullable.Equals(Container, other.Container)
        && Equals(StorageItem, other.StorageItem)
        && Item.Equals(other.Item);

    public override bool Equals(object? obj) => Equals(obj as BedrockInventorySlotPacket);

    public override int GetHashCode() => HashCode.Combine(WindowId, Slot);
}

internal sealed class BedrockContainerOpenPacket : IEquatable<BedrockContainerOpenPacket>
{
    internal BedrockContainerOpenPacket(sbyte windowId, sbyte windowType, int x, int y, int z, long entityUniqueId)
    {
        WindowId = windowId;
        WindowType = windowType;
        X = x;
        Y = y;
        Z = z;
        EntityUniqueId = entityUniqueId;
    }

    internal sbyte WindowId { get; }

    internal sbyte WindowType { get; }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    internal long EntityUniqueId { get; }

    public bool Equals(BedrockContainerOpenPacket? other) =>
        other is not null
        && WindowId == other.WindowId
        && WindowType == other.WindowType
        && X == other.X
        && Y == other.Y
        && Z == other.Z
        && EntityUniqueId == other.EntityUniqueId;

    public override bool Equals(object? obj) => Equals(obj as BedrockContainerOpenPacket);

    public override int GetHashCode() => HashCode.Combine(WindowId, WindowType, X, Y, Z, EntityUniqueId);
}

internal sealed class BedrockContainerClosePacket : IEquatable<BedrockContainerClosePacket>
{
    internal BedrockContainerClosePacket(sbyte windowId, sbyte windowType, bool serverSide)
    {
        WindowId = windowId;
        WindowType = windowType;
        ServerSide = serverSide;
    }

    internal sbyte WindowId { get; }

    internal sbyte WindowType { get; }

    internal bool ServerSide { get; }

    public bool Equals(BedrockContainerClosePacket? other) =>
        other is not null
        && WindowId == other.WindowId
        && WindowType == other.WindowType
        && ServerSide == other.ServerSide;

    public override bool Equals(object? obj) => Equals(obj as BedrockContainerClosePacket);

    public override int GetHashCode() => HashCode.Combine(WindowId, WindowType, ServerSide);
}

internal sealed class BedrockPlayerHotbarPacket : IEquatable<BedrockPlayerHotbarPacket>
{
    internal BedrockPlayerHotbarPacket(int selectedSlot, sbyte windowId, bool selectSlot)
    {
        SelectedSlot = selectedSlot;
        WindowId = windowId;
        SelectSlot = selectSlot;
    }

    internal int SelectedSlot { get; }

    internal sbyte WindowId { get; }

    internal bool SelectSlot { get; }

    public bool Equals(BedrockPlayerHotbarPacket? other) =>
        other is not null
        && SelectedSlot == other.SelectedSlot
        && WindowId == other.WindowId
        && SelectSlot == other.SelectSlot;

    public override bool Equals(object? obj) => Equals(obj as BedrockPlayerHotbarPacket);

    public override int GetHashCode() => HashCode.Combine(SelectedSlot, WindowId, SelectSlot);
}

internal static class BedrockInventoryCodecs
{
    internal const int MaxContentItems = 1024;
    internal const int MaxSlotIndex = 4095;

    internal static byte[] Encode(BedrockInventoryContentPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            WriteWindowId(writer, packet.WindowId);
            writer.WriteUInt32VarInt(checked((uint)packet.Items.Length));
            foreach (BedrockItemInstance item in packet.Items)
                WriteItem(writer, item);
            WriteContainerAddress(writer, packet.Container);
            WriteItem(writer, packet.StorageItem);
        });
    }

    internal static BedrockInventoryContentPacket DecodeContent(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int windowId = ReadWindowId(ref reader);
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxContentItems || count > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The inventory content count {count} exceeds its element limit.");
        var items = new List<BedrockItemInstance>(checked((int)Math.Min(count, 16)));
        for (uint index = 0; index < count; index++)
            items.Add(ReadItem(ref reader, "InventoryContent item", actualLimits));
        BedrockContainerAddress container = ReadContainerAddress(ref reader, windowId, "InventoryContent container");
        BedrockItemInstance storageItem = ReadItem(ref reader, "InventoryContent storage item", actualLimits);
        EnsureEnd(reader);
        return new BedrockInventoryContentPacket(windowId, items, container, storageItem);
    }

    internal static byte[] Encode(BedrockInventorySlotPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            WriteWindowId(writer, packet.WindowId);
            writer.WriteUInt32VarInt(checked((uint)packet.Slot));
            writer.WriteBoolean(packet.Container.HasValue);
            if (packet.Container.HasValue)
                WriteContainerAddress(writer, packet.Container.Value);
            writer.WriteBoolean(packet.StorageItem is not null);
            if (packet.StorageItem is not null)
                WriteItem(writer, packet.StorageItem);
            WriteItem(writer, packet.Item);
        });
    }

    internal static BedrockInventorySlotPacket DecodeSlot(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int windowId = ReadWindowId(ref reader);
        uint slot = reader.ReadUInt32VarInt();
        if (slot > MaxSlotIndex)
            throw new BedrockCodecException($"The inventory slot index {slot} exceeds its element limit.");
        BedrockContainerAddress? container = reader.ReadBoolean() ? ReadContainerAddress(ref reader, windowId, "InventorySlot container") : null;
        BedrockItemInstance? storageItem = reader.ReadBoolean() ? ReadItem(ref reader, "InventorySlot storage item", actualLimits) : null;
        BedrockItemInstance item = ReadItem(ref reader, "InventorySlot item", actualLimits);
        EnsureEnd(reader);
        return new BedrockInventorySlotPacket(windowId, checked((int)slot), container, storageItem, item);
    }

    internal static byte[] Encode(BedrockContainerOpenPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteByte(unchecked((byte)packet.WindowId));
            writer.WriteByte(unchecked((byte)packet.WindowType));
            writer.WriteZigZag32(packet.X);
            writer.WriteZigZag32(packet.Y);
            writer.WriteZigZag32(packet.Z);
            writer.WriteZigZag64(packet.EntityUniqueId);
        });
    }

    internal static BedrockContainerOpenPacket DecodeContainerOpen(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        sbyte windowId = unchecked((sbyte)reader.ReadByte());
        sbyte windowType = unchecked((sbyte)reader.ReadByte());
        int x = reader.ReadZigZag32();
        int y = reader.ReadZigZag32();
        int z = reader.ReadZigZag32();
        long entityUniqueId = reader.ReadZigZag64();
        EnsureEnd(reader);
        return new BedrockContainerOpenPacket(windowId, windowType, x, y, z, entityUniqueId);
    }

    internal static byte[] Encode(BedrockContainerClosePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteByte(unchecked((byte)packet.WindowId));
            writer.WriteByte(unchecked((byte)packet.WindowType));
            writer.WriteBoolean(packet.ServerSide);
        });
    }

    internal static BedrockContainerClosePacket DecodeContainerClose(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        sbyte windowId = unchecked((sbyte)reader.ReadByte());
        sbyte windowType = unchecked((sbyte)reader.ReadByte());
        bool serverSide = reader.ReadBoolean();
        EnsureEnd(reader);
        return new BedrockContainerClosePacket(windowId, windowType, serverSide);
    }

    internal static byte[] Encode(BedrockPlayerHotbarPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt32VarInt(unchecked((uint)packet.SelectedSlot));
            writer.WriteByte(unchecked((byte)packet.WindowId));
            writer.WriteBoolean(packet.SelectSlot);
        });
    }

    internal static BedrockPlayerHotbarPacket DecodePlayerHotbar(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        int selectedSlot = unchecked((int)reader.ReadUInt32VarInt());
        sbyte windowId = unchecked((sbyte)reader.ReadByte());
        bool selectSlot = reader.ReadBoolean();
        EnsureEnd(reader);
        return new BedrockPlayerHotbarPacket(selectedSlot, windowId, selectSlot);
    }

    private static void WriteWindowId(BedrockWriter writer, int windowId) => writer.WriteUInt32VarInt(unchecked((uint)windowId));

    private static int ReadWindowId(ref BedrockReader reader) => unchecked((int)reader.ReadUInt32VarInt());

    private static void WriteContainerAddress(BedrockWriter writer, BedrockContainerAddress address)
    {
        writer.WriteByte(address.ContainerId);
        writer.WriteBoolean(address.DynamicContainerId.HasValue);
        if (address.DynamicContainerId.HasValue)
            writer.WriteUInt32VarInt(address.DynamicContainerId.Value);
    }

    private static BedrockContainerAddress ReadContainerAddress(ref BedrockReader reader, int windowId, string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        byte containerId = reader.ReadByte();
        uint? dynamicId = reader.ReadBoolean() ? reader.ReadUInt32VarInt() : null;
        return new BedrockContainerAddress(windowId, containerId, dynamicId);
    }

    private static void WriteItem(BedrockWriter writer, BedrockItemInstance item)
    {
        writer.WriteInt16LittleEndian(item.NetworkId);
        writer.WriteUInt16LittleEndian(item.Count);
        writer.WriteUInt32VarInt(item.MetadataValue);
        writer.WriteBoolean(item.StackNetworkId.HasValue);
        if (item.StackNetworkId.HasValue)
            writer.WriteZigZag32(item.StackNetworkId.Value);
        writer.WriteUInt32VarInt(item.BlockRuntimeId);
        writer.WriteByteArray(item.ExtraData);
    }

    private static BedrockItemInstance ReadItem(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        short networkId = reader.ReadInt16LittleEndian();
        ushort count = reader.ReadUInt16LittleEndian();
        uint metadataValue = reader.ReadUInt32VarInt();
        int? stackNetworkId = reader.ReadBoolean() ? reader.ReadZigZag32() : null;
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        byte[] extraData = reader.ReadByteArray();
        if (extraData.Length > BedrockObservationCodecs.MaxItemExtraDataBytes)
            throw new BedrockCodecException($"The {kind} extra data length {extraData.Length} exceeds its byte limit.");
        return new BedrockItemInstance(networkId, count, metadataValue, stackNetworkId, blockRuntimeId, extraData);
    }

    private static byte[] Encode(Action<BedrockWriter> write)
    {
        var output = new ArrayBufferWriter<byte>();
        write(new BedrockWriter(output));
        return output.WrittenSpan.ToArray();
    }

    private static void EnsureEnd(BedrockReader reader)
    {
        if (!reader.End)
            throw new BedrockCodecException($"The inventory payload has {reader.Remaining} unread bytes.");
    }
}
