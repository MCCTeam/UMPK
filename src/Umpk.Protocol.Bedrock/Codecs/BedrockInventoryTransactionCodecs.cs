using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockInventoryTransactionType
{
    Normal = 0,
    InventoryMismatch = 1,
    ItemUse = 2,
    ItemUseOnEntity = 3,
    ItemRelease = 4,
}

internal sealed class BedrockTransactionLegacyEntry : IEquatable<BedrockTransactionLegacyEntry>
{
    internal BedrockTransactionLegacyEntry(byte containerId, IReadOnlyList<byte> changedSlots)
    {
        ArgumentNullException.ThrowIfNull(changedSlots);
        ContainerId = containerId;
        ChangedSlots = [.. changedSlots];
    }

    internal byte ContainerId { get; }

    internal ImmutableArray<byte> ChangedSlots { get; }

    public bool Equals(BedrockTransactionLegacyEntry? other) =>
        other is not null && ContainerId == other.ContainerId && ChangedSlots.SequenceEqual(other.ChangedSlots);

    public override bool Equals(object? obj) => Equals(obj as BedrockTransactionLegacyEntry);

    public override int GetHashCode() => HashCode.Combine(ContainerId, ChangedSlots.Length);
}

internal sealed class BedrockTransactionLegacy : IEquatable<BedrockTransactionLegacy>
{
    internal BedrockTransactionLegacy(int requestId, IReadOnlyList<BedrockTransactionLegacyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        RequestId = requestId;
        Entries = [.. entries];
    }

    internal int RequestId { get; }

    internal ImmutableArray<BedrockTransactionLegacyEntry> Entries { get; }

    public bool Equals(BedrockTransactionLegacy? other) =>
        other is not null && RequestId == other.RequestId && Entries.SequenceEqual(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as BedrockTransactionLegacy);

    public override int GetHashCode() => HashCode.Combine(RequestId, Entries.Length);
}

internal sealed class BedrockTransactionAction : IEquatable<BedrockTransactionAction>
{
    internal BedrockTransactionAction(uint sourceType, bool containerPresence, sbyte? windowId, bool flagPresence, uint? flags, uint slot, BedrockItemInstance oldItem, BedrockItemInstance newItem)
    {
        ArgumentNullException.ThrowIfNull(oldItem);
        ArgumentNullException.ThrowIfNull(newItem);
        SourceType = sourceType;
        ContainerPresence = containerPresence;
        WindowId = windowId;
        FlagPresence = flagPresence;
        Flags = flags;
        Slot = slot;
        OldItem = oldItem;
        NewItem = newItem;
    }

    internal uint SourceType { get; }

    internal bool ContainerPresence { get; }

    internal sbyte? WindowId { get; }

    internal bool FlagPresence { get; }

    internal uint? Flags { get; }

    internal uint Slot { get; }

    internal BedrockItemInstance OldItem { get; }

    internal BedrockItemInstance NewItem { get; }

    public bool Equals(BedrockTransactionAction? other) =>
        other is not null
        && SourceType == other.SourceType
        && ContainerPresence == other.ContainerPresence
        && WindowId == other.WindowId
        && FlagPresence == other.FlagPresence
        && Flags == other.Flags
        && Slot == other.Slot
        && OldItem.Equals(other.OldItem)
        && NewItem.Equals(other.NewItem);

    public override bool Equals(object? obj) => Equals(obj as BedrockTransactionAction);

    public override int GetHashCode() => HashCode.Combine(SourceType, ContainerPresence, WindowId, FlagPresence, Flags, Slot);
}

internal abstract record BedrockInventoryTransactionData;

internal sealed record BedrockItemUseTransactionData(int ActionType, byte TriggerType, int BlockX, int BlockY, int BlockZ, byte Face, int HotbarSlot, BedrockItemInstance HeldItem, BedrockVector3 PlayerPos, BedrockVector3 ClickPos, uint BlockRuntimeId, byte ClientPrediction, byte ClientCooldownState) : BedrockInventoryTransactionData;

internal sealed record BedrockItemUseOnEntityTransactionData(ulong EntityRuntimeId, int ActionType, int HotbarSlot, BedrockItemInstance HeldItem, BedrockVector3 PlayerPos, BedrockVector3 ClickPos) : BedrockInventoryTransactionData;

internal sealed record BedrockItemReleaseTransactionData(int ActionType, int HotbarSlot, BedrockItemInstance HeldItem, BedrockVector3 HeadPos) : BedrockInventoryTransactionData;

