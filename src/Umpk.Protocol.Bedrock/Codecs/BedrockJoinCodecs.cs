using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

internal sealed class ResourcePacksInfoPacket : IEquatable<ResourcePacksInfoPacket>
{
    internal ResourcePacksInfoPacket(
        bool mustAccept,
        bool hasAddons,
        bool hasScripts,
        bool disableVibrantVisuals,
        Guid worldTemplateId,
        string worldTemplateVersion,
        IReadOnlyList<BedrockTexturePackInfo> packs)
    {
        ArgumentNullException.ThrowIfNull(worldTemplateVersion);
        ArgumentNullException.ThrowIfNull(packs);
        MustAccept = mustAccept;
        HasAddons = hasAddons;
        HasScripts = hasScripts;
        DisableVibrantVisuals = disableVibrantVisuals;
        WorldTemplateId = worldTemplateId;
        WorldTemplateVersion = worldTemplateVersion;
        Packs = [.. packs];
    }

    internal bool MustAccept { get; }

    internal bool HasAddons { get; }

    internal bool HasScripts { get; }

    internal bool DisableVibrantVisuals { get; }

    internal Guid WorldTemplateId { get; }

    internal string WorldTemplateVersion { get; }

    internal IReadOnlyList<BedrockTexturePackInfo> Packs { get; }

    public bool Equals(ResourcePacksInfoPacket? other) =>
        other is not null
        && MustAccept == other.MustAccept
        && HasAddons == other.HasAddons
        && HasScripts == other.HasScripts
        && DisableVibrantVisuals == other.DisableVibrantVisuals
        && WorldTemplateId == other.WorldTemplateId
        && WorldTemplateVersion == other.WorldTemplateVersion
        && Packs.SequenceEqual(other.Packs);

    public override bool Equals(object? obj) => Equals(obj as ResourcePacksInfoPacket);

    public override int GetHashCode() => HashCode.Combine(MustAccept, HasAddons, WorldTemplateId, Packs.Count);
}

internal sealed record BedrockTexturePackInfo(
    Guid PackId,
    string Version,
    ulong Size,
    string ContentKey,
    string SubPackName,
    string ContentIdentity,
    bool HasScripts,
    bool IsAddonPack,
    bool RtxEnabled,
    string CdnUrl);

internal sealed class ResourcePackStackPacket : IEquatable<ResourcePackStackPacket>
{
    internal ResourcePackStackPacket(
        bool mustAccept,
        IReadOnlyList<BedrockResourcePackIdVersion> packs,
        string gameVersion,
        IReadOnlyList<BedrockExperiment> experiments,
        bool experimentsPreviouslyUsed,
        bool hasEditorPacks)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(gameVersion);
        ArgumentNullException.ThrowIfNull(experiments);
        MustAccept = mustAccept;
        Packs = [.. packs];
        GameVersion = gameVersion;
        Experiments = [.. experiments];
        ExperimentsPreviouslyUsed = experimentsPreviouslyUsed;
        HasEditorPacks = hasEditorPacks;
    }

    internal bool MustAccept { get; }

    internal IReadOnlyList<BedrockResourcePackIdVersion> Packs { get; }

    internal string GameVersion { get; }

    internal IReadOnlyList<BedrockExperiment> Experiments { get; }

    internal bool ExperimentsPreviouslyUsed { get; }

    internal bool HasEditorPacks { get; }

    public bool Equals(ResourcePackStackPacket? other) =>
        other is not null
        && MustAccept == other.MustAccept
        && Packs.SequenceEqual(other.Packs)
        && GameVersion == other.GameVersion
        && Experiments.SequenceEqual(other.Experiments)
        && ExperimentsPreviouslyUsed == other.ExperimentsPreviouslyUsed
        && HasEditorPacks == other.HasEditorPacks;

    public override bool Equals(object? obj) => Equals(obj as ResourcePackStackPacket);

    public override int GetHashCode() => HashCode.Combine(MustAccept, GameVersion, Packs.Count, Experiments.Count);
}

internal sealed record BedrockResourcePackIdVersion(string PackId, string Version, string Name);

internal sealed record BedrockExperiment(string Name, bool Enabled);

internal enum ResourcePackResponseStatus : uint
{
    Refused = 0,
    SendPacks = 1,
    HaveAllPacks = 2,
    Completed = 3,
}

internal enum BedrockViolationType
{
    Malformed = 0,
}

internal enum BedrockViolationSeverity
{
    Warning = 0,
    FinalWarning = 1,
    Terminating = 2,
}

