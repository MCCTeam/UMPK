using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockPacketHeader
{
    internal BedrockPacketHeader(int packetId, BedrockSubclientId senderSubclient, BedrockSubclientId targetSubclient)
    {
        if (packetId is < 0 or > 1023)
            throw new ArgumentOutOfRangeException(nameof(packetId));

        PacketId = packetId;
        SenderSubclient = senderSubclient;
        TargetSubclient = targetSubclient;
    }

    internal int PacketId { get; }

    internal BedrockSubclientId SenderSubclient { get; }

    internal BedrockSubclientId TargetSubclient { get; }
}

internal sealed record BedrockBatchMember
{
    internal BedrockBatchMember(BedrockPacketHeader header, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Header = header;
        Payload = payload;
    }

    internal BedrockPacketHeader Header { get; }

    internal byte[] Payload { get; }
}

internal static class BedrockBatchCodec
{
    internal static byte[] EncodeMember(BedrockPacketHeader header, ReadOnlySpan<byte> payload)
    {
        var body = new ArrayBufferWriter<byte>();
        var bodyWriter = new BedrockWriter(body);
        uint headerValue = checked((uint)(header.PacketId
            | (header.SenderSubclient.Value << 10)
            | (header.TargetSubclient.Value << 12)));
        bodyWriter.WriteUInt32VarInt(headerValue);
        bodyWriter.WriteBytes(payload);

        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(checked((uint)body.WrittenCount));
        writer.WriteBytes(body.WrittenSpan);
        return output.WrittenSpan.ToArray();
    }

    internal static byte[] EncodeMembers(IReadOnlyList<BedrockBatchMember> members, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (members.Count == 0 || members.Count > limits.MaxBatchMembers)
            throw new BedrockCodecException($"Invalid batch member count {members.Count}.");

        var output = new ArrayBufferWriter<byte>();
        foreach (BedrockBatchMember member in members)
        {
            byte[] encoded = EncodeMember(member.Header, member.Payload);
            if (output.WrittenCount > limits.MaxBatchBytes - encoded.Length)
                throw new BedrockCodecException("The encoded batch exceeds its byte limit.");

            output.Write(encoded);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static IReadOnlyList<BedrockBatchMember> DecodeMembers(ReadOnlySpan<byte> batch, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (batch.Length == 0 || batch.Length > limits.MaxBatchBytes)
            throw new BedrockCodecException($"Invalid batch length {batch.Length}.");

        var reader = new BedrockReader(batch, limits);
        var members = new List<BedrockBatchMember>();
        while (!reader.End)
        {
            if (members.Count >= limits.MaxBatchMembers)
                throw new BedrockCodecException("The batch exceeds its member limit.");

            uint memberLengthValue = reader.ReadUInt32VarInt();
            if (memberLengthValue == 0 || memberLengthValue > limits.MaxBatchBytes || memberLengthValue > reader.Remaining)
                throw new BedrockCodecException($"Invalid batch member length {memberLengthValue}.");

            var memberReader = new BedrockReader(reader.ReadExact(checked((int)memberLengthValue)), limits);
            uint headerValue = memberReader.ReadUInt32VarInt();
            if (headerValue > 0x3fff)
                throw new BedrockCodecException($"Packet header 0x{headerValue:x} contains unsupported high bits.");

            int packetId = (int)(headerValue & 0x3ff);
            int sender = (int)((headerValue >> 10) & 0x3);
            int target = (int)((headerValue >> 12) & 0x3);
            if (sender != 0 || target != 0)
                throw new BedrockCodecException($"Subclient routing {sender}->{target} is not supported.");

            byte[] payload = memberReader.ReadExact(memberReader.Remaining).ToArray();
            members.Add(new BedrockBatchMember(
                new BedrockPacketHeader(packetId, new BedrockSubclientId(sender), new BedrockSubclientId(target)),
                payload));
        }

        return members;
    }
}
