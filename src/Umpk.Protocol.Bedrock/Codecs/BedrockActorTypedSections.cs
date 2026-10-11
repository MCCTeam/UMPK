using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockVec3i(int X, int Y, int Z);

internal enum BedrockMetadataValueKind : byte
{
    Byte = 0,
    Short = 1,
    Int = 2,
    Float = 3,
    String = 4,
    Compound = 5,
    Vec3i = 6,
    Long = 7,
    Vec3f = 8,
}

internal sealed class BedrockMetadataValue : IEquatable<BedrockMetadataValue>
{
    private BedrockMetadataValue(BedrockMetadataValueKind kind, long signed, float real, string? text, byte[]? raw, BedrockVec3i vec3i, BedrockVector3 vec3f)
    {
        Kind = kind;
        Signed = signed;
        Real = real;
        Text = text;
        Raw = raw;
        Vec3i = vec3i;
        Vec3f = vec3f;
    }

    internal BedrockMetadataValueKind Kind { get; }

    internal long Signed { get; }

    internal float Real { get; }

    internal string? Text { get; }

    internal byte[]? Raw { get; }

    internal BedrockVec3i Vec3i { get; }

    internal BedrockVector3 Vec3f { get; }

    internal static BedrockMetadataValue FromByte(byte value) => new(BedrockMetadataValueKind.Byte, value, 0f, null, null, default, default);

    internal static BedrockMetadataValue FromShort(short value) => new(BedrockMetadataValueKind.Short, value, 0f, null, null, default, default);

    internal static BedrockMetadataValue FromInt(int value) => new(BedrockMetadataValueKind.Int, value, 0f, null, null, default, default);

    internal static BedrockMetadataValue FromFloat(float value) => new(BedrockMetadataValueKind.Float, 0L, value, null, null, default, default);

    internal static BedrockMetadataValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BedrockMetadataValue(BedrockMetadataValueKind.String, 0L, 0f, value, null, default, default);
    }

    internal static BedrockMetadataValue FromCompound(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BedrockMetadataValue(BedrockMetadataValueKind.Compound, 0L, 0f, null, (byte[])value.Clone(), default, default);
    }

    internal static BedrockMetadataValue FromVec3i(BedrockVec3i value) => new(BedrockMetadataValueKind.Vec3i, 0L, 0f, null, null, value, default);

    internal static BedrockMetadataValue FromLong(long value) => new(BedrockMetadataValueKind.Long, value, 0f, null, null, default, default);

    internal static BedrockMetadataValue FromVec3f(BedrockVector3 value) => new(BedrockMetadataValueKind.Vec3f, 0L, 0f, null, null, default, value);

    public bool Equals(BedrockMetadataValue? other) =>
        other is not null
        && Kind == other.Kind
        && Signed == other.Signed
        && Real.Equals(other.Real)
        && Text == other.Text
        && (Raw ?? []).AsSpan().SequenceEqual(other.Raw ?? [])
        && Vec3i.Equals(other.Vec3i)
        && Vec3f.Equals(other.Vec3f);

    public override bool Equals(object? obj) => Equals(obj as BedrockMetadataValue);

    public override int GetHashCode() => HashCode.Combine(Kind, Signed, Real, Text);
}

internal sealed class BedrockMetadataEntry : IEquatable<BedrockMetadataEntry>
{
    internal BedrockMetadataEntry(uint key, uint type, byte legacyType, BedrockMetadataValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        Type = type;
        LegacyType = legacyType;
        Value = value;
    }

    internal uint Key { get; }

    internal uint Type { get; }

    internal byte LegacyType { get; }

    internal BedrockMetadataValue Value { get; }

    public bool Equals(BedrockMetadataEntry? other) =>
        other is not null
        && Key == other.Key
        && Type == other.Type
        && LegacyType == other.LegacyType
        && Value.Equals(other.Value);

    public override bool Equals(object? obj) => Equals(obj as BedrockMetadataEntry);

    public override int GetHashCode() => HashCode.Combine(Key, Type, Value);
}