internal sealed record BedrockPacketViolationWarningPacket(
    BedrockViolationType ViolationType,
    BedrockViolationSeverity Severity,
    int PacketId,
    string Reason);

internal sealed class ResourcePackClientResponsePacket : IEquatable<ResourcePackClientResponsePacket>
{
    internal ResourcePackClientResponsePacket(ResourcePackResponseStatus status, IReadOnlyList<string> packIds)
    {
        ArgumentNullException.ThrowIfNull(packIds);
        Status = status;
        PackIds = [.. packIds];
    }

    internal ResourcePackResponseStatus Status { get; }

    internal IReadOnlyList<string> PackIds { get; }

    public bool Equals(ResourcePackClientResponsePacket? other) =>
        other is not null && Status == other.Status && PackIds.SequenceEqual(other.PackIds);

    public override bool Equals(object? obj) => Equals(obj as ResourcePackClientResponsePacket);

    public override int GetHashCode() => HashCode.Combine(Status, PackIds.Count);
}

internal sealed class StartGamePacket : IEquatable<StartGamePacket>
{
    internal StartGamePacket(long entityId, ulong runtimeEntityId, byte[] remainder)
    {
        ArgumentNullException.ThrowIfNull(remainder);
        EntityId = entityId;
        RuntimeEntityId = runtimeEntityId;
        Remainder = (byte[])remainder.Clone();
    }

    internal long EntityId { get; }

    internal ulong RuntimeEntityId { get; }

    internal byte[] Remainder { get; }

    public bool Equals(StartGamePacket? other) =>
        other is not null
        && EntityId == other.EntityId
        && RuntimeEntityId == other.RuntimeEntityId
        && Remainder.AsSpan().SequenceEqual(other.Remainder);

    public override bool Equals(object? obj) => Equals(obj as StartGamePacket);

    public override int GetHashCode() => HashCode.Combine(EntityId, RuntimeEntityId, Remainder.Length);
}

internal sealed class BiomeDefinitionListPacket : IEquatable<BiomeDefinitionListPacket>
{
    internal BiomeDefinitionListPacket(uint definitionCount, byte[] remainder)
    {
        ArgumentNullException.ThrowIfNull(remainder);
        DefinitionCount = definitionCount;
        Remainder = (byte[])remainder.Clone();
    }

    internal uint DefinitionCount { get; }

    internal byte[] Remainder { get; }

    public bool Equals(BiomeDefinitionListPacket? other) =>
        other is not null
        && DefinitionCount == other.DefinitionCount
        && Remainder.AsSpan().SequenceEqual(other.Remainder);

    public override bool Equals(object? obj) => Equals(obj as BiomeDefinitionListPacket);

    public override int GetHashCode() => HashCode.Combine(DefinitionCount, Remainder.Length);
}

internal sealed class AvailableEntityIdentifiersPacket(NbtTag identifiers)
{
    internal NbtTag Identifiers { get; } = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
}

