using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockVector3(float X, float Y, float Z);

internal enum BedrockMovePlayerMode : byte
{
    Normal = 0,
    Reset = 1,
    Teleport = 2,
    Rotation = 3,
}

internal enum BedrockTeleportCause : int
{
    Unknown = 0,
    Projectile = 1,
    ChorusFruit = 2,
    Command = 3,
    Behavior = 4,
}

internal sealed class BedrockAddEntityPacket : IEquatable<BedrockAddEntityPacket>
{
    internal BedrockAddEntityPacket(
        long uniqueEntityId,
        ulong runtimeEntityId,
        string entityType,
        BedrockVector3 position,
        BedrockVector3 velocity,
        float pitch,
        float yaw,
        float headYaw,
        float bodyYaw,
        ImmutableArray<BedrockEntityAttributeValue> attributes,
        ImmutableArray<BedrockMetadataEntry> metadata,
        BedrockEntityProperties properties,
        ImmutableArray<BedrockEntityLink> links)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);
        ArgumentNullException.ThrowIfNull(properties);
        UniqueEntityId = uniqueEntityId;
        RuntimeEntityId = runtimeEntityId;
        EntityType = entityType;
        Position = position;
        Velocity = velocity;
        Pitch = pitch;
        Yaw = yaw;
        HeadYaw = headYaw;
        BodyYaw = bodyYaw;
        Attributes = attributes;
        Metadata = metadata;
        Properties = properties;
        Links = links;
    }

    internal long UniqueEntityId { get; }

    internal ulong RuntimeEntityId { get; }

    internal string EntityType { get; }

    internal BedrockVector3 Position { get; }

    internal BedrockVector3 Velocity { get; }

    internal float Pitch { get; }

    internal float Yaw { get; }

    internal float HeadYaw { get; }

    internal float BodyYaw { get; }

    internal ImmutableArray<BedrockEntityAttributeValue> Attributes { get; }

    internal ImmutableArray<BedrockMetadataEntry> Metadata { get; }

    internal BedrockEntityProperties Properties { get; }

    internal ImmutableArray<BedrockEntityLink> Links { get; }

    public bool Equals(BedrockAddEntityPacket? other) =>
        other is not null
        && UniqueEntityId == other.UniqueEntityId
        && RuntimeEntityId == other.RuntimeEntityId
        && EntityType == other.EntityType
        && Position.Equals(other.Position)
        && Velocity.Equals(other.Velocity)
        && Pitch.Equals(other.Pitch)
        && Yaw.Equals(other.Yaw)
        && HeadYaw.Equals(other.HeadYaw)
        && BodyYaw.Equals(other.BodyYaw)
        && Attributes.SequenceEqual(other.Attributes)
        && Metadata.SequenceEqual(other.Metadata)
        && Properties.Equals(other.Properties)
        && Links.SequenceEqual(other.Links);

    public override bool Equals(object? obj) => Equals(obj as BedrockAddEntityPacket);

    public override int GetHashCode() => HashCode.Combine(UniqueEntityId, RuntimeEntityId, EntityType);
}