internal sealed class BedrockInventoryTransactionPacket : IEquatable<BedrockInventoryTransactionPacket>
{
    internal BedrockInventoryTransactionPacket(BedrockTransactionLegacy legacy, BedrockInventoryTransactionType? transactionType, IReadOnlyList<BedrockTransactionAction> actions, BedrockInventoryTransactionData? data)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(actions);
        if (!Matches(transactionType, data))
            throw new BedrockCodecException("The inventory transaction type carries an incompatible payload.");
        Legacy = legacy;
        TransactionType = transactionType;
        Actions = [.. actions];
        Data = data;
    }

    internal BedrockTransactionLegacy Legacy { get; }

    internal BedrockInventoryTransactionType? TransactionType { get; }

    internal ImmutableArray<BedrockTransactionAction> Actions { get; }

    internal BedrockInventoryTransactionData? Data { get; }

    public bool Equals(BedrockInventoryTransactionPacket? other) =>
        other is not null
        && Legacy.Equals(other.Legacy)
        && TransactionType == other.TransactionType
        && Actions.SequenceEqual(other.Actions)
        && Equals(Data, other.Data);

    public override bool Equals(object? obj) => Equals(obj as BedrockInventoryTransactionPacket);

    public override int GetHashCode() => HashCode.Combine(Legacy, TransactionType, Actions.Length);

    private static bool Matches(BedrockInventoryTransactionType? type, BedrockInventoryTransactionData? data) => (type, data) switch
    {
        (null, null) => true,
        (BedrockInventoryTransactionType.Normal, null) => true,
        (BedrockInventoryTransactionType.InventoryMismatch, null) => true,
        (BedrockInventoryTransactionType.ItemUse, BedrockItemUseTransactionData) => true,
        (BedrockInventoryTransactionType.ItemUseOnEntity, BedrockItemUseOnEntityTransactionData) => true,
        (BedrockInventoryTransactionType.ItemRelease, BedrockItemReleaseTransactionData) => true,
        _ => false,
    };
}

internal static class BedrockInventoryTransactionCodecs
{
    internal const int MaxActionsPerTransaction = 256;
    internal const int MaxLegacyEntries = 64;
    internal const int MaxChangedSlotsPerEntry = 256;