internal static class BedrockJoinCodecs
{
    internal static byte[] Encode(ResourcePacksInfoPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteBoolean(packet.MustAccept);
        writer.WriteBoolean(packet.HasAddons);
        writer.WriteBoolean(packet.HasScripts);
        writer.WriteBoolean(packet.DisableVibrantVisuals);
        writer.WriteUuid(packet.WorldTemplateId);
        writer.WriteString(packet.WorldTemplateVersion);
        WriteCount(writer, packet.Packs.Count, "texture packs");
        foreach (BedrockTexturePackInfo pack in packet.Packs)
        {
            writer.WriteUuid(pack.PackId);
            writer.WriteString(pack.Version);
            writer.WriteUInt64LittleEndian(pack.Size);
            writer.WriteString(pack.ContentKey);
            writer.WriteString(pack.SubPackName);
            writer.WriteString(pack.ContentIdentity);
            writer.WriteBoolean(pack.HasScripts);
            writer.WriteBoolean(pack.IsAddonPack);
            writer.WriteBoolean(pack.RtxEnabled);
            writer.WriteString(pack.CdnUrl);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static ResourcePacksInfoPacket DecodeResourcePacksInfo(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        var packet = new ResourcePacksInfoPacket(
            reader.ReadBoolean(),
            reader.ReadBoolean(),
            reader.ReadBoolean(),
            reader.ReadBoolean(),
            reader.ReadUuid(),
            reader.ReadString(),
            ReadTexturePacks(ref reader, actualLimits));
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ResourcePackStackPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteBoolean(packet.MustAccept);
        WriteCount(writer, packet.Packs.Count, "resource packs");
        foreach (BedrockResourcePackIdVersion pack in packet.Packs)
        {
            writer.WriteString(pack.PackId);
            writer.WriteString(pack.Version);
            writer.WriteString(pack.Name);
        }

        writer.WriteString(packet.GameVersion);
        if (packet.Experiments.Count > BedrockCodecLimits.Default.MaxCollectionElements)
            throw new BedrockCodecException("The experiment count exceeds its element limit.");
        writer.WriteInt32LittleEndian(packet.Experiments.Count);
        foreach (BedrockExperiment experiment in packet.Experiments)
        {
            writer.WriteString(experiment.Name);
            writer.WriteBoolean(experiment.Enabled);
        }

        writer.WriteBoolean(packet.ExperimentsPreviouslyUsed);
        writer.WriteBoolean(packet.HasEditorPacks);
        return output.WrittenSpan.ToArray();
    }

    internal static ResourcePackStackPacket DecodeResourcePackStack(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        bool mustAccept = reader.ReadBoolean();
        var packs = new List<BedrockResourcePackIdVersion>();
        int packCount = ReadCount(ref reader, "resource packs", actualLimits);
        for (int index = 0; index < packCount; index++)
            packs.Add(new BedrockResourcePackIdVersion(reader.ReadString(), reader.ReadString(), reader.ReadString()));

        string gameVersion = reader.ReadString();
        int experimentCount = reader.ReadInt32LittleEndian();
        if (experimentCount < 0 || experimentCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"Invalid experiment count {experimentCount}.");

        var experiments = new List<BedrockExperiment>();
        for (int index = 0; index < experimentCount; index++)
            experiments.Add(new BedrockExperiment(reader.ReadString(), reader.ReadBoolean()));

        var packet = new ResourcePackStackPacket(
            mustAccept,
            packs,
            gameVersion,
            experiments,
            reader.ReadBoolean(),
            reader.ReadBoolean());
        EnsureEnd(reader);
        return packet;
    }

    internal static byte[] Encode(ResourcePackClientResponsePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!Enum.IsDefined(packet.Status))
            throw new BedrockCodecException($"Unknown resource response status {(uint)packet.Status}.");
        if (packet.Status != ResourcePackResponseStatus.SendPacks && packet.PackIds.Count != 0)
            throw new BedrockCodecException("Pack IDs are only valid with the send_packs status.");

        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt((uint)packet.Status);
        writer.WriteString(StatusName(packet.Status));
        if (packet.Status == ResourcePackResponseStatus.SendPacks)
        {
            WriteCount(writer, packet.PackIds.Count, "pack IDs");
            foreach (string packId in packet.PackIds)
                writer.WriteString(packId);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static ResourcePackClientResponsePacket DecodeResourcePackClientResponse(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        uint statusValue = reader.ReadUInt32VarInt();
        if (statusValue > (uint)ResourcePackResponseStatus.Completed)
            throw new BedrockCodecException($"Unknown resource response status {statusValue}.");

        var status = (ResourcePackResponseStatus)statusValue;
        string name = reader.ReadString();
        if (!string.Equals(name, StatusName(status), StringComparison.Ordinal))
            throw new BedrockCodecException($"The resource response name '{name}' does not match status {statusValue}.");

        List<string> packIds = [];
        if (status == ResourcePackResponseStatus.SendPacks)
        {
            int count = ReadCount(ref reader, "pack IDs", limits ?? BedrockCodecLimits.Default);
            for (int index = 0; index < count; index++)
                packIds.Add(reader.ReadString());
        }

        EnsureEnd(reader);
        return new ResourcePackClientResponsePacket(status, packIds);
    }

    internal static byte[] Encode(StartGamePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Remainder.Length > BedrockCodecLimits.Default.MaxByteArrayBytes)
            throw new BedrockCodecException("The StartGame remainder exceeds its byte limit.");

        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag64(packet.EntityId);
        writer.WriteUInt64VarInt(packet.RuntimeEntityId);
        writer.WriteBytes(packet.Remainder);
        return output.WrittenSpan.ToArray();
    }

    internal static StartGamePacket DecodeStartGame(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        long entityId = reader.ReadZigZag64();
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        byte[] remainder = ReadRemainder(ref reader, "StartGame", actualLimits);
        return new StartGamePacket(entityId, runtimeEntityId, remainder);
    }

    internal static byte[] Encode(BiomeDefinitionListPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Remainder.Length > BedrockCodecLimits.Default.MaxByteArrayBytes)
            throw new BedrockCodecException("The biome remainder exceeds its byte limit.");

        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteUInt32VarInt(packet.DefinitionCount);
        writer.WriteBytes(packet.Remainder);
        return output.WrittenSpan.ToArray();
    }

    internal static BiomeDefinitionListPacket DecodeBiomeDefinitionList(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        uint count = reader.ReadUInt32VarInt();
        if (count > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The biome definition count {count} exceeds its element limit.");

        return new BiomeDefinitionListPacket(count, ReadRemainder(ref reader, "biome definitions", actualLimits));
    }

    internal static byte[] Encode(AvailableEntityIdentifiersPacket packet, BedrockCodecLimits? limits = null) =>
        BedrockNbtCodec.Encode(packet?.Identifiers ?? throw new ArgumentNullException(nameof(packet)), BedrockNbtWireFormat.NetworkLittleVarInt);
    internal static AvailableEntityIdentifiersPacket DecodeAvailableEntityIdentifiers(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null) =>
        new(BedrockNbtCodec.Decode(payload, BedrockNbtWireFormat.NetworkLittleVarInt, limits ?? BedrockCodecLimits.Default));

    internal static byte[] Encode(BedrockPacketViolationWarningPacket packet) => Encode(writer =>
    {
        ArgumentNullException.ThrowIfNull(packet);
        writer.WriteZigZag32((int)packet.ViolationType);
        writer.WriteZigZag32((int)packet.Severity);
        writer.WriteZigZag32(packet.PacketId);
        writer.WriteString(packet.Reason);
    });

    internal static BedrockPacketViolationWarningPacket DecodePacketViolationWarning(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        var packet = new BedrockPacketViolationWarningPacket(
            (BedrockViolationType)reader.ReadZigZag32(),
            (BedrockViolationSeverity)reader.ReadZigZag32(),
            reader.ReadZigZag32(),
            reader.ReadString());
        EnsureEnd(reader);
        return packet;
    }

    internal static IReadOnlyList<string> ReadEntityIdentifiers(AvailableEntityIdentifiersPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Identifiers is not NbtCompound root || root.GetList("idlist") is not NbtList list)
            throw new BedrockCodecException("The entity identifier payload does not contain an identifier list.");

        var identifiers = new List<string>(list.Count);
        foreach (NbtTag entry in list)
        {
            if (entry is not NbtCompound compound || string.IsNullOrEmpty(compound.GetString("id")))
                throw new BedrockCodecException("An entity identifier entry does not contain an ID string.");
            identifiers.Add(compound.GetString("id"));
        }

        return identifiers;
    }

    internal static string StatusName(ResourcePackResponseStatus status) => status switch
    {
        ResourcePackResponseStatus.Refused => "cancel",
        ResourcePackResponseStatus.SendPacks => "downloading",
        ResourcePackResponseStatus.HaveAllPacks => "downloadingfinished",
        ResourcePackResponseStatus.Completed => "resourcepackstackfinished",
        _ => throw new BedrockCodecException($"Unknown resource response status {(uint)status}."),
    };

    private static void WriteCount(BedrockWriter writer, int count, string kind)
    {
        if (count < 0 || count > BedrockCodecLimits.Default.MaxCollectionElements)
            throw new BedrockCodecException($"The {kind} count {count} exceeds its element limit.");
        writer.WriteUInt32VarInt(checked((uint)count));
    }

    private static int ReadCount(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        uint count = reader.ReadUInt32VarInt();
        if (count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The {kind} count {count} exceeds its element limit.");
        return checked((int)count);
    }

    private static byte[] ReadRemainder(ref BedrockReader reader, string kind, BedrockCodecLimits limits)
    {
        if (reader.Remaining > limits.MaxByteArrayBytes)
            throw new BedrockCodecException($"The {kind} remainder exceeds its byte limit.");
        return reader.ReadExact(reader.Remaining).ToArray();
    }

    private static List<BedrockTexturePackInfo> ReadTexturePacks(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int count = ReadCount(ref reader, "texture packs", limits);
        var packs = new List<BedrockTexturePackInfo>(count);
        for (int index = 0; index < count; index++)
        {
            packs.Add(new BedrockTexturePackInfo(
                reader.ReadUuid(),
                reader.ReadString(),
                reader.ReadUInt64LittleEndian(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadString()));
        }

        return packs;
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
