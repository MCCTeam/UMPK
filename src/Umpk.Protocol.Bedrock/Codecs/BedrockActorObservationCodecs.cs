using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockMobEffectEvent : byte
{
    Add = 1,
    Modify = 2,
    Remove = 3,
}

internal readonly record struct BedrockActorEffect(int EffectId, int Amplifier, int Duration, bool Particles, bool Ambient);

internal sealed class BedrockItemInstance : IEquatable<BedrockItemInstance>
{
    internal BedrockItemInstance(short networkId, ushort count, uint metadataValue, int? stackNetworkId, uint blockRuntimeId, byte[] extraData)
    {
        ArgumentNullException.ThrowIfNull(extraData);
        NetworkId = networkId;
        Count = count;
        MetadataValue = metadataValue;
        StackNetworkId = stackNetworkId;
        BlockRuntimeId = blockRuntimeId;
        ExtraData = (byte[])extraData.Clone();
    }

    internal short NetworkId { get; }

    internal ushort Count { get; }

    internal uint MetadataValue { get; }

    internal int? StackNetworkId { get; }

    internal uint BlockRuntimeId { get; }

    internal byte[] ExtraData { get; }

    public bool Equals(BedrockItemInstance? other) =>
        other is not null
        && NetworkId == other.NetworkId
        && Count == other.Count
        && MetadataValue == other.MetadataValue
        && StackNetworkId == other.StackNetworkId
        && BlockRuntimeId == other.BlockRuntimeId
        && ExtraData.AsSpan().SequenceEqual(other.ExtraData);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemInstance);

    public override int GetHashCode() => HashCode.Combine(NetworkId, Count, MetadataValue, BlockRuntimeId);
}

internal sealed class BedrockMobEquipmentPacket : IEquatable<BedrockMobEquipmentPacket>
{
    internal BedrockMobEquipmentPacket(ulong runtimeEntityId, BedrockItemInstance item, byte inventorySlot, byte selectedSlot, sbyte windowId)
    {
        ArgumentNullException.ThrowIfNull(item);
        RuntimeEntityId = runtimeEntityId;
        Item = item;
        InventorySlot = inventorySlot;
        SelectedSlot = selectedSlot;
        WindowId = windowId;
    }

    internal ulong RuntimeEntityId { get; }

    internal BedrockItemInstance Item { get; }

    internal byte InventorySlot { get; }

    internal byte SelectedSlot { get; }

    internal sbyte WindowId { get; }

    public bool Equals(BedrockMobEquipmentPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Item.Equals(other.Item)
        && InventorySlot == other.InventorySlot
        && SelectedSlot == other.SelectedSlot
        && WindowId == other.WindowId;

    public override bool Equals(object? obj) => Equals(obj as BedrockMobEquipmentPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, InventorySlot, SelectedSlot);
}

internal sealed class BedrockMobArmorEquipmentPacket : IEquatable<BedrockMobArmorEquipmentPacket>
{
    internal BedrockMobArmorEquipmentPacket(
        ulong runtimeEntityId,
        BedrockItemInstance helmet,
        BedrockItemInstance chestplate,
        BedrockItemInstance leggings,
        BedrockItemInstance boots,
        BedrockItemInstance body)
    {
        ArgumentNullException.ThrowIfNull(helmet);
        ArgumentNullException.ThrowIfNull(chestplate);
        ArgumentNullException.ThrowIfNull(leggings);
        ArgumentNullException.ThrowIfNull(boots);
        ArgumentNullException.ThrowIfNull(body);
        RuntimeEntityId = runtimeEntityId;
        Helmet = helmet;
        Chestplate = chestplate;
        Leggings = leggings;
        Boots = boots;
        Body = body;
    }

    internal ulong RuntimeEntityId { get; }

    internal BedrockItemInstance Helmet { get; }

    internal BedrockItemInstance Chestplate { get; }

    internal BedrockItemInstance Leggings { get; }

    internal BedrockItemInstance Boots { get; }

    internal BedrockItemInstance Body { get; }

    public bool Equals(BedrockMobArmorEquipmentPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Helmet.Equals(other.Helmet)
        && Chestplate.Equals(other.Chestplate)
        && Leggings.Equals(other.Leggings)
        && Boots.Equals(other.Boots)
        && Body.Equals(other.Body);

    public override bool Equals(object? obj) => Equals(obj as BedrockMobArmorEquipmentPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, Helmet, Chestplate);
}

internal sealed class BedrockHeldItemObservation : IEquatable<BedrockHeldItemObservation>
{
    internal BedrockHeldItemObservation(BedrockItemInstance item, byte inventorySlot, byte selectedSlot, sbyte windowId)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        InventorySlot = inventorySlot;
        SelectedSlot = selectedSlot;
        WindowId = windowId;
    }

    internal BedrockItemInstance Item { get; }