    internal static byte[] Encode(BedrockInventoryTransactionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer => WritePacket(writer, packet));
    }

    internal static BedrockInventoryTransactionPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        BedrockInventoryTransactionPacket packet = ReadPacket(ref reader, actualLimits);
        if (!reader.End)
            throw new BedrockCodecException($"The inventory transaction payload has {reader.Remaining} unread bytes.");
        return packet;
    }

    private static void WritePacket(BedrockWriter writer, BedrockInventoryTransactionPacket packet)
    {
        WriteLegacyEntries(writer, packet.Legacy);

        writer.WriteBoolean(packet.TransactionType.HasValue);
        if (packet.TransactionType.HasValue)
            writer.WriteUInt32VarInt(checked((uint)packet.TransactionType.Value));

        writer.WriteBoolean(packet.Actions.Length > 0);
        if (packet.Actions.Length > 0)
        {
            writer.WriteUInt32VarInt(checked((uint)packet.Actions.Length));
            foreach (BedrockTransactionAction action in packet.Actions)
                WriteAction(writer, action);
        }

        WriteData(writer, packet.TransactionType, packet.Data);
    }

    private static BedrockInventoryTransactionPacket ReadPacket(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int requestId = reader.ReadZigZag32();
        ImmutableArray<BedrockTransactionLegacyEntry> legacyEntries = ReadLegacyEntries(ref reader, limits);
        var legacy = new BedrockTransactionLegacy(requestId, legacyEntries);

        BedrockInventoryTransactionType? transactionType = ReadTransactionType(ref reader);
        ImmutableArray<BedrockTransactionAction> actions = ReadActions(ref reader, limits);
        BedrockInventoryTransactionData? data = ReadData(ref reader, transactionType, limits);
        return new BedrockInventoryTransactionPacket(legacy, transactionType, actions, data);
    }

    internal static ImmutableArray<BedrockTransactionLegacyEntry> ReadLegacyEntries(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        if (!reader.ReadBoolean())
            return [];
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxLegacyEntries || count > (uint)limits.MaxCollectionElements)
            throw new BedrockCodecException($"The legacy transaction count {count} exceeds its element limit.");
        var entries = new List<BedrockTransactionLegacyEntry>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
        {
            byte containerId = reader.ReadByte();
            uint slotCount = reader.ReadUInt32VarInt();
            if (slotCount > MaxChangedSlotsPerEntry || slotCount > (uint)limits.MaxCollectionElements)
                throw new BedrockCodecException($"The legacy slot count {slotCount} exceeds its element limit.");
            var slots = new List<byte>(checked((int)Math.Min(slotCount, 8)));
            for (uint slotIndex = 0; slotIndex < slotCount; slotIndex++)
                slots.Add(reader.ReadByte());
            entries.Add(new BedrockTransactionLegacyEntry(containerId, slots));
        }

        return [.. entries];
    }

    private static BedrockInventoryTransactionType? ReadTransactionType(ref BedrockReader reader)
    {
        if (!reader.ReadBoolean())
            return null;
        uint value = reader.ReadUInt32VarInt();
        if (value > 4 || !Enum.IsDefined(typeof(BedrockInventoryTransactionType), (int)value))
            throw new BedrockCodecException($"The inventory transaction type {value} is not supported.");
        return (BedrockInventoryTransactionType)value;
    }

    internal static ImmutableArray<BedrockTransactionAction> ReadActions(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        if (!reader.ReadBoolean())
            return [];
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxActionsPerTransaction || count > (uint)limits.MaxCollectionElements)
            throw new BedrockCodecException($"The transaction action count {count} exceeds its element limit.");
        var actions = new List<BedrockTransactionAction>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            actions.Add(ReadAction(ref reader, limits));
        return [.. actions];
    }

    internal static void WriteLegacyEntries(BedrockWriter writer, BedrockTransactionLegacy legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        writer.WriteZigZag32(legacy.RequestId);
        writer.WriteBoolean(legacy.Entries.Length > 0);
        if (legacy.Entries.Length > 0)
        {
            writer.WriteUInt32VarInt(checked((uint)legacy.Entries.Length));
            foreach (BedrockTransactionLegacyEntry entry in legacy.Entries)
            {
                writer.WriteByte(entry.ContainerId);
                writer.WriteUInt32VarInt(checked((uint)entry.ChangedSlots.Length));
                foreach (byte slot in entry.ChangedSlots)
                    writer.WriteByte(slot);
            }
        }
    }

    internal static void WriteAction(BedrockWriter writer, BedrockTransactionAction action)
    {
        writer.WriteUInt32VarInt(action.SourceType);
        writer.WriteBoolean(action.ContainerPresence);
        writer.WriteBoolean(action.WindowId.HasValue);
        if (action.WindowId.HasValue)
            writer.WriteByte(unchecked((byte)action.WindowId.Value));
        writer.WriteBoolean(action.FlagPresence);
        writer.WriteBoolean(action.Flags.HasValue);
        if (action.Flags.HasValue)
            writer.WriteUInt32VarInt(action.Flags.Value);
        writer.WriteUInt32VarInt(action.Slot);
        WriteItem(writer, action.OldItem);
        WriteItem(writer, action.NewItem);
    }

    internal static BedrockTransactionAction ReadAction(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        uint sourceType = reader.ReadUInt32VarInt();
        bool containerPresence = reader.ReadBoolean();
        sbyte? windowId = reader.ReadBoolean() ? unchecked((sbyte)reader.ReadByte()) : null;
        bool flagPresence = reader.ReadBoolean();
        uint? flags = reader.ReadBoolean() ? reader.ReadUInt32VarInt() : null;
        uint slot = reader.ReadUInt32VarInt();
        BedrockItemInstance oldItem = ReadItem(ref reader, "Transaction old item", limits);
        BedrockItemInstance newItem = ReadItem(ref reader, "Transaction new item", limits);
        return new BedrockTransactionAction(sourceType, containerPresence, windowId, flagPresence, flags, slot, oldItem, newItem);
    }

    private static void WriteData(BedrockWriter writer, BedrockInventoryTransactionType? type, BedrockInventoryTransactionData? data)
    {
        switch (type, data)
        {
            case (null, null):
            case (BedrockInventoryTransactionType.Normal, null):
            case (BedrockInventoryTransactionType.InventoryMismatch, null):
                break;
            case (BedrockInventoryTransactionType.ItemUse, BedrockItemUseTransactionData use):
                WriteUseItemData(writer, use);
                break;
            case (BedrockInventoryTransactionType.ItemUseOnEntity, BedrockItemUseOnEntityTransactionData entity):
                writer.WriteUInt64VarInt(entity.EntityRuntimeId);
                writer.WriteZigZag32(entity.ActionType);
                writer.WriteZigZag32(entity.HotbarSlot);
                WriteItem(writer, entity.HeldItem);
                WriteVec3(writer, entity.PlayerPos);
                WriteVec3(writer, entity.ClickPos);
                break;
            case (BedrockInventoryTransactionType.ItemRelease, BedrockItemReleaseTransactionData release):
                writer.WriteZigZag32(release.ActionType);
                writer.WriteZigZag32(release.HotbarSlot);
                WriteItem(writer, release.HeldItem);
                WriteVec3(writer, release.HeadPos);
                break;
            default:
                throw new BedrockCodecException("The inventory transaction type carries an incompatible payload.");
        }
    }

    private static BedrockInventoryTransactionData? ReadData(ref BedrockReader reader, BedrockInventoryTransactionType? type, BedrockCodecLimits limits)
    {
        switch (type)
        {
            case null:
            case BedrockInventoryTransactionType.Normal:
            case BedrockInventoryTransactionType.InventoryMismatch:
                return null;
            case BedrockInventoryTransactionType.ItemUse:
                return ReadUseItemData(ref reader, limits);

            case BedrockInventoryTransactionType.ItemUseOnEntity:
                {
                    ulong entityId = reader.ReadUInt64VarInt();
                    int actionType = reader.ReadZigZag32();
                    int hotbarSlot = reader.ReadZigZag32();
                    BedrockItemInstance held = ReadItem(ref reader, "Transaction held item", limits);
                    BedrockVector3 playerPos = ReadVec3(ref reader);
                    BedrockVector3 clickPos = ReadVec3(ref reader);
                    return new BedrockItemUseOnEntityTransactionData(entityId, actionType, hotbarSlot, held, playerPos, clickPos);
                }

            case BedrockInventoryTransactionType.ItemRelease:
                {
                    int actionType = reader.ReadZigZag32();
                    int hotbarSlot = reader.ReadZigZag32();
                    BedrockItemInstance held = ReadItem(ref reader, "Transaction held item", limits);
                    BedrockVector3 headPos = ReadVec3(ref reader);
                    return new BedrockItemReleaseTransactionData(actionType, hotbarSlot, held, headPos);
                }

            default:
                throw new BedrockCodecException($"The inventory transaction type {(int)type.Value} is not supported.");
        }
    }

    internal static BedrockItemUseTransactionData ReadUseItemData(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int actionType = reader.ReadZigZag32();
        byte triggerType = reader.ReadByte();
        int blockX = reader.ReadZigZag32();
        int blockY = reader.ReadZigZag32();
        int blockZ = reader.ReadZigZag32();
        byte face = reader.ReadByte();
        int hotbarSlot = reader.ReadZigZag32();
        BedrockItemInstance held = ReadItem(ref reader, "Transaction held item", limits);
        BedrockVector3 playerPos = ReadVec3(ref reader);
        BedrockVector3 clickPos = ReadVec3(ref reader);
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        byte prediction = reader.ReadByte();
        byte cooldown = reader.ReadByte();
        return new BedrockItemUseTransactionData(actionType, triggerType, blockX, blockY, blockZ, face, hotbarSlot, held, playerPos, clickPos, blockRuntimeId, prediction, cooldown);
    }

    internal static void WriteUseItemData(BedrockWriter writer, BedrockItemUseTransactionData use)
    {
        ArgumentNullException.ThrowIfNull(use);
        writer.WriteZigZag32(use.ActionType);
        writer.WriteByte(use.TriggerType);
        writer.WriteZigZag32(use.BlockX);
        writer.WriteZigZag32(use.BlockY);
        writer.WriteZigZag32(use.BlockZ);
        writer.WriteByte(use.Face);
        writer.WriteZigZag32(use.HotbarSlot);
        WriteItem(writer, use.HeldItem);
        WriteVec3(writer, use.PlayerPos);
        WriteVec3(writer, use.ClickPos);
        writer.WriteUInt32VarInt(use.BlockRuntimeId);
        writer.WriteByte(use.ClientPrediction);
        writer.WriteByte(use.ClientCooldownState);
    }

    internal static void WriteVec3(BedrockWriter writer, BedrockVector3 value)
    {
        EnsureFinite(value.X, "Transaction position X");
        EnsureFinite(value.Y, "Transaction position Y");
        EnsureFinite(value.Z, "Transaction position Z");
        writer.WriteSingleLittleEndian(value.X);
        writer.WriteSingleLittleEndian(value.Y);
        writer.WriteSingleLittleEndian(value.Z);
    }

    internal static BedrockVector3 ReadVec3(ref BedrockReader reader)
    {
        float x = reader.ReadSingleLittleEndian();
        float y = reader.ReadSingleLittleEndian();
        float z = reader.ReadSingleLittleEndian();
        EnsureFinite(x, "Transaction position X");
        EnsureFinite(y, "Transaction position Y");
        EnsureFinite(z, "Transaction position Z");
        return new BedrockVector3(x, y, z);
    }

    private static void EnsureFinite(float value, string kind)
    {
        if (!float.IsFinite(value))
            throw new BedrockCodecException($"The {kind} is not finite.");
    }

    internal static void WriteItem(BedrockWriter writer, BedrockItemInstance item)
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

    internal static BedrockItemInstance ReadItem(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
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
}
