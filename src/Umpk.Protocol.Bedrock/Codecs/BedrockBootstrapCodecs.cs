using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal sealed record RequestNetworkSettingsPacket(int ClientProtocol);

internal enum PlayStatusCode
{
    LoginSuccess = 0,
    FailedClient = 1,
    FailedSpawn = 2,
    PlayerSpawn = 3,
    FailedInvalidTenant = 4,
    FailedVanillaEducation = 5,
    FailedEducationVanilla = 6,
    FailedServerFull = 7,
    FailedEditorVanillaMismatch = 8,
    FailedVanillaEditorMismatch = 9,
}

internal sealed record PlayStatusPacket(PlayStatusCode Status);

internal sealed record NetworkSettingsPacket(
    ushort CompressionThreshold,
    BedrockCompressionAlgorithm CompressionAlgorithm,
    bool ClientThrottle,
    byte ClientThrottleThreshold,
    float ClientThrottleScalar);

internal sealed record LoginPacket(int ProtocolVersion, string IdentityToken, string ClientToken);

internal sealed record DisconnectPacket(int Reason, bool HideDisconnectReason, string? Message, string? FilteredMessage);

internal sealed record ServerToClientHandshakePacket(string Token);

internal sealed record ClientToServerHandshakePacket;

internal sealed record RequestChunkRadiusPacket(int Radius, byte MaximumRadius);

internal sealed record ChunkRadiusUpdatePacket(int Radius);

internal sealed record SetLocalPlayerAsInitializedPacket(ulong RuntimeEntityId);

internal sealed record ClientCacheStatusPacket(bool Enabled);

internal static class BedrockBootstrapCodecs
{
    internal static byte[] Encode(RequestNetworkSettingsPacket packet) => Encode(writer => writer.WriteInt32BigEndian(packet.ClientProtocol));

    internal static RequestNetworkSettingsPacket DecodeRequestNetworkSettings(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new RequestNetworkSettingsPacket(reader.ReadInt32BigEndian());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(PlayStatusPacket packet) => Encode(writer => writer.WriteInt32BigEndian((int)packet.Status));

    internal static PlayStatusPacket DecodePlayStatus(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new PlayStatusPacket((PlayStatusCode)reader.ReadInt32BigEndian());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(NetworkSettingsPacket packet) => Encode(writer =>
    {
        writer.WriteUInt16LittleEndian(packet.CompressionThreshold);
        writer.WriteUInt16LittleEndian((ushort)packet.CompressionAlgorithm);
        writer.WriteBoolean(packet.ClientThrottle);
        writer.WriteByte(packet.ClientThrottleThreshold);
        writer.WriteSingleLittleEndian(packet.ClientThrottleScalar);
    });

    internal static NetworkSettingsPacket DecodeNetworkSettings(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        ushort threshold = reader.ReadUInt16LittleEndian();
        ushort algorithmValue = reader.ReadUInt16LittleEndian();
        if (algorithmValue > (ushort)BedrockCompressionAlgorithm.Snappy)
            throw new BedrockCodecException($"Unknown compression algorithm {algorithmValue}.");

        var packet = new NetworkSettingsPacket(
            threshold,
            (BedrockCompressionAlgorithm)algorithmValue,
            reader.ReadBoolean(),
            reader.ReadByte(),
            reader.ReadSingleLittleEndian());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(LoginPacket packet) => Encode(writer =>
    {
        writer.WriteInt32BigEndian(packet.ProtocolVersion);
        var tokens = new ArrayBufferWriter<byte>();
        var tokenWriter = new BedrockWriter(tokens);
        tokenWriter.WriteLittleString(packet.IdentityToken);
        tokenWriter.WriteLittleString(packet.ClientToken);
        writer.WriteByteArray(tokens.WrittenSpan);
    });

    internal static LoginPacket DecodeLogin(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        int protocolVersion = reader.ReadInt32BigEndian();
        byte[] tokens = reader.ReadByteArray();
        EnsureEnd(reader);
        var tokenReader = new BedrockReader(tokens);
        var packet = new LoginPacket(protocolVersion, tokenReader.ReadLittleString(), tokenReader.ReadLittleString());
        EnsureEnd(tokenReader);
        return packet;
    }

    internal static byte[] Encode(DisconnectPacket packet) => Encode(writer =>
    {
        writer.WriteZigZag32(packet.Reason);
        writer.WriteBoolean(packet.HideDisconnectReason);
        if (packet.HideDisconnectReason)
        {
            if (packet.Message is not null || packet.FilteredMessage is not null)
                throw new BedrockCodecException("A hidden disconnect reason cannot carry messages.");

            return;
        }

        writer.WriteString(packet.Message ?? throw new BedrockCodecException("A visible disconnect reason requires a message."));
        writer.WriteString(packet.FilteredMessage ?? throw new BedrockCodecException("A visible disconnect reason requires a filtered message."));
    });

    internal static DisconnectPacket DecodeDisconnect(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        int reason = reader.ReadZigZag32();
        bool hide = reader.ReadBoolean();
        var packet = hide
            ? new DisconnectPacket(reason, true, null, null)
            : new DisconnectPacket(reason, false, reader.ReadString(), reader.ReadString());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ServerToClientHandshakePacket packet) => Encode(writer => writer.WriteString(packet.Token));

    internal static ServerToClientHandshakePacket DecodeServerToClientHandshake(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new ServerToClientHandshakePacket(reader.ReadString());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ClientToServerHandshakePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return [];
    }

    internal static ClientToServerHandshakePacket DecodeClientToServerHandshake(ReadOnlySpan<byte> payload)
    {
        if (!payload.IsEmpty)
            throw new BedrockCodecException("The client handshake packet must have an empty payload.");

        return new ClientToServerHandshakePacket();
    }

    internal static byte[] Encode(RequestChunkRadiusPacket packet) => Encode(writer =>
    {
        writer.WriteZigZag32(packet.Radius);
        writer.WriteByte(packet.MaximumRadius);
    });

    internal static RequestChunkRadiusPacket DecodeRequestChunkRadius(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new RequestChunkRadiusPacket(reader.ReadZigZag32(), reader.ReadByte());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ChunkRadiusUpdatePacket packet) => Encode(writer => writer.WriteZigZag32(packet.Radius));

    internal static ChunkRadiusUpdatePacket DecodeChunkRadiusUpdate(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new ChunkRadiusUpdatePacket(reader.ReadZigZag32());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(SetLocalPlayerAsInitializedPacket packet) => Encode(writer => writer.WriteUInt64VarInt(packet.RuntimeEntityId));

    internal static SetLocalPlayerAsInitializedPacket DecodeSetLocalPlayerAsInitialized(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new SetLocalPlayerAsInitializedPacket(reader.ReadUInt64VarInt());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ClientCacheStatusPacket packet) => Encode(writer => writer.WriteBoolean(packet.Enabled));

    internal static ClientCacheStatusPacket DecodeClientCacheStatus(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var packet = new ClientCacheStatusPacket(reader.ReadBoolean());
        EnsureEnd(reader);
        return packet;
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
            throw new BedrockCodecException($"The packet payload has {reader.Remaining} unread bytes.");
    }
}