internal sealed class BedrockAddPlayerPacket : IEquatable<BedrockAddPlayerPacket>
{
    internal BedrockAddPlayerPacket(
        Guid uuid,
        string username,
        ulong runtimeEntityId,
        string platformChatId,
        BedrockVector3 position,
        BedrockVector3 velocity,
        float pitch,
        float yaw,
        float headYaw,
        BedrockItemInstance heldItem,
        int gameType,
        ImmutableArray<BedrockMetadataEntry> metadata,
        BedrockEntityProperties properties,
        long uniqueEntityId,
        byte permissionLevel,
        byte commandPermission,
        ImmutableArray<BedrockAbilityLayer> abilityLayers,
        ImmutableArray<BedrockEntityLink> links,
        string deviceId,
        int? deviceOs)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentNullException.ThrowIfNull(platformChatId);
        ArgumentNullException.ThrowIfNull(heldItem);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(deviceId);
        Uuid = uuid;
        Username = username;
        RuntimeEntityId = runtimeEntityId;
        PlatformChatId = platformChatId;
        Position = position;
        Velocity = velocity;
        Pitch = pitch;
        Yaw = yaw;
        HeadYaw = headYaw;
        HeldItem = heldItem;
        GameType = gameType;
        Metadata = metadata;
        Properties = properties;
        UniqueEntityId = uniqueEntityId;
        PermissionLevel = permissionLevel;
        CommandPermission = commandPermission;
        AbilityLayers = abilityLayers;
        Links = links;
        DeviceId = deviceId;
        DeviceOs = deviceOs;
    }

    internal Guid Uuid { get; }

    internal string Username { get; }

    internal ulong RuntimeEntityId { get; }

    internal string PlatformChatId { get; }

    internal BedrockVector3 Position { get; }

    internal BedrockVector3 Velocity { get; }

    internal float Pitch { get; }

    internal float Yaw { get; }

    internal float HeadYaw { get; }

    internal BedrockItemInstance HeldItem { get; }

    internal int GameType { get; }

    internal ImmutableArray<BedrockMetadataEntry> Metadata { get; }

    internal BedrockEntityProperties Properties { get; }

    internal long UniqueEntityId { get; }

    internal byte PermissionLevel { get; }

    internal byte CommandPermission { get; }

    internal ImmutableArray<BedrockAbilityLayer> AbilityLayers { get; }

    internal ImmutableArray<BedrockEntityLink> Links { get; }

    internal string DeviceId { get; }

    internal int? DeviceOs { get; }

    public bool Equals(BedrockAddPlayerPacket? other) =>
        other is not null
        && Uuid == other.Uuid
        && Username == other.Username
        && RuntimeEntityId == other.RuntimeEntityId
        && PlatformChatId == other.PlatformChatId
        && Position.Equals(other.Position)
        && Velocity.Equals(other.Velocity)
        && Pitch.Equals(other.Pitch)
        && Yaw.Equals(other.Yaw)
        && HeadYaw.Equals(other.HeadYaw)
        && HeldItem.Equals(other.HeldItem)
        && GameType == other.GameType
        && Metadata.SequenceEqual(other.Metadata)
        && Properties.Equals(other.Properties)
        && UniqueEntityId == other.UniqueEntityId
        && PermissionLevel == other.PermissionLevel
        && CommandPermission == other.CommandPermission
        && AbilityLayers.SequenceEqual(other.AbilityLayers)
        && Links.SequenceEqual(other.Links)
        && DeviceId == other.DeviceId
        && DeviceOs == other.DeviceOs;

    public override bool Equals(object? obj) => Equals(obj as BedrockAddPlayerPacket);

    public override int GetHashCode() => HashCode.Combine(Uuid, RuntimeEntityId, UniqueEntityId);
}

internal sealed record BedrockRemoveEntityPacket(long UniqueEntityId);

/// <summary>Represents a dropped item spawn with the item stack, position, velocity, metadata, and fishing flag carried by protocol 2169.</summary>
internal sealed class BedrockAddItemEntityPacket : IEquatable<BedrockAddItemEntityPacket>
{
    internal BedrockAddItemEntityPacket(
        long uniqueEntityId,
        ulong runtimeEntityId,
        BedrockItemInstance item,
        BedrockVector3 position,
        BedrockVector3 velocity,
        ImmutableArray<BedrockMetadataEntry> metadata,
        bool isFromFishing)
    {
        ArgumentNullException.ThrowIfNull(item);
        UniqueEntityId = uniqueEntityId;
        RuntimeEntityId = runtimeEntityId;
        Item = item;
        Position = position;
        Velocity = velocity;
        Metadata = metadata;
        IsFromFishing = isFromFishing;
    }

    internal long UniqueEntityId { get; }

    internal ulong RuntimeEntityId { get; }

    internal BedrockItemInstance Item { get; }

    internal BedrockVector3 Position { get; }

    internal BedrockVector3 Velocity { get; }

    internal ImmutableArray<BedrockMetadataEntry> Metadata { get; }

    internal bool IsFromFishing { get; }

    public bool Equals(BedrockAddItemEntityPacket? other) =>
        other is not null
        && UniqueEntityId == other.UniqueEntityId
        && RuntimeEntityId == other.RuntimeEntityId
        && Item.Equals(other.Item)
        && Position.Equals(other.Position)
        && Velocity.Equals(other.Velocity)
        && Metadata.SequenceEqual(other.Metadata)
        && IsFromFishing == other.IsFromFishing;

    public override bool Equals(object? obj) => Equals(obj as BedrockAddItemEntityPacket);

    public override int GetHashCode() => HashCode.Combine(UniqueEntityId, RuntimeEntityId, IsFromFishing);
}