    internal byte InventorySlot { get; }

    internal byte SelectedSlot { get; }

    internal sbyte WindowId { get; }

    public bool Equals(BedrockHeldItemObservation? other) =>
        other is not null
        && Item.Equals(other.Item)
        && InventorySlot == other.InventorySlot
        && SelectedSlot == other.SelectedSlot
        && WindowId == other.WindowId;

    public override bool Equals(object? obj) => Equals(obj as BedrockHeldItemObservation);

    public override int GetHashCode() => HashCode.Combine(Item, InventorySlot, SelectedSlot);
}

internal sealed class BedrockArmorObservation : IEquatable<BedrockArmorObservation>
{
    internal BedrockArmorObservation(
        BedrockItemInstance helmet,
        BedrockItemInstance chestplate,
        BedrockItemInstance leggings,
        BedrockItemInstance boots,
        BedrockItemInstance body)
    {
        ArgumentNullException.ThrowIfNull(helmet);
        ArgumentNullException.ThrowIfNull(chestplate);
        ArgumentNullException.ThrowIfNull(leggings);
        ArgumentNullException.ThrowIfNull(boots);
        ArgumentNullException.ThrowIfNull(body);
        Helmet = helmet;
        Chestplate = chestplate;
        Leggings = leggings;
        Boots = boots;
        Body = body;
    }

    internal BedrockItemInstance Helmet { get; }

    internal BedrockItemInstance Chestplate { get; }

    internal BedrockItemInstance Leggings { get; }

    internal BedrockItemInstance Boots { get; }

    internal BedrockItemInstance Body { get; }

    public bool Equals(BedrockArmorObservation? other) =>
        other is not null
        && Helmet.Equals(other.Helmet)
        && Chestplate.Equals(other.Chestplate)
        && Leggings.Equals(other.Leggings)
        && Boots.Equals(other.Boots)
        && Body.Equals(other.Body);

    public override bool Equals(object? obj) => Equals(obj as BedrockArmorObservation);

    public override int GetHashCode() => HashCode.Combine(Helmet, Chestplate, Leggings);
}

internal readonly record struct BedrockAttributeModifier(string Id, string Name, float Amount, int Operation, int Operand, bool Serializable);

internal sealed class BedrockPlayerAttribute : IEquatable<BedrockPlayerAttribute>
{
    internal BedrockPlayerAttribute(
        float min,
        float max,
        float current,
        float defaultMin,
        float defaultMax,
        float @default,
        string name,
        ImmutableArray<BedrockAttributeModifier> modifiers)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || !float.IsFinite(current) || !float.IsFinite(defaultMin) || !float.IsFinite(defaultMax) || !float.IsFinite(@default))
            throw new BedrockCodecException("The player attribute contains a non-finite bound.");
        ArgumentException.ThrowIfNullOrEmpty(name);
        Min = min;
        Max = max;
        Current = current;
        DefaultMin = defaultMin;
        DefaultMax = defaultMax;
        Default = @default;
        Name = name;
        Modifiers = modifiers;
    }

    internal float Min { get; }

    internal float Max { get; }

    internal float Current { get; }

    internal float DefaultMin { get; }

    internal float DefaultMax { get; }

    internal float Default { get; }

    internal string Name { get; }

    internal ImmutableArray<BedrockAttributeModifier> Modifiers { get; }

    public bool Equals(BedrockPlayerAttribute? other) =>
        other is not null
        && Min.Equals(other.Min)
        && Max.Equals(other.Max)
        && Current.Equals(other.Current)
        && DefaultMin.Equals(other.DefaultMin)
        && DefaultMax.Equals(other.DefaultMax)
        && Default.Equals(other.Default)
        && Name.Equals(other.Name, StringComparison.Ordinal)
        && Modifiers.SequenceEqual(other.Modifiers);

    public override bool Equals(object? obj) => Equals(obj as BedrockPlayerAttribute);

    public override int GetHashCode() => HashCode.Combine(Min, Max, Current, Name);
}

internal sealed class BedrockUpdateAttributesPacket : IEquatable<BedrockUpdateAttributesPacket>
{
    internal BedrockUpdateAttributesPacket(ulong runtimeEntityId, ImmutableArray<BedrockPlayerAttribute> attributes, ulong tick)
    {
        RuntimeEntityId = runtimeEntityId;
        Attributes = attributes;
        Tick = tick;
    }

    internal ulong RuntimeEntityId { get; }

    internal ImmutableArray<BedrockPlayerAttribute> Attributes { get; }

    internal ulong Tick { get; }

    public bool Equals(BedrockUpdateAttributesPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Tick == other.Tick
        && Attributes.SequenceEqual(other.Attributes);

    public override bool Equals(object? obj) => Equals(obj as BedrockUpdateAttributesPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, Tick);
}

internal sealed class BedrockMobEffectPacket : IEquatable<BedrockMobEffectPacket>
{
    internal BedrockMobEffectPacket(
        ulong runtimeEntityId,
        BedrockMobEffectEvent @event,
        int effectId,
        int amplifier,
        bool particles,
        int duration,
        ulong tick,
        bool ambient)
    {
        if (!Enum.IsDefined(@event))
            throw new BedrockCodecException($"Unknown MobEffect event {(byte)@event}.");
        RuntimeEntityId = runtimeEntityId;
        Event = @event;
        EffectId = effectId;
        Amplifier = amplifier;
        Particles = particles;
        Duration = duration;
        Tick = tick;
        Ambient = ambient;
    }

    internal ulong RuntimeEntityId { get; }

    internal BedrockMobEffectEvent Event { get; }

    internal int EffectId { get; }

    internal int Amplifier { get; }

    internal bool Particles { get; }

    internal int Duration { get; }

    internal ulong Tick { get; }

    internal bool Ambient { get; }

    public bool Equals(BedrockMobEffectPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Event == other.Event
        && EffectId == other.EffectId
        && Amplifier == other.Amplifier
        && Particles == other.Particles
        && Duration == other.Duration
        && Tick == other.Tick
        && Ambient == other.Ambient;

    public override bool Equals(object? obj) => Equals(obj as BedrockMobEffectPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, Event, EffectId);
}

internal static class BedrockObservationCodecs
{
    internal const int MaxAttributesPerPacket = 1024;
    internal const int MaxAttributeModifiers = 256;
    internal const int MaxItemExtraDataBytes = 1024 * 1024;
    internal static byte[] Encode(BedrockMobEffectPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            writer.WriteByte((byte)packet.Event);
            writer.WriteZigZag32(packet.EffectId);
            writer.WriteZigZag32(packet.Amplifier);
            writer.WriteBoolean(packet.Particles);
            writer.WriteZigZag32(packet.Duration);
            writer.WriteUInt64VarInt(packet.Tick);
            writer.WriteBoolean(packet.Ambient);
        });
    }

    internal static BedrockMobEffectPacket DecodeMobEffect(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        byte eventValue = reader.ReadByte();
        if (!Enum.IsDefined((BedrockMobEffectEvent)eventValue))
            throw new BedrockCodecException($"Unknown MobEffect event {eventValue}.");
        int effectId = reader.ReadZigZag32();
        int amplifier = reader.ReadZigZag32();
        bool particles = reader.ReadBoolean();
        int duration = reader.ReadZigZag32();
        ulong tick = reader.ReadUInt64VarInt();
        bool ambient = reader.ReadBoolean();
        EnsureEnd(reader);
        return new BedrockMobEffectPacket(runtimeEntityId, (BedrockMobEffectEvent)eventValue, effectId, amplifier, particles, duration, tick, ambient);
    }