internal readonly record struct BedrockEntityAttributeValue(string Name, float Min, float Value, float Max);

internal readonly record struct BedrockIndexedInt(uint Index, int Value);

internal readonly record struct BedrockIndexedFloat(uint Index, float Value);

internal sealed class BedrockEntityProperties : IEquatable<BedrockEntityProperties>
{
    internal BedrockEntityProperties(ImmutableArray<BedrockIndexedInt> ints, ImmutableArray<BedrockIndexedFloat> floats)
    {
        Ints = ints;
        Floats = floats;
    }

    internal static BedrockEntityProperties Empty { get; } = new([], []);

    internal ImmutableArray<BedrockIndexedInt> Ints { get; }

    internal ImmutableArray<BedrockIndexedFloat> Floats { get; }

    public bool Equals(BedrockEntityProperties? other) =>
        other is not null && Ints.SequenceEqual(other.Ints) && Floats.SequenceEqual(other.Floats);

    public override bool Equals(object? obj) => Equals(obj as BedrockEntityProperties);

    public override int GetHashCode() => HashCode.Combine(Ints.Length, Floats.Length);
}

internal sealed class BedrockAbilityLayer : IEquatable<BedrockAbilityLayer>
{
    internal BedrockAbilityLayer(ushort type, uint allowed, uint enabled, float flySpeed, float verticalFlySpeed, float walkSpeed)
    {
        if (!float.IsFinite(flySpeed) || !float.IsFinite(verticalFlySpeed) || !float.IsFinite(walkSpeed))
            throw new BedrockCodecException("The ability layer contains a non-finite speed.");
        Type = type;
        Allowed = allowed;
        Enabled = enabled;
        FlySpeed = flySpeed;
        VerticalFlySpeed = verticalFlySpeed;
        WalkSpeed = walkSpeed;
    }

    internal ushort Type { get; }

    internal uint Allowed { get; }

    internal uint Enabled { get; }

    internal float FlySpeed { get; }

    internal float VerticalFlySpeed { get; }

    internal float WalkSpeed { get; }

    public bool Equals(BedrockAbilityLayer? other) =>
        other is not null
        && Type == other.Type
        && Allowed == other.Allowed
        && Enabled == other.Enabled
        && FlySpeed.Equals(other.FlySpeed)
        && VerticalFlySpeed.Equals(other.VerticalFlySpeed)
        && WalkSpeed.Equals(other.WalkSpeed);

    public override bool Equals(object? obj) => Equals(obj as BedrockAbilityLayer);

    public override int GetHashCode() => HashCode.Combine(Type, Allowed, Enabled);
}

internal enum BedrockEntityLinkType : byte
{
    Remove = 0,
    Rider = 1,
    Passenger = 2,
}

internal readonly record struct BedrockEntityLink(long RiddenUniqueId, long RiderUniqueId, BedrockEntityLinkType Type, bool Immediate, bool RiderInitiated, float AngularVelocity);

internal sealed record BedrockSetEntityLinkPacket(BedrockEntityLink Link);

internal static class BedrockActorSections
{
    internal const int MaxMetadataEntries = 1024;
    internal const int MaxEntityAttributes = 1024;
    internal const int MaxPropertyValues = 1024;
    internal const int MaxAbilityLayers = 64;
    internal const int MaxLinks = 1024;

    private const uint MetadataKeyFlags = 0U;
    private const uint MetadataKeyFlagsExtended = 92U;
    private const uint MetadataKeySeatCameraRadius = 134U;
    private const uint MetadataKeySeatCameraSmoothing = 135U;

    private const uint MetadataTypeByte = 0U;
    private const uint MetadataTypeShort = 1U;
    private const uint MetadataTypeInt = 2U;
    private const uint MetadataTypeFloat = 3U;
    private const uint MetadataTypeString = 4U;
    private const uint MetadataTypeCompound = 5U;
    private const uint MetadataTypeVec3i = 6U;
    private const uint MetadataTypeLong = 7U;
    private const uint MetadataTypeVec3f = 8U;