internal sealed class BedrockSetEntityDataPacket : IEquatable<BedrockSetEntityDataPacket>
{
    internal BedrockSetEntityDataPacket(
        ulong runtimeEntityId,
        ImmutableArray<BedrockMetadataEntry> metadata,
        BedrockEntityProperties properties,
        ulong tick)
    {
        ArgumentNullException.ThrowIfNull(properties);
        RuntimeEntityId = runtimeEntityId;
        Metadata = metadata;
        Properties = properties;
        Tick = tick;
    }

    internal ulong RuntimeEntityId { get; }

    internal ImmutableArray<BedrockMetadataEntry> Metadata { get; }

    internal BedrockEntityProperties Properties { get; }

    internal ulong Tick { get; }

    public bool Equals(BedrockSetEntityDataPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Metadata.SequenceEqual(other.Metadata)
        && Properties.Equals(other.Properties)
        && Tick == other.Tick;

    public override bool Equals(object? obj) => Equals(obj as BedrockSetEntityDataPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, Tick);
}

internal sealed record BedrockSetEntityMotionPacket(ulong RuntimeEntityId, BedrockVector3 Velocity, ulong Tick);

internal sealed class BedrockMovePlayerPacket : IEquatable<BedrockMovePlayerPacket>
{
    internal BedrockMovePlayerPacket(
        ulong runtimeEntityId,
        BedrockVector3 position,
        float pitch,
        float yaw,
        float headYaw,
        BedrockMovePlayerMode mode,
        bool onGround,
        ulong riddenRuntimeEntityId,
        BedrockTeleportCause? teleportCause,
        int? teleportSourceEntityType,
        ulong tick)
    {
        if (!Enum.IsDefined(mode))
            throw new BedrockCodecException($"Unknown MovePlayer mode {(byte)mode}.");
        if (teleportCause.HasValue != teleportSourceEntityType.HasValue)
            throw new ArgumentException("Teleport cause and source entity type must be set together.");
        if (teleportCause.HasValue && !Enum.IsDefined(teleportCause.Value))
            throw new BedrockCodecException($"Unknown MovePlayer teleport cause {(int)teleportCause.Value}.");
        RuntimeEntityId = runtimeEntityId;
        Position = position;
        Pitch = pitch;
        Yaw = yaw;
        HeadYaw = headYaw;
        Mode = mode;
        OnGround = onGround;
        RiddenRuntimeEntityId = riddenRuntimeEntityId;
        TeleportCause = teleportCause;
        TeleportSourceEntityType = teleportSourceEntityType;
        Tick = tick;
    }

    internal ulong RuntimeEntityId { get; }

    internal BedrockVector3 Position { get; }

    internal float Pitch { get; }

    internal float Yaw { get; }

    internal float HeadYaw { get; }

    internal BedrockMovePlayerMode Mode { get; }

    internal bool OnGround { get; }

    internal ulong RiddenRuntimeEntityId { get; }

    internal BedrockTeleportCause? TeleportCause { get; }

    internal int? TeleportSourceEntityType { get; }

    internal ulong Tick { get; }

    public bool Equals(BedrockMovePlayerPacket? other) =>
        other is not null
        && RuntimeEntityId == other.RuntimeEntityId
        && Position.Equals(other.Position)
        && Pitch.Equals(other.Pitch)
        && Yaw.Equals(other.Yaw)
        && HeadYaw.Equals(other.HeadYaw)
        && Mode == other.Mode
        && OnGround == other.OnGround
        && RiddenRuntimeEntityId == other.RiddenRuntimeEntityId
        && TeleportCause == other.TeleportCause
        && TeleportSourceEntityType == other.TeleportSourceEntityType
        && Tick == other.Tick;

    public override bool Equals(object? obj) => Equals(obj as BedrockMovePlayerPacket);