    internal static byte[] Encode(BedrockUpdateAttributesPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            writer.WriteUInt32VarInt(checked((uint)packet.Attributes.Length));
            foreach (BedrockPlayerAttribute attribute in packet.Attributes)
                WritePlayerAttribute(writer, attribute);
            writer.WriteUInt64VarInt(packet.Tick);
        });
    }

    internal static BedrockUpdateAttributesPacket DecodeUpdateAttributes(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxAttributesPerPacket)
            throw new BedrockCodecException($"The UpdateAttributes count {count} exceeds its element limit.");
        var attributes = new List<BedrockPlayerAttribute>(checked((int)Math.Min(count, 16)));
        for (uint index = 0; index < count; index++)
            attributes.Add(ReadPlayerAttribute(ref reader, actualLimits));
        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new BedrockUpdateAttributesPacket(runtimeEntityId, [.. attributes], tick);
    }

    internal static byte[] Encode(BedrockMobEquipmentPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            WriteItemInstance(writer, packet.Item);
            writer.WriteByte(packet.InventorySlot);
            writer.WriteByte(packet.SelectedSlot);
            writer.WriteByte(unchecked((byte)packet.WindowId));
        });
    }

    internal static BedrockMobEquipmentPacket DecodeMobEquipment(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        BedrockItemInstance item = ReadItemInstance(ref reader, "MobEquipment item", actualLimits);
        byte inventorySlot = reader.ReadByte();
        byte selectedSlot = reader.ReadByte();
        sbyte windowId = unchecked((sbyte)reader.ReadByte());
        EnsureEnd(reader);
        return new BedrockMobEquipmentPacket(runtimeEntityId, item, inventorySlot, selectedSlot, windowId);
    }

    internal static byte[] Encode(BedrockMobArmorEquipmentPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            WriteItemInstance(writer, packet.Helmet);
            WriteItemInstance(writer, packet.Chestplate);
            WriteItemInstance(writer, packet.Leggings);
            WriteItemInstance(writer, packet.Boots);
            WriteItemInstance(writer, packet.Body);
        });
    }

    internal static BedrockMobArmorEquipmentPacket DecodeMobArmorEquipment(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        BedrockItemInstance helmet = ReadItemInstance(ref reader, "MobArmorEquipment helmet", actualLimits);
        BedrockItemInstance chestplate = ReadItemInstance(ref reader, "MobArmorEquipment chestplate", actualLimits);
        BedrockItemInstance leggings = ReadItemInstance(ref reader, "MobArmorEquipment leggings", actualLimits);
        BedrockItemInstance boots = ReadItemInstance(ref reader, "MobArmorEquipment boots", actualLimits);
        BedrockItemInstance body = ReadItemInstance(ref reader, "MobArmorEquipment body", actualLimits);
        EnsureEnd(reader);
        return new BedrockMobArmorEquipmentPacket(runtimeEntityId, helmet, chestplate, leggings, boots, body);
    }

    internal static byte[] Encode(BedrockSetEntityLinkPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer => BedrockActorSections.WriteLink(writer, packet.Link));
    }

    internal static BedrockSetEntityLinkPacket DecodeSetEntityLink(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        BedrockEntityLink link = BedrockActorSections.ReadLink(ref reader, "SetEntityLink");
        EnsureEnd(reader);
        return new BedrockSetEntityLinkPacket(link);
    }

    private static void WriteItemInstance(BedrockWriter writer, BedrockItemInstance item) => BedrockActorSections.WriteItemInstance(writer, item);

    private static BedrockItemInstance ReadItemInstance(ref BedrockReader reader, string kind, BedrockCodecLimits limits) => BedrockActorSections.ReadItemInstance(ref reader, kind, limits);

    private static void WritePlayerAttribute(BedrockWriter writer, BedrockPlayerAttribute attribute)
    {
        writer.WriteSingleLittleEndian(attribute.Min);
        writer.WriteSingleLittleEndian(attribute.Max);
        writer.WriteSingleLittleEndian(attribute.Current);
        writer.WriteSingleLittleEndian(attribute.DefaultMin);
        writer.WriteSingleLittleEndian(attribute.DefaultMax);
        writer.WriteSingleLittleEndian(attribute.Default);
        writer.WriteString(attribute.Name);
        writer.WriteUInt32VarInt(checked((uint)attribute.Modifiers.Length));
        foreach (BedrockAttributeModifier modifier in attribute.Modifiers)
        {
            writer.WriteString(modifier.Id);
            writer.WriteString(modifier.Name);
            writer.WriteSingleLittleEndian(modifier.Amount);
            writer.WriteInt32LittleEndian(modifier.Operation);
            writer.WriteInt32LittleEndian(modifier.Operand);
            writer.WriteBoolean(modifier.Serializable);
        }
    }

    private static BedrockPlayerAttribute ReadPlayerAttribute(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        float min = ReadFiniteSingle(ref reader, "UpdateAttributes minimum");
        float max = ReadFiniteSingle(ref reader, "UpdateAttributes maximum");
        float current = ReadFiniteSingle(ref reader, "UpdateAttributes value");
        float defaultMin = ReadFiniteSingle(ref reader, "UpdateAttributes default minimum");
        float defaultMax = ReadFiniteSingle(ref reader, "UpdateAttributes default maximum");
        float @default = ReadFiniteSingle(ref reader, "UpdateAttributes default");
        string name = reader.ReadString();
        if (name.Length == 0)
            throw new BedrockCodecException("The UpdateAttributes name must not be empty.");
        uint modifierCount = reader.ReadUInt32VarInt();
        if (modifierCount > MaxAttributeModifiers)
            throw new BedrockCodecException($"The UpdateAttributes modifier count {modifierCount} exceeds its element limit.");
        var modifiers = new List<BedrockAttributeModifier>(checked((int)Math.Min(modifierCount, 8)));
        for (uint index = 0; index < modifierCount; index++)
        {
            string id = reader.ReadString();
            string modifierName = reader.ReadString();
            float amount = ReadFiniteSingle(ref reader, "UpdateAttributes modifier amount");
            int operation = reader.ReadInt32LittleEndian();
            int operand = reader.ReadInt32LittleEndian();
            bool serializable = reader.ReadBoolean();
            modifiers.Add(new BedrockAttributeModifier(id, modifierName, amount, operation, operand, serializable));
        }

        return new BedrockPlayerAttribute(min, max, current, defaultMin, defaultMax, @default, name, [.. modifiers]);
    }

    private static float ReadFiniteSingle(ref BedrockReader reader, string kind)
    {
        float value = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(value))
            throw new BedrockCodecException($"The {kind} is non-finite.");
        return value;
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
            throw new BedrockCodecException($"The actor observation payload has {reader.Remaining} unread bytes.");
    }
}