    internal static ImmutableArray<BedrockMetadataEntry> ParseMetadataDictionary(ReadOnlySpan<byte> payload, string kind, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ImmutableArray<BedrockMetadataEntry> entries = ParseMetadataDictionary(payload, ref reader, kind, actualLimits);
        EnsureEnd(reader, kind);
        return entries;
    }

    internal static ImmutableArray<BedrockMetadataEntry> ParseMetadataDictionary(ReadOnlySpan<byte> payload, ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        int count = ReadCount(ref reader, kind, MaxMetadataEntries, limits);
        var entries = new List<BedrockMetadataEntry>(Math.Min(count, 32));
        for (int index = 0; index < count; index++)
            entries.Add(ReadMetadataEntry(payload, ref reader, kind, limits));
        return [.. entries];
    }

    internal static byte[] EncodeMetadataDictionary(ImmutableArray<BedrockMetadataEntry> entries)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)entries.Length));
        foreach (BedrockMetadataEntry entry in entries)
            WriteMetadataEntry(writer, entry);
        return output.WrittenSpan.ToArray();
    }

    internal static ImmutableArray<BedrockEntityAttributeValue> ParseEntityAttributes(ReadOnlySpan<byte> payload, string kind, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ImmutableArray<BedrockEntityAttributeValue> values = ParseEntityAttributes(ref reader, kind, actualLimits);
        EnsureEnd(reader, kind);
        return values;
    }

    internal static ImmutableArray<BedrockEntityAttributeValue> ParseEntityAttributes(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        int count = ReadCount(ref reader, kind, MaxEntityAttributes, limits);
        var values = new List<BedrockEntityAttributeValue>(Math.Min(count, 16));
        for (int index = 0; index < count; index++)
        {
            string name = reader.ReadString();
            if (name.Length == 0)
                throw new BedrockCodecException($"The {kind} name must not be empty.");
            float min = ReadFiniteSingle(ref reader, kind);
            float value = ReadFiniteSingle(ref reader, kind);
            float max = ReadFiniteSingle(ref reader, kind);
            values.Add(new BedrockEntityAttributeValue(name, min, value, max));
        }

        return [.. values];
    }

    internal static byte[] EncodeEntityAttributes(ImmutableArray<BedrockEntityAttributeValue> values)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)values.Length));
        foreach (BedrockEntityAttributeValue value in values)
        {
            writer.WriteString(value.Name);
            writer.WriteSingleLittleEndian(value.Min);
            writer.WriteSingleLittleEndian(value.Value);
            writer.WriteSingleLittleEndian(value.Max);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static BedrockEntityProperties ParseEntityProperties(ReadOnlySpan<byte> payload, string kind, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        BedrockEntityProperties properties = ParseEntityProperties(ref reader, kind, actualLimits);
        EnsureEnd(reader, kind);
        return properties;
    }

    internal static BedrockEntityProperties ParseEntityProperties(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        int ints = ReadCount(ref reader, kind, MaxPropertyValues, limits);
        var intValues = new List<BedrockIndexedInt>(Math.Min(ints, 8));
        for (int index = 0; index < ints; index++)
            intValues.Add(new BedrockIndexedInt(reader.ReadUInt32VarInt(), reader.ReadZigZag32()));
        int floats = ReadCount(ref reader, kind, MaxPropertyValues, limits);
        var floatValues = new List<BedrockIndexedFloat>(Math.Min(floats, 8));
        for (int index = 0; index < floats; index++)
            floatValues.Add(new BedrockIndexedFloat(reader.ReadUInt32VarInt(), ReadFiniteSingle(ref reader, kind)));
        return new BedrockEntityProperties([.. intValues], [.. floatValues]);
    }

    internal static byte[] EncodeEntityProperties(BedrockEntityProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)properties.Ints.Length));
        foreach (BedrockIndexedInt value in properties.Ints)
        {
            writer.WriteUInt32VarInt(value.Index);
            writer.WriteZigZag32(value.Value);
        }

        writer.WriteUInt32VarInt(checked((uint)properties.Floats.Length));
        foreach (BedrockIndexedFloat value in properties.Floats)
        {
            writer.WriteUInt32VarInt(value.Index);
            writer.WriteSingleLittleEndian(value.Value);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static ImmutableArray<BedrockAbilityLayer> ParseAbilityLayers(ReadOnlySpan<byte> payload, string kind, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ImmutableArray<BedrockAbilityLayer> layers = ParseAbilityLayers(ref reader, kind, actualLimits);
        EnsureEnd(reader, kind);
        return layers;
    }

    internal static ImmutableArray<BedrockAbilityLayer> ParseAbilityLayers(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxAbilityLayers || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The {kind} count {count} exceeds its element limit.");
        var layers = new List<BedrockAbilityLayer>(checked((int)count));
        for (uint index = 0; index < count; index++)
        {
            ushort type = reader.ReadUInt16LittleEndian();
            uint allowed = reader.ReadUInt32LittleEndian();
            uint enabled = reader.ReadUInt32LittleEndian();
            float flySpeed = ReadFiniteSingle(ref reader, kind);
            float verticalFlySpeed = ReadFiniteSingle(ref reader, kind);
            float walkSpeed = ReadFiniteSingle(ref reader, kind);
            layers.Add(new BedrockAbilityLayer(type, allowed, enabled, flySpeed, verticalFlySpeed, walkSpeed));
        }

        return [.. layers];
    }

    internal static byte[] EncodeAbilityLayers(ImmutableArray<BedrockAbilityLayer> layers)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)layers.Length));
        foreach (BedrockAbilityLayer layer in layers)
        {
            writer.WriteUInt16LittleEndian(layer.Type);
            writer.WriteUInt32LittleEndian(layer.Allowed);
            writer.WriteUInt32LittleEndian(layer.Enabled);
            writer.WriteSingleLittleEndian(layer.FlySpeed);
            writer.WriteSingleLittleEndian(layer.VerticalFlySpeed);
            writer.WriteSingleLittleEndian(layer.WalkSpeed);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static ImmutableArray<BedrockEntityLink> ParseLinks(ReadOnlySpan<byte> payload, string kind, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ImmutableArray<BedrockEntityLink> links = ParseLinks(ref reader, kind, actualLimits);
        EnsureEnd(reader, kind);
        return links;
    }

    internal static ImmutableArray<BedrockEntityLink> ParseLinks(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        int count = ReadCount(ref reader, kind, MaxLinks, limits);
        var links = new List<BedrockEntityLink>(Math.Min(count, 4));
        for (int index = 0; index < count; index++)
            links.Add(ReadLink(ref reader, kind));
        return [.. links];
    }

    internal static byte[] EncodeLinks(ImmutableArray<BedrockEntityLink> links)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)links.Length));
        foreach (BedrockEntityLink link in links)
            WriteLink(writer, link);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockEntityLink ReadLink(ref BedrockReader reader, string kind)
    {
        long ridden = reader.ReadZigZag64();
        long rider = reader.ReadZigZag64();
        byte typeValue = reader.ReadByte();
        if (!Enum.IsDefined((BedrockEntityLinkType)typeValue))
            throw new BedrockCodecException($"The {kind} link type {typeValue} is unknown.");
        bool immediate = reader.ReadBoolean();
        bool riderInitiated = reader.ReadBoolean();
        float angularVelocity = ReadFiniteSingle(ref reader, kind);
        return new BedrockEntityLink(ridden, rider, (BedrockEntityLinkType)typeValue, immediate, riderInitiated, angularVelocity);
    }

    internal static void WriteLink(BedrockWriter writer, BedrockEntityLink link)
    {
        writer.WriteZigZag64(link.RiddenUniqueId);
        writer.WriteZigZag64(link.RiderUniqueId);
        writer.WriteByte((byte)link.Type);
        writer.WriteBoolean(link.Immediate);
        writer.WriteBoolean(link.RiderInitiated);
        writer.WriteSingleLittleEndian(link.AngularVelocity);
    }

    internal static BedrockItemInstance ReadItemInstance(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        short networkId = reader.ReadInt16LittleEndian();
        ushort count = reader.ReadUInt16LittleEndian();
        uint metadataValue = reader.ReadUInt32VarInt();
        int? stackNetworkId = reader.ReadBoolean() ? reader.ReadZigZag32() : null;
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        uint extraLength = reader.ReadUInt32VarInt();
        if (extraLength > BedrockObservationCodecs.MaxItemExtraDataBytes || extraLength > limits.MaxByteArrayBytes)
            throw new BedrockCodecException($"The {kind} extra data length {extraLength} exceeds its byte limit.");
        byte[] extraData = reader.ReadExact(checked((int)extraLength)).ToArray();
        return new BedrockItemInstance(networkId, count, metadataValue, stackNetworkId, blockRuntimeId, extraData);
    }

    internal static void WriteItemInstance(BedrockWriter writer, BedrockItemInstance item)
    {
        writer.WriteInt16LittleEndian(item.NetworkId);
        writer.WriteUInt16LittleEndian(item.Count);
        writer.WriteUInt32VarInt(item.MetadataValue);
        writer.WriteBoolean(item.StackNetworkId.HasValue);
        if (item.StackNetworkId.HasValue)
            writer.WriteZigZag32(item.StackNetworkId.Value);
        writer.WriteUInt32VarInt(item.BlockRuntimeId);
        writer.WriteUInt32VarInt(checked((uint)item.ExtraData.Length));
        writer.WriteBytes(item.ExtraData);
    }

    private static BedrockMetadataEntry ReadMetadataEntry(ReadOnlySpan<byte> payload, ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        uint key = reader.ReadUInt32VarInt();
        uint type = reader.ReadUInt32VarInt();
        byte legacyType = reader.ReadByte();
        BedrockMetadataValue value;
        if (key == MetadataKeyFlags || key == MetadataKeyFlagsExtended)
        {
            value = BedrockMetadataValue.FromLong(reader.ReadZigZag64());
        }
        else if (key == MetadataKeySeatCameraRadius || key == MetadataKeySeatCameraSmoothing)
        {
            value = BedrockMetadataValue.FromFloat(ReadFiniteSingle(ref reader, kind));
        }
        else
        {
            value = ReadMetadataValue(payload, ref reader, type, kind, limits);
        }

        return new BedrockMetadataEntry(key, type, legacyType, value);
    }

    private static BedrockMetadataValue ReadMetadataValue(ReadOnlySpan<byte> payload, ref BedrockReader reader, uint type, string kind, BedrockCodecLimits limits)
    {
        return type switch
        {
            MetadataTypeByte => BedrockMetadataValue.FromByte(reader.ReadByte()),
            MetadataTypeShort => BedrockMetadataValue.FromShort(reader.ReadInt16LittleEndian()),
            MetadataTypeInt => BedrockMetadataValue.FromInt(reader.ReadZigZag32()),
            MetadataTypeFloat => BedrockMetadataValue.FromFloat(ReadFiniteSingle(ref reader, kind)),
            MetadataTypeString => BedrockMetadataValue.FromString(reader.ReadString()),
            MetadataTypeCompound => ReadMetadataCompound(payload, ref reader, limits),
            MetadataTypeVec3i => BedrockMetadataValue.FromVec3i(new BedrockVec3i(reader.ReadZigZag32(), reader.ReadZigZag32(), reader.ReadZigZag32())),
            MetadataTypeLong => BedrockMetadataValue.FromLong(reader.ReadZigZag64()),
            MetadataTypeVec3f => BedrockMetadataValue.FromVec3f(new BedrockVector3(ReadFiniteSingle(ref reader, kind), ReadFiniteSingle(ref reader, kind), ReadFiniteSingle(ref reader, kind))),
            _ => throw new BedrockCodecException($"Unknown metadata value type {type}."),
        };
    }

    private static BedrockMetadataValue ReadMetadataCompound(ReadOnlySpan<byte> payload, ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int start = reader.Consumed;
        _ = BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, limits);
        return BedrockMetadataValue.FromCompound(payload.Slice(start, reader.Consumed - start).ToArray());
    }

    private static void WriteMetadataEntry(BedrockWriter writer, BedrockMetadataEntry entry)
    {
        writer.WriteUInt32VarInt(entry.Key);
        writer.WriteUInt32VarInt(entry.Type);
        writer.WriteByte(entry.LegacyType);
        WriteMetadataValue(writer, entry);
    }

    private static void WriteMetadataValue(BedrockWriter writer, BedrockMetadataEntry entry)
    {
        if (entry.Key == MetadataKeyFlags || entry.Key == MetadataKeyFlagsExtended)
        {
            RequireKind(entry, BedrockMetadataValueKind.Long);
            writer.WriteZigZag64(entry.Value.Signed);
            return;
        }

        if (entry.Key == MetadataKeySeatCameraRadius || entry.Key == MetadataKeySeatCameraSmoothing)
        {
            RequireKind(entry, BedrockMetadataValueKind.Float);
            writer.WriteSingleLittleEndian(entry.Value.Real);
            return;
        }

        switch (entry.Value.Kind)
        {
            case BedrockMetadataValueKind.Byte:
                writer.WriteByte(unchecked((byte)entry.Value.Signed));
                break;
            case BedrockMetadataValueKind.Short:
                writer.WriteInt16LittleEndian(unchecked((short)entry.Value.Signed));
                break;
            case BedrockMetadataValueKind.Int:
                writer.WriteZigZag32(unchecked((int)entry.Value.Signed));
                break;
            case BedrockMetadataValueKind.Float:
                writer.WriteSingleLittleEndian(entry.Value.Real);
                break;
            case BedrockMetadataValueKind.String:
                writer.WriteString(entry.Value.Text ?? throw new BedrockCodecException("The metadata string value is missing."));
                break;
            case BedrockMetadataValueKind.Compound:
                writer.WriteBytes(entry.Value.Raw ?? throw new BedrockCodecException("The metadata compound value is missing."));
                break;
            case BedrockMetadataValueKind.Vec3i:
                writer.WriteZigZag32(entry.Value.Vec3i.X);
                writer.WriteZigZag32(entry.Value.Vec3i.Y);
                writer.WriteZigZag32(entry.Value.Vec3i.Z);
                break;
            case BedrockMetadataValueKind.Long:
                writer.WriteZigZag64(entry.Value.Signed);
                break;
            case BedrockMetadataValueKind.Vec3f:
                writer.WriteSingleLittleEndian(entry.Value.Vec3f.X);
                writer.WriteSingleLittleEndian(entry.Value.Vec3f.Y);
                writer.WriteSingleLittleEndian(entry.Value.Vec3f.Z);
                break;
            default:
                throw new BedrockCodecException($"Unknown metadata value kind {(byte)entry.Value.Kind}.");
        }
    }

    private static void RequireKind(BedrockMetadataEntry entry, BedrockMetadataValueKind kind)
    {
        if (entry.Value.Kind != kind)
            throw new BedrockCodecException($"The metadata entry {entry.Key} has an unexpected value kind {(byte)entry.Value.Kind}.");
    }

    private static float ReadFiniteSingle(ref BedrockReader reader, string kind)
    {
        float value = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(value))
            throw new BedrockCodecException($"The {kind} is non-finite.");
        return value;
    }

    private static int ReadCount(ref BedrockReader reader, string kind, int cap, BedrockCodecLimits limits)
    {
        uint count = reader.ReadUInt32VarInt();
        if (count > cap || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The {kind} count {count} exceeds its element limit.");
        return checked((int)count);
    }

    private static void EnsureEnd(BedrockReader reader, string kind)
    {
        if (!reader.End)
            throw new BedrockCodecException($"The {kind} has {reader.Remaining} unread bytes.");
    }
}