    public override int GetHashCode() => HashCode.Combine(RuntimeEntityId, Mode, Tick);
}

internal sealed record BedrockPlayerLocationPacket(long EntityUniqueId, bool Hide, BedrockVector3? Position, int Unused);

internal static class BedrockActorCodecs
{
    internal static byte[] Encode(BedrockAddEntityPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteZigZag64(packet.UniqueEntityId);
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            writer.WriteString(packet.EntityType);
            WriteVector3(writer, packet.Position);
            WriteVector3(writer, packet.Velocity);
            WriteRotation(writer, packet.Pitch, packet.Yaw, packet.HeadYaw, packet.BodyYaw);
            writer.WriteBytes(BedrockActorSections.EncodeEntityAttributes(packet.Attributes));
            writer.WriteBytes(BedrockActorSections.EncodeMetadataDictionary(packet.Metadata));
            writer.WriteBytes(BedrockActorSections.EncodeEntityProperties(packet.Properties));
            writer.WriteBytes(BedrockActorSections.EncodeLinks(packet.Links));
        });
    }

    internal static BedrockAddEntityPacket DecodeAddEntity(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        long uniqueEntityId = reader.ReadZigZag64();
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        string entityType = reader.ReadString();
        if (entityType.Length == 0)
            throw new BedrockCodecException("The AddEntity type must not be empty.");
        BedrockVector3 position = ReadVector3(ref reader, "AddEntity position");
        BedrockVector3 velocity = ReadVector3(ref reader, "AddEntity velocity");
        float pitch = ReadRotation(ref reader, "AddEntity pitch");
        float yaw = ReadRotation(ref reader, "AddEntity yaw");
        float headYaw = ReadRotation(ref reader, "AddEntity head yaw");
        float bodyYaw = ReadRotation(ref reader, "AddEntity body yaw");
        ImmutableArray<BedrockEntityAttributeValue> attributes = BedrockActorSections.ParseEntityAttributes(ref reader, "AddEntity attributes", actualLimits);
        ImmutableArray<BedrockMetadataEntry> metadata = BedrockActorSections.ParseMetadataDictionary(payload, ref reader, "AddEntity metadata", actualLimits);
        BedrockEntityProperties properties = BedrockActorSections.ParseEntityProperties(ref reader, "AddEntity properties", actualLimits);
        ImmutableArray<BedrockEntityLink> links = BedrockActorSections.ParseLinks(ref reader, "AddEntity links", actualLimits);
        EnsureEnd(reader);
        return new BedrockAddEntityPacket(uniqueEntityId, runtimeEntityId, entityType, position, velocity, pitch, yaw, headYaw, bodyYaw, attributes, metadata, properties, links);
    }

    internal static byte[] Encode(BedrockAddItemEntityPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteZigZag64(packet.UniqueEntityId);
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            BedrockActorSections.WriteItemInstance(writer, packet.Item);
            WriteVector3(writer, packet.Position);
            WriteVector3(writer, packet.Velocity);
            writer.WriteBytes(BedrockActorSections.EncodeMetadataDictionary(packet.Metadata));
            writer.WriteBoolean(packet.IsFromFishing);
        });
    }

    internal static BedrockAddItemEntityPacket DecodeAddItemEntity(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        long uniqueEntityId = reader.ReadZigZag64();
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        BedrockItemInstance item = BedrockActorSections.ReadItemInstance(ref reader, "AddItemEntity item", actualLimits);
        BedrockVector3 position = ReadVector3(ref reader, "AddItemEntity position");
        BedrockVector3 velocity = ReadVector3(ref reader, "AddItemEntity velocity");
        ImmutableArray<BedrockMetadataEntry> metadata = BedrockActorSections.ParseMetadataDictionary(payload, ref reader, "AddItemEntity metadata", actualLimits);
        bool isFromFishing = reader.ReadBoolean();
        EnsureEnd(reader);
        return new BedrockAddItemEntityPacket(uniqueEntityId, runtimeEntityId, item, position, velocity, metadata, isFromFishing);
    }

    internal static byte[] Encode(BedrockAddPlayerPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUuid(packet.Uuid);
            writer.WriteString(packet.Username);
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            writer.WriteString(packet.PlatformChatId);
            WriteVector3(writer, packet.Position);
            WriteVector3(writer, packet.Velocity);
            writer.WriteSingleLittleEndian(packet.Pitch);
            writer.WriteSingleLittleEndian(packet.Yaw);
            writer.WriteSingleLittleEndian(packet.HeadYaw);
            BedrockActorSections.WriteItemInstance(writer, packet.HeldItem);
            writer.WriteZigZag32(packet.GameType);
            writer.WriteBytes(BedrockActorSections.EncodeMetadataDictionary(packet.Metadata));
            writer.WriteBytes(BedrockActorSections.EncodeEntityProperties(packet.Properties));
            writer.WriteInt64LittleEndian(packet.UniqueEntityId);
            writer.WriteByte(packet.PermissionLevel);
            writer.WriteByte(packet.CommandPermission);
            writer.WriteBytes(BedrockActorSections.EncodeAbilityLayers(packet.AbilityLayers));
            writer.WriteBytes(BedrockActorSections.EncodeLinks(packet.Links));
            writer.WriteString(packet.DeviceId);
            if (packet.DeviceOs.HasValue)
                writer.WriteInt32LittleEndian(packet.DeviceOs.Value);
        });
    }

    internal static BedrockAddPlayerPacket DecodeAddPlayer(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        Guid uuid = reader.ReadUuid();
        string username = reader.ReadString();
        if (username.Length == 0)
            throw new BedrockCodecException("The AddPlayer username must not be empty.");
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        string platformChatId = reader.ReadString();
        BedrockVector3 position = ReadVector3(ref reader, "AddPlayer position");
        BedrockVector3 velocity = ReadVector3(ref reader, "AddPlayer velocity");
        float pitch = ReadRotation(ref reader, "AddPlayer pitch");
        float yaw = ReadRotation(ref reader, "AddPlayer yaw");
        float headYaw = ReadRotation(ref reader, "AddPlayer head yaw");
        BedrockItemInstance heldItem = BedrockActorSections.ReadItemInstance(ref reader, "AddPlayer held item", actualLimits);
        int gameType = reader.ReadZigZag32();
        ImmutableArray<BedrockMetadataEntry> metadata = BedrockActorSections.ParseMetadataDictionary(payload, ref reader, "AddPlayer metadata", actualLimits);
        BedrockEntityProperties properties = BedrockActorSections.ParseEntityProperties(ref reader, "AddPlayer properties", actualLimits);
        long uniqueEntityId = reader.ReadInt64LittleEndian();
        byte permissionLevel = reader.ReadByte();
        byte commandPermission = reader.ReadByte();
        ImmutableArray<BedrockAbilityLayer> abilityLayers = BedrockActorSections.ParseAbilityLayers(ref reader, "AddPlayer abilities", actualLimits);
        ImmutableArray<BedrockEntityLink> links = BedrockActorSections.ParseLinks(ref reader, "AddPlayer links", actualLimits);
        string deviceId = reader.ReadString();
        int? deviceOs = reader.End ? null : reader.ReadInt32LittleEndian();
        EnsureEnd(reader);
        return new BedrockAddPlayerPacket(uuid, username, runtimeEntityId, platformChatId, position, velocity, pitch, yaw, headYaw, heldItem, gameType, metadata, properties, uniqueEntityId, permissionLevel, commandPermission, abilityLayers, links, deviceId, deviceOs);
    }

    internal static byte[] Encode(BedrockRemoveEntityPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer => writer.WriteZigZag64(packet.UniqueEntityId));
    }

    internal static BedrockRemoveEntityPacket DecodeRemoveEntity(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        long uniqueEntityId = reader.ReadZigZag64();
        EnsureEnd(reader);
        return new BedrockRemoveEntityPacket(uniqueEntityId);
    }

    internal static byte[] Encode(BedrockSetEntityDataPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            writer.WriteBytes(BedrockActorSections.EncodeMetadataDictionary(packet.Metadata));
            writer.WriteBytes(BedrockActorSections.EncodeEntityProperties(packet.Properties));
            writer.WriteUInt64VarInt(packet.Tick);
        });
    }

    internal static BedrockSetEntityDataPacket DecodeSetEntityData(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        ImmutableArray<BedrockMetadataEntry> metadata = BedrockActorSections.ParseMetadataDictionary(payload, ref reader, "SetEntityData metadata", actualLimits);
        BedrockEntityProperties properties = BedrockActorSections.ParseEntityProperties(ref reader, "SetEntityData properties", actualLimits);
        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new BedrockSetEntityDataPacket(runtimeEntityId, metadata, properties, tick);
    }

    internal static byte[] Encode(BedrockSetEntityMotionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            WriteVector3(writer, packet.Velocity);
            writer.WriteUInt64VarInt(packet.Tick);
        });
    }

    internal static BedrockSetEntityMotionPacket DecodeSetEntityMotion(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        BedrockVector3 velocity = ReadVector3(ref reader, "SetEntityMotion velocity");
        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new BedrockSetEntityMotionPacket(runtimeEntityId, velocity, tick);
    }

    internal static byte[] Encode(BedrockMovePlayerPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt64VarInt(packet.RuntimeEntityId);
            WriteVector3(writer, packet.Position);
            writer.WriteSingleLittleEndian(packet.Pitch);
            writer.WriteSingleLittleEndian(packet.Yaw);
            writer.WriteSingleLittleEndian(packet.HeadYaw);
            writer.WriteByte((byte)packet.Mode);
            writer.WriteBoolean(packet.OnGround);
            writer.WriteUInt64VarInt(packet.RiddenRuntimeEntityId);
            writer.WriteBoolean(packet.TeleportCause.HasValue);
            if (packet.TeleportCause.HasValue)
            {
                writer.WriteInt32LittleEndian((int)packet.TeleportCause.Value);
                writer.WriteInt32LittleEndian(packet.TeleportSourceEntityType!.Value);
            }

            writer.WriteUInt64VarInt(packet.Tick);
        });
    }

    internal static BedrockMovePlayerPacket DecodeMovePlayer(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        BedrockVector3 position = ReadVector3(ref reader, "MovePlayer position");
        float pitch = ReadRotation(ref reader, "MovePlayer pitch");
        float yaw = ReadRotation(ref reader, "MovePlayer yaw");
        float headYaw = ReadRotation(ref reader, "MovePlayer head yaw");
        byte modeValue = reader.ReadByte();
        if (modeValue > (byte)BedrockMovePlayerMode.Rotation)
            throw new BedrockCodecException($"Unknown MovePlayer mode {modeValue}.");
        var mode = (BedrockMovePlayerMode)modeValue;
        bool onGround = reader.ReadBoolean();
        ulong riddenRuntimeEntityId = reader.ReadUInt64VarInt();
        BedrockTeleportCause? cause = null;
        int? sourceEntityType = null;
        if (reader.ReadBoolean())
        {
            int causeValue = reader.ReadInt32LittleEndian();
            if (!Enum.IsDefined((BedrockTeleportCause)causeValue))
                throw new BedrockCodecException($"Unknown MovePlayer teleport cause {causeValue}.");
            cause = (BedrockTeleportCause)causeValue;
            sourceEntityType = reader.ReadInt32LittleEndian();
        }

        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new BedrockMovePlayerPacket(runtimeEntityId, position, pitch, yaw, headYaw, mode, onGround, riddenRuntimeEntityId, cause, sourceEntityType, tick);
    }

    internal static byte[] Encode(BedrockPlayerLocationPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!packet.Hide && packet.Position is null)
            throw new BedrockCodecException("A coordinates PlayerLocation must carry a position.");
        if (packet.Hide && packet.Position is not null)
            throw new BedrockCodecException("A hide PlayerLocation must not carry a position.");
        return Encode(writer =>
        {
            writer.WriteZigZag64(packet.EntityUniqueId);
            writer.WriteUInt32VarInt(packet.Hide ? 1U : 0U);
            writer.WriteZigZag32(packet.Unused);
            if (packet.Position.HasValue)
                WriteVector3(writer, packet.Position.Value);
        });
    }

    internal static BedrockPlayerLocationPacket DecodePlayerLocation(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        long entityUniqueId = reader.ReadZigZag64();
        uint type = reader.ReadUInt32VarInt();
        if (type > 1U)
            throw new BedrockCodecException($"Unknown PlayerLocation type {type}.");
        int unused = reader.ReadZigZag32();
        BedrockVector3? position = type == 0U ? ReadVector3(ref reader, "PlayerLocation position") : null;
        EnsureEnd(reader);
        return new BedrockPlayerLocationPacket(entityUniqueId, type == 1U, position, unused);
    }

    private static void WriteVector3(BedrockWriter writer, BedrockVector3 value)
    {
        writer.WriteSingleLittleEndian(value.X);
        writer.WriteSingleLittleEndian(value.Y);
        writer.WriteSingleLittleEndian(value.Z);
    }

    private static BedrockVector3 ReadVector3(ref BedrockReader reader, string kind)
    {
        float x = reader.ReadSingleLittleEndian();
        float y = reader.ReadSingleLittleEndian();
        float z = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new BedrockCodecException($"The {kind} contains a non-finite component.");
        return new BedrockVector3(x, y, z);
    }

    private static void WriteRotation(BedrockWriter writer, float pitch, float yaw, float headYaw, float bodyYaw)
    {
        writer.WriteSingleLittleEndian(pitch);
        writer.WriteSingleLittleEndian(yaw);
        writer.WriteSingleLittleEndian(headYaw);
        writer.WriteSingleLittleEndian(bodyYaw);
    }

    private static float ReadRotation(ref BedrockReader reader, string kind)
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
            throw new BedrockCodecException($"The actor payload has {reader.Remaining} unread bytes.");
    }
}
