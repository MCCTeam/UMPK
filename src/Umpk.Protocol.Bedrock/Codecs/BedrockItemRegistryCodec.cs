using System.Buffers;
using System.Collections.Immutable;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

internal sealed record BedrockItemState(
    string Name,
    short RuntimeId,
    bool ComponentBased,
    int Version,
    NbtTag Components);

internal sealed record BedrockItemRegistryPacket(IReadOnlyList<BedrockItemState> Items);

internal static class BedrockItemRegistryCodec
{
    internal static byte[] Encode(BedrockItemRegistryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(packet.Items);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)packet.Items.Count));
        foreach (BedrockItemState item in packet.Items)
        {
            writer.WriteString(item.Name);
            writer.WriteInt16LittleEndian(item.RuntimeId);
            writer.WriteBoolean(item.ComponentBased);
            writer.WriteZigZag32(item.Version);
            writer.WriteBytes(BedrockNbtCodec.Encode(item.Components, BedrockNbtWireFormat.NetworkLittleVarInt));
        }

        return output.WrittenSpan.ToArray();
    }

    internal static BedrockItemRegistryPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        uint count = reader.ReadUInt32VarInt();
        if (count > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException("The item registry exceeds its element limit.");

        var items = new BedrockItemState[count];
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = new BedrockItemState(
                reader.ReadString(),
                reader.ReadInt16LittleEndian(),
                reader.ReadBoolean(),
                reader.ReadZigZag32(),
                BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, actualLimits));
        }

        if (!reader.End)
            throw new BedrockCodecException("The item registry has unread bytes.");

        return new BedrockItemRegistryPacket(items);
    }

    internal static BedrockRegistrySnapshot Publish(BedrockItemRegistryPacket packet, BedrockRegistrySnapshot current)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(current);
        var items = ImmutableDictionary.CreateBuilder<short, string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (BedrockItemState item in packet.Items)
        {
            if (!items.TryAdd(item.RuntimeId, item.Name))
                throw new BedrockCodecException($"Duplicate item runtime ID {item.RuntimeId}.");
            if (!names.Add(item.Name))
                throw new BedrockCodecException($"Duplicate item name {item.Name}.");
        }

        return current with { Generation = checked(current.Generation + 1), Items = items.ToImmutable() };
    }
}
