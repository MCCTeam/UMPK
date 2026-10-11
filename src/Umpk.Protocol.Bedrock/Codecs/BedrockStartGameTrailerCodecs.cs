using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

// The StartGame remainder carries world identity, gamerules, experiments, custom block properties, and the palette mode flag.
// Every field decodes in pinned order with raw values preserved, and the negotiated palette flag determines whether block identities are persistent hashes or session identifiers.
internal readonly record struct BedrockStartGamePosition(int X, int Y, int Z);

internal enum BedrockGameRuleKind : uint
{
    None = 0,
    Bool = 1,
    Int = 2,
    Float = 3,
}

internal sealed class BedrockGameRule : IEquatable<BedrockGameRule>
{
    internal BedrockGameRule(string name, bool editable, BedrockGameRuleKind kind, bool boolValue, int intValue, float floatValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Editable = editable;
        Kind = kind;
        BoolValue = boolValue;
        IntValue = intValue;
        FloatValue = floatValue;
    }

    internal string Name { get; }

    internal bool Editable { get; }

    internal BedrockGameRuleKind Kind { get; }

    internal bool BoolValue { get; }

    internal int IntValue { get; }

    internal float FloatValue { get; }

    public bool Equals(BedrockGameRule? other) =>
        other is not null
        && Name == other.Name
        && Editable == other.Editable
        && Kind == other.Kind
        && BoolValue == other.BoolValue
        && IntValue == other.IntValue
        && FloatValue.Equals(other.FloatValue);

    public override bool Equals(object? obj) => Equals(obj as BedrockGameRule);

    public override int GetHashCode() => HashCode.Combine(Name, Kind, IntValue);
}

internal sealed class BedrockExperimentEntry : IEquatable<BedrockExperimentEntry>
{
    internal BedrockExperimentEntry(string name, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Enabled = enabled;
    }

    internal string Name { get; }

    internal bool Enabled { get; }

    public bool Equals(BedrockExperimentEntry? other) =>
        other is not null && Name == other.Name && Enabled == other.Enabled;

    public override bool Equals(object? obj) => Equals(obj as BedrockExperimentEntry);

    public override int GetHashCode() => HashCode.Combine(Name, Enabled);
}

internal sealed class BedrockBlockPropertyEntry : IEquatable<BedrockBlockPropertyEntry>
{
    internal BedrockBlockPropertyEntry(string name, NbtTag state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        Name = name;
        State = state;
    }

    internal string Name { get; }

    internal NbtTag State { get; }

    public bool Equals(BedrockBlockPropertyEntry? other) => other is not null && Name == other.Name && State.Equals(other.State);

    public override bool Equals(object? obj) => Equals(obj as BedrockBlockPropertyEntry);

    public override int GetHashCode() => HashCode.Combine(Name, State);
}

internal sealed class BedrockGatheringJoinInfo : IEquatable<BedrockGatheringJoinInfo>
{
    internal BedrockGatheringJoinInfo(
        Guid experienceId,
        string experienceName,
        Guid? experienceWorldId,
        string? experienceWorldName,
        string creatorId,
        Guid? targetId,
        string? scenarioId,
        string? serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(experienceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorId);
        ExperienceId = experienceId;
        ExperienceName = experienceName;
        ExperienceWorldId = experienceWorldId;
        ExperienceWorldName = experienceWorldName;
        CreatorId = creatorId;
        TargetId = targetId;
        ScenarioId = scenarioId;
        ServerId = serverId;
    }

    internal Guid ExperienceId { get; }

    internal string ExperienceName { get; }

    internal Guid? ExperienceWorldId { get; }

    internal string? ExperienceWorldName { get; }

    internal string CreatorId { get; }

    internal Guid? TargetId { get; }

    internal string? ScenarioId { get; }

    internal string? ServerId { get; }

    public bool Equals(BedrockGatheringJoinInfo? other) =>
        other is not null
        && ExperienceId == other.ExperienceId
        && ExperienceName == other.ExperienceName
        && ExperienceWorldId == other.ExperienceWorldId
        && ExperienceWorldName == other.ExperienceWorldName
        && CreatorId == other.CreatorId
        && TargetId == other.TargetId
        && ScenarioId == other.ScenarioId
        && ServerId == other.ServerId;

    public override bool Equals(object? obj) => Equals(obj as BedrockGatheringJoinInfo);

    public override int GetHashCode() => HashCode.Combine(ExperienceId, ExperienceName, CreatorId);
}

internal sealed class BedrockStoreEntryPointInfo : IEquatable<BedrockStoreEntryPointInfo>
{
    internal BedrockStoreEntryPointInfo(string storeId, string storeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        StoreId = storeId;
        StoreName = storeName;
    }

    internal string StoreId { get; }

    internal string StoreName { get; }

    public bool Equals(BedrockStoreEntryPointInfo? other) =>
        other is not null && StoreId == other.StoreId && StoreName == other.StoreName;

    public override bool Equals(object? obj) => Equals(obj as BedrockStoreEntryPointInfo);

    public override int GetHashCode() => HashCode.Combine(StoreId, StoreName);
}

internal sealed class BedrockPresenceInfo : IEquatable<BedrockPresenceInfo>
{
    internal BedrockPresenceInfo(string? experienceName, string? worldName, string? richPresenceId)
    {
        ExperienceName = experienceName;
        WorldName = worldName;
        RichPresenceId = richPresenceId;
    }

    internal string? ExperienceName { get; }

    internal string? WorldName { get; }

    internal string? RichPresenceId { get; }

    public bool Equals(BedrockPresenceInfo? other) =>
        other is not null
        && ExperienceName == other.ExperienceName
        && WorldName == other.WorldName
        && RichPresenceId == other.RichPresenceId;

    public override bool Equals(object? obj) => Equals(obj as BedrockPresenceInfo);

    public override int GetHashCode() => HashCode.Combine(ExperienceName, WorldName, RichPresenceId);
}

internal sealed class BedrockServerJoinInformation : IEquatable<BedrockServerJoinInformation>
{
    internal BedrockServerJoinInformation(
        BedrockGatheringJoinInfo? gathering,
        BedrockStoreEntryPointInfo? storeEntry,
        BedrockPresenceInfo? presence)
    {
        Gathering = gathering;
        StoreEntry = storeEntry;
        Presence = presence;
    }

    internal BedrockGatheringJoinInfo? Gathering { get; }

    internal BedrockStoreEntryPointInfo? StoreEntry { get; }

    internal BedrockPresenceInfo? Presence { get; }

    public bool Equals(BedrockServerJoinInformation? other) =>
        other is not null
        && Equals(Gathering, other.Gathering)
        && Equals(StoreEntry, other.StoreEntry)
        && Equals(Presence, other.Presence);

    public override bool Equals(object? obj) => Equals(obj as BedrockServerJoinInformation);

    public override int GetHashCode() => HashCode.Combine(Gathering, StoreEntry, Presence);
}

internal sealed class BedrockStartGameTrailer
{
    internal BedrockStartGameTrailer(
        int playerGamemode,
        BedrockVector3 playerPosition,
        float pitch,
        float yaw,
        ulong seed,
        short biomeType,
        string biomeName,
        int dimension,
        int generator,
        int worldGamemode,
        bool hardcore,
        int difficulty,
        BedrockStartGamePosition spawn,
        bool achievementsDisabled,
        int editorWorldType,
        bool createdInEditor,
        bool exportedFromEditor,
        int dayCycleStopTime,
        uint eduOffer,
        bool eduFeaturesEnabled,
        string eduProductUuid,
        float rainLevel,
        float lightningLevel,
        bool platformLockedContent,
        bool multiplayer,
        bool broadcastToLan,
        uint xboxBroadcastMode,
        uint platformBroadcastMode,
        bool commandsEnabled,
        bool texturepacksRequired,
        IReadOnlyList<BedrockGameRule> gamerules,
        IReadOnlyList<BedrockExperimentEntry> experiments,
        bool experimentsPreviouslyUsed,
        bool bonusChest,
        bool mapEnabled,
        byte permissionLevel,
        int serverChunkTickRange,
        bool lockedBehaviorPack,
        bool lockedResourcePack,
        bool fromLockedWorldTemplate,
        bool msaGamertagsOnly,
        bool fromWorldTemplate,
        bool worldTemplateOptionLocked,
        bool onlySpawnV1Villagers,
        bool personaDisabled,
        bool customSkinsDisabled,
        bool emoteChatMuted,
        string gameVersion,
        int limitedWorldWidth,
        int limitedWorldLength,
        bool newNether,
        string eduButtonName,
        string eduLinkUri,
        bool experimentalGameplayOverride,
        byte chatRestrictionLevel,
        bool disablePlayerInteractions,
        int serverEditorConnectionPolicy,
        bool allowAnonymousBlockDrops,
        string levelId,
        string worldName,
        string premiumWorldTemplateId,
        bool trial,
        int rewindHistorySize,
        bool serverAuthoritativeBlockBreaking,
        long currentTick,
        int enchantmentSeed,
        IReadOnlyList<BedrockBlockPropertyEntry> blockProperties,
        string multiplayerCorrelationId,
        bool serverAuthoritativeInventory,
        string engine,
        NbtTag propertyData,
        ulong blockPaletteChecksum,
        Guid worldTemplateId,
        bool clientSideGeneration,
        bool blockNetworkIdsAreHashes,
        bool serverControlledSound,
        BedrockServerJoinInformation? serverJoinInfo,
        string serverIdentifier,
        string scenarioIdentifier,
        string worldIdentifier,
        string ownerIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(biomeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(levelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        ArgumentNullException.ThrowIfNull(eduProductUuid);
        ArgumentNullException.ThrowIfNull(gameVersion);
        ArgumentNullException.ThrowIfNull(eduButtonName);
        ArgumentNullException.ThrowIfNull(eduLinkUri);
        ArgumentNullException.ThrowIfNull(premiumWorldTemplateId);
        ArgumentNullException.ThrowIfNull(multiplayerCorrelationId);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(gamerules);
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(blockProperties);
        ArgumentNullException.ThrowIfNull(propertyData);
        ArgumentNullException.ThrowIfNull(serverIdentifier);
        ArgumentNullException.ThrowIfNull(scenarioIdentifier);
        ArgumentNullException.ThrowIfNull(worldIdentifier);
        ArgumentNullException.ThrowIfNull(ownerIdentifier);
        PlayerGamemode = playerGamemode;
        PlayerPosition = playerPosition;
        Pitch = pitch;
        Yaw = yaw;
        Seed = seed;
        BiomeType = biomeType;
        BiomeName = biomeName;
        Dimension = dimension;
        Generator = generator;
        WorldGamemode = worldGamemode;
        Hardcore = hardcore;
        Difficulty = difficulty;
        Spawn = spawn;
        AchievementsDisabled = achievementsDisabled;
        EditorWorldType = editorWorldType;
        CreatedInEditor = createdInEditor;
        ExportedFromEditor = exportedFromEditor;
        DayCycleStopTime = dayCycleStopTime;
        EduOffer = eduOffer;
        EduFeaturesEnabled = eduFeaturesEnabled;
        EduProductUuid = eduProductUuid;
        RainLevel = rainLevel;
        LightningLevel = lightningLevel;
        PlatformLockedContent = platformLockedContent;
        Multiplayer = multiplayer;
        BroadcastToLan = broadcastToLan;
        XboxBroadcastMode = xboxBroadcastMode;
        PlatformBroadcastMode = platformBroadcastMode;
        CommandsEnabled = commandsEnabled;
        TexturepacksRequired = texturepacksRequired;
        Gamerules = [.. gamerules];
        Experiments = [.. experiments];
        ExperimentsPreviouslyUsed = experimentsPreviouslyUsed;
        BonusChest = bonusChest;
        MapEnabled = mapEnabled;
        PermissionLevel = permissionLevel;
        ServerChunkTickRange = serverChunkTickRange;
        LockedBehaviorPack = lockedBehaviorPack;
        LockedResourcePack = lockedResourcePack;
        FromLockedWorldTemplate = fromLockedWorldTemplate;
        MsaGamertagsOnly = msaGamertagsOnly;
        FromWorldTemplate = fromWorldTemplate;
        WorldTemplateOptionLocked = worldTemplateOptionLocked;
        OnlySpawnV1Villagers = onlySpawnV1Villagers;
        PersonaDisabled = personaDisabled;
        CustomSkinsDisabled = customSkinsDisabled;
        EmoteChatMuted = emoteChatMuted;
        GameVersion = gameVersion;
        LimitedWorldWidth = limitedWorldWidth;
        LimitedWorldLength = limitedWorldLength;
        NewNether = newNether;
        EduButtonName = eduButtonName;
        EduLinkUri = eduLinkUri;
        ExperimentalGameplayOverride = experimentalGameplayOverride;
        ChatRestrictionLevel = chatRestrictionLevel;
        DisablePlayerInteractions = disablePlayerInteractions;
        ServerEditorConnectionPolicy = serverEditorConnectionPolicy;
        AllowAnonymousBlockDrops = allowAnonymousBlockDrops;
        LevelId = levelId;
        WorldName = worldName;
        PremiumWorldTemplateId = premiumWorldTemplateId;
        Trial = trial;
        RewindHistorySize = rewindHistorySize;
        ServerAuthoritativeBlockBreaking = serverAuthoritativeBlockBreaking;
        CurrentTick = currentTick;
        EnchantmentSeed = enchantmentSeed;
        BlockProperties = [.. blockProperties];
        MultiplayerCorrelationId = multiplayerCorrelationId;
        ServerAuthoritativeInventory = serverAuthoritativeInventory;
        Engine = engine;
        PropertyData = propertyData;
        BlockPaletteChecksum = blockPaletteChecksum;
        WorldTemplateId = worldTemplateId;
        ClientSideGeneration = clientSideGeneration;
        BlockNetworkIdsAreHashes = blockNetworkIdsAreHashes;
        ServerControlledSound = serverControlledSound;
        ServerJoinInfo = serverJoinInfo;
        ServerIdentifier = serverIdentifier;
        ScenarioIdentifier = scenarioIdentifier;
        WorldIdentifier = worldIdentifier;
        OwnerIdentifier = ownerIdentifier;
    }

    internal int PlayerGamemode { get; }

    internal BedrockVector3 PlayerPosition { get; }

    internal float Pitch { get; }

    internal float Yaw { get; }

    internal ulong Seed { get; }

    internal short BiomeType { get; }

    internal string BiomeName { get; }

    internal int Dimension { get; }

    internal int Generator { get; }

    internal int WorldGamemode { get; }

    internal bool Hardcore { get; }

    internal int Difficulty { get; }

    internal BedrockStartGamePosition Spawn { get; }

    internal bool AchievementsDisabled { get; }

    internal int EditorWorldType { get; }

    internal bool CreatedInEditor { get; }

    internal bool ExportedFromEditor { get; }

    internal int DayCycleStopTime { get; }

    internal uint EduOffer { get; }

    internal bool EduFeaturesEnabled { get; }

    internal string EduProductUuid { get; }

    internal float RainLevel { get; }

    internal float LightningLevel { get; }

    internal bool PlatformLockedContent { get; }

    internal bool Multiplayer { get; }

    internal bool BroadcastToLan { get; }

    internal uint XboxBroadcastMode { get; }

    internal uint PlatformBroadcastMode { get; }

    internal bool CommandsEnabled { get; }

    internal bool TexturepacksRequired { get; }

    internal IReadOnlyList<BedrockGameRule> Gamerules { get; }

    internal IReadOnlyList<BedrockExperimentEntry> Experiments { get; }

    internal bool ExperimentsPreviouslyUsed { get; }

    internal bool BonusChest { get; }

    internal bool MapEnabled { get; }

    internal byte PermissionLevel { get; }

    internal int ServerChunkTickRange { get; }

    internal bool LockedBehaviorPack { get; }

    internal bool LockedResourcePack { get; }

    internal bool FromLockedWorldTemplate { get; }

    internal bool MsaGamertagsOnly { get; }

    internal bool FromWorldTemplate { get; }

    internal bool WorldTemplateOptionLocked { get; }

    internal bool OnlySpawnV1Villagers { get; }

    internal bool PersonaDisabled { get; }

    internal bool CustomSkinsDisabled { get; }

    internal bool EmoteChatMuted { get; }

    internal string GameVersion { get; }

    internal int LimitedWorldWidth { get; }

    internal int LimitedWorldLength { get; }

    internal bool NewNether { get; }

    internal string EduButtonName { get; }

    internal string EduLinkUri { get; }

    internal bool ExperimentalGameplayOverride { get; }

    internal byte ChatRestrictionLevel { get; }

    internal bool DisablePlayerInteractions { get; }

    internal int ServerEditorConnectionPolicy { get; }

    internal bool AllowAnonymousBlockDrops { get; }

    internal string LevelId { get; }

    internal string WorldName { get; }

    internal string PremiumWorldTemplateId { get; }

    internal bool Trial { get; }

    internal int RewindHistorySize { get; }

    internal bool ServerAuthoritativeBlockBreaking { get; }

    internal long CurrentTick { get; }

    internal int EnchantmentSeed { get; }

    internal IReadOnlyList<BedrockBlockPropertyEntry> BlockProperties { get; }

    internal string MultiplayerCorrelationId { get; }

    internal bool ServerAuthoritativeInventory { get; }

    internal string Engine { get; }

    internal NbtTag PropertyData { get; }

    internal ulong BlockPaletteChecksum { get; }

    internal Guid WorldTemplateId { get; }

    internal bool ClientSideGeneration { get; }

    internal bool BlockNetworkIdsAreHashes { get; }

    internal bool ServerControlledSound { get; }

    internal BedrockServerJoinInformation? ServerJoinInfo { get; }

    internal string ServerIdentifier { get; }

    internal string ScenarioIdentifier { get; }

    internal string WorldIdentifier { get; }

    internal string OwnerIdentifier { get; }
}

internal static class BedrockStartGameTrailerCodecs
{
    internal static BedrockStartGameTrailer DecodeTrailer(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int playerGamemode = reader.ReadZigZag32();
        var playerPosition = new BedrockVector3(reader.ReadSingleLittleEndian(), reader.ReadSingleLittleEndian(), reader.ReadSingleLittleEndian());
        float pitch = reader.ReadSingleLittleEndian();
        float yaw = reader.ReadSingleLittleEndian();
        ulong seed = reader.ReadUInt64LittleEndian();
        short biomeType = reader.ReadInt16LittleEndian();
        string biomeName = reader.ReadString();
        int dimension = reader.ReadZigZag32();
        int generator = reader.ReadZigZag32();
        int worldGamemode = reader.ReadZigZag32();
        bool hardcore = reader.ReadBoolean();
        int difficulty = reader.ReadZigZag32();
        var spawn = new BedrockStartGamePosition(reader.ReadZigZag32(), reader.ReadZigZag32(), reader.ReadZigZag32());
        bool achievementsDisabled = reader.ReadBoolean();
        int editorWorldType = reader.ReadZigZag32();
        bool createdInEditor = reader.ReadBoolean();
        bool exportedFromEditor = reader.ReadBoolean();
        int dayCycleStopTime = reader.ReadZigZag32();
        uint eduOffer = reader.ReadUInt32VarInt();
        bool eduFeaturesEnabled = reader.ReadBoolean();
        string eduProductUuid = reader.ReadString();
        float rainLevel = reader.ReadSingleLittleEndian();
        float lightningLevel = reader.ReadSingleLittleEndian();
        bool platformLockedContent = reader.ReadBoolean();
        bool multiplayer = reader.ReadBoolean();
        bool broadcastToLan = reader.ReadBoolean();
        uint xboxBroadcastMode = reader.ReadUInt32VarInt();
        uint platformBroadcastMode = reader.ReadUInt32VarInt();
        bool commandsEnabled = reader.ReadBoolean();
        bool texturepacksRequired = reader.ReadBoolean();
        uint gameruleCount = reader.ReadUInt32VarInt();
        if (gameruleCount > (uint)actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The gamerule count {gameruleCount} exceeds its element limit.");
        var gamerules = new BedrockGameRule[gameruleCount];
        for (int index = 0; index < gamerules.Length; index++)
            gamerules[index] = ReadGamerule(ref reader);
        int experimentCount = reader.ReadInt32LittleEndian();
        if (experimentCount < 0 || experimentCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The experiment count {experimentCount} is outside its element limit.");
        var experiments = new BedrockExperimentEntry[experimentCount];
        for (int index = 0; index < experiments.Length; index++)
            experiments[index] = new BedrockExperimentEntry(reader.ReadString(), reader.ReadBoolean());
        bool experimentsPreviouslyUsed = reader.ReadBoolean();
        bool bonusChest = reader.ReadBoolean();
        bool mapEnabled = reader.ReadBoolean();
        byte permissionLevel = reader.ReadByte();
        int serverChunkTickRange = reader.ReadInt32LittleEndian();
        bool lockedBehaviorPack = reader.ReadBoolean();
        bool lockedResourcePack = reader.ReadBoolean();
        bool fromLockedWorldTemplate = reader.ReadBoolean();
        bool msaGamertagsOnly = reader.ReadBoolean();
        bool fromWorldTemplate = reader.ReadBoolean();
        bool worldTemplateOptionLocked = reader.ReadBoolean();
        bool onlySpawnV1Villagers = reader.ReadBoolean();
        bool personaDisabled = reader.ReadBoolean();
        bool customSkinsDisabled = reader.ReadBoolean();
        bool emoteChatMuted = reader.ReadBoolean();
        string gameVersion = reader.ReadString();
        int limitedWorldWidth = reader.ReadInt32LittleEndian();
        int limitedWorldLength = reader.ReadInt32LittleEndian();
        bool newNether = reader.ReadBoolean();
        string eduButtonName = reader.ReadString();
        string eduLinkUri = reader.ReadString();
        bool experimentalGameplayOverride = reader.ReadBoolean();
        byte chatRestrictionLevel = reader.ReadByte();
        bool disablePlayerInteractions = reader.ReadBoolean();
        int serverEditorConnectionPolicy = reader.ReadZigZag32();
        bool allowAnonymousBlockDrops = reader.ReadBoolean();
        string levelId = reader.ReadString();
        string worldName = reader.ReadString();
        string premiumWorldTemplateId = reader.ReadString();
        bool trial = reader.ReadBoolean();
        int rewindHistorySize = reader.ReadZigZag32();
        bool serverAuthoritativeBlockBreaking = reader.ReadBoolean();
        long currentTick = reader.ReadInt64LittleEndian();
        int enchantmentSeed = reader.ReadZigZag32();
        uint propertyCount = reader.ReadUInt32VarInt();
        if (propertyCount > (uint)actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The block property count {propertyCount} exceeds its element limit.");
        var blockProperties = new BedrockBlockPropertyEntry[propertyCount];
        for (int index = 0; index < blockProperties.Length; index++)
            blockProperties[index] = new BedrockBlockPropertyEntry(reader.ReadString(), BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, actualLimits));
        string multiplayerCorrelationId = reader.ReadString();
        bool serverAuthoritativeInventory = reader.ReadBoolean();
        string engine = reader.ReadString();
        NbtTag propertyData = BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, actualLimits);
        ulong blockPaletteChecksum = reader.ReadUInt64LittleEndian();
        Guid worldTemplateId = reader.ReadUuid();
        bool clientSideGeneration = reader.ReadBoolean();
        bool blockNetworkIdsAreHashes = reader.ReadBoolean();
        bool serverControlledSound = reader.ReadBoolean();
        BedrockServerJoinInformation? serverJoinInfo = reader.ReadBoolean() ? ReadServerJoinInformation(ref reader, actualLimits) : null;
        string serverIdentifier = reader.ReadString();
        string scenarioIdentifier = reader.ReadString();
        string worldIdentifier = reader.ReadString();
        string ownerIdentifier = reader.ReadString();
        if (!reader.End)
            throw new BedrockCodecException("The StartGame remainder has unread bytes.");
        return new BedrockStartGameTrailer(
            playerGamemode, playerPosition, pitch, yaw, seed, biomeType, biomeName, dimension, generator,
            worldGamemode, hardcore, difficulty, spawn, achievementsDisabled, editorWorldType, createdInEditor,
            exportedFromEditor, dayCycleStopTime, eduOffer, eduFeaturesEnabled, eduProductUuid, rainLevel,
            lightningLevel, platformLockedContent, multiplayer, broadcastToLan, xboxBroadcastMode,
            platformBroadcastMode, commandsEnabled, texturepacksRequired, gamerules, experiments,
            experimentsPreviouslyUsed, bonusChest, mapEnabled, permissionLevel, serverChunkTickRange,
            lockedBehaviorPack, lockedResourcePack, fromLockedWorldTemplate, msaGamertagsOnly, fromWorldTemplate,
            worldTemplateOptionLocked, onlySpawnV1Villagers, personaDisabled, customSkinsDisabled, emoteChatMuted,
            gameVersion, limitedWorldWidth, limitedWorldLength, newNether, eduButtonName, eduLinkUri,
            experimentalGameplayOverride, chatRestrictionLevel, disablePlayerInteractions,
            serverEditorConnectionPolicy, allowAnonymousBlockDrops, levelId, worldName, premiumWorldTemplateId,
            trial, rewindHistorySize, serverAuthoritativeBlockBreaking, currentTick, enchantmentSeed,
            blockProperties, multiplayerCorrelationId, serverAuthoritativeInventory, engine, propertyData,
            blockPaletteChecksum, worldTemplateId, clientSideGeneration, blockNetworkIdsAreHashes,
            serverControlledSound, serverJoinInfo, serverIdentifier, scenarioIdentifier, worldIdentifier,
            ownerIdentifier);
    }

    internal static byte[] EncodeTrailer(BedrockStartGameTrailer trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32(trailer.PlayerGamemode);
        writer.WriteSingleLittleEndian(trailer.PlayerPosition.X);
        writer.WriteSingleLittleEndian(trailer.PlayerPosition.Y);
        writer.WriteSingleLittleEndian(trailer.PlayerPosition.Z);
        writer.WriteSingleLittleEndian(trailer.Pitch);
        writer.WriteSingleLittleEndian(trailer.Yaw);
        writer.WriteUInt64LittleEndian(trailer.Seed);
        writer.WriteInt16LittleEndian(trailer.BiomeType);
        writer.WriteString(trailer.BiomeName);
        writer.WriteZigZag32(trailer.Dimension);
        writer.WriteZigZag32(trailer.Generator);
        writer.WriteZigZag32(trailer.WorldGamemode);
        writer.WriteBoolean(trailer.Hardcore);
        writer.WriteZigZag32(trailer.Difficulty);
        writer.WriteZigZag32(trailer.Spawn.X);
        writer.WriteZigZag32(trailer.Spawn.Y);
        writer.WriteZigZag32(trailer.Spawn.Z);
        writer.WriteBoolean(trailer.AchievementsDisabled);
        writer.WriteZigZag32(trailer.EditorWorldType);
        writer.WriteBoolean(trailer.CreatedInEditor);
        writer.WriteBoolean(trailer.ExportedFromEditor);
        writer.WriteZigZag32(trailer.DayCycleStopTime);
        writer.WriteUInt32VarInt(trailer.EduOffer);
        writer.WriteBoolean(trailer.EduFeaturesEnabled);
        writer.WriteString(trailer.EduProductUuid);
        writer.WriteSingleLittleEndian(trailer.RainLevel);
        writer.WriteSingleLittleEndian(trailer.LightningLevel);
        writer.WriteBoolean(trailer.PlatformLockedContent);
        writer.WriteBoolean(trailer.Multiplayer);
        writer.WriteBoolean(trailer.BroadcastToLan);
        writer.WriteUInt32VarInt(trailer.XboxBroadcastMode);
        writer.WriteUInt32VarInt(trailer.PlatformBroadcastMode);
        writer.WriteBoolean(trailer.CommandsEnabled);
        writer.WriteBoolean(trailer.TexturepacksRequired);
        writer.WriteUInt32VarInt(checked((uint)trailer.Gamerules.Count));
        foreach (BedrockGameRule gamerule in trailer.Gamerules)
            WriteGamerule(writer, gamerule);
        writer.WriteInt32LittleEndian(trailer.Experiments.Count);
        foreach (BedrockExperimentEntry experiment in trailer.Experiments)
        {
            writer.WriteString(experiment.Name);
            writer.WriteBoolean(experiment.Enabled);
        }

        writer.WriteBoolean(trailer.ExperimentsPreviouslyUsed);
        writer.WriteBoolean(trailer.BonusChest);
        writer.WriteBoolean(trailer.MapEnabled);
        writer.WriteByte(trailer.PermissionLevel);
        writer.WriteInt32LittleEndian(trailer.ServerChunkTickRange);
        writer.WriteBoolean(trailer.LockedBehaviorPack);
        writer.WriteBoolean(trailer.LockedResourcePack);
        writer.WriteBoolean(trailer.FromLockedWorldTemplate);
        writer.WriteBoolean(trailer.MsaGamertagsOnly);
        writer.WriteBoolean(trailer.FromWorldTemplate);
        writer.WriteBoolean(trailer.WorldTemplateOptionLocked);
        writer.WriteBoolean(trailer.OnlySpawnV1Villagers);
        writer.WriteBoolean(trailer.PersonaDisabled);
        writer.WriteBoolean(trailer.CustomSkinsDisabled);
        writer.WriteBoolean(trailer.EmoteChatMuted);
        writer.WriteString(trailer.GameVersion);
        writer.WriteInt32LittleEndian(trailer.LimitedWorldWidth);
        writer.WriteInt32LittleEndian(trailer.LimitedWorldLength);
        writer.WriteBoolean(trailer.NewNether);
        writer.WriteString(trailer.EduButtonName);
        writer.WriteString(trailer.EduLinkUri);
        writer.WriteBoolean(trailer.ExperimentalGameplayOverride);
        writer.WriteByte(trailer.ChatRestrictionLevel);
        writer.WriteBoolean(trailer.DisablePlayerInteractions);
        writer.WriteZigZag32(trailer.ServerEditorConnectionPolicy);
        writer.WriteBoolean(trailer.AllowAnonymousBlockDrops);
        writer.WriteString(trailer.LevelId);
        writer.WriteString(trailer.WorldName);
        writer.WriteString(trailer.PremiumWorldTemplateId);
        writer.WriteBoolean(trailer.Trial);
        writer.WriteZigZag32(trailer.RewindHistorySize);
        writer.WriteBoolean(trailer.ServerAuthoritativeBlockBreaking);
        writer.WriteInt64LittleEndian(trailer.CurrentTick);
        writer.WriteZigZag32(trailer.EnchantmentSeed);
        writer.WriteUInt32VarInt(checked((uint)trailer.BlockProperties.Count));
        foreach (BedrockBlockPropertyEntry property in trailer.BlockProperties)
        {
            writer.WriteString(property.Name);
            writer.WriteBytes(BedrockNbtCodec.Encode(property.State, BedrockNbtWireFormat.NetworkLittleVarInt));
        }

        writer.WriteString(trailer.MultiplayerCorrelationId);
        writer.WriteBoolean(trailer.ServerAuthoritativeInventory);
        writer.WriteString(trailer.Engine);
        writer.WriteBytes(BedrockNbtCodec.Encode(trailer.PropertyData, BedrockNbtWireFormat.NetworkLittleVarInt));
        writer.WriteUInt64LittleEndian(trailer.BlockPaletteChecksum);
        writer.WriteUuid(trailer.WorldTemplateId);
        writer.WriteBoolean(trailer.ClientSideGeneration);
        writer.WriteBoolean(trailer.BlockNetworkIdsAreHashes);
        writer.WriteBoolean(trailer.ServerControlledSound);
        writer.WriteBoolean(trailer.ServerJoinInfo is not null);
        if (trailer.ServerJoinInfo is not null)
            WriteServerJoinInformation(writer, trailer.ServerJoinInfo);
        writer.WriteString(trailer.ServerIdentifier);
        writer.WriteString(trailer.ScenarioIdentifier);
        writer.WriteString(trailer.WorldIdentifier);
        writer.WriteString(trailer.OwnerIdentifier);
        return output.WrittenSpan.ToArray();
    }

    private static BedrockGameRule ReadGamerule(ref BedrockReader reader)
    {
        string name = reader.ReadString();
        bool editable = reader.ReadBoolean();
        uint type = reader.ReadUInt32VarInt();
        return type switch
        {
            1 => new BedrockGameRule(name, editable, BedrockGameRuleKind.Bool, reader.ReadBoolean(), 0, 0),
            2 => new BedrockGameRule(name, editable, BedrockGameRuleKind.Int, false, reader.ReadInt32LittleEndian(), 0),
            3 => new BedrockGameRule(name, editable, BedrockGameRuleKind.Float, false, 0, reader.ReadSingleLittleEndian()),
            _ => throw new BedrockCodecException($"The gamerule '{name}' carries unknown value type {type}."),
        };
    }

    private static void WriteGamerule(BedrockWriter writer, BedrockGameRule gamerule)
    {
        writer.WriteString(gamerule.Name);
        writer.WriteBoolean(gamerule.Editable);
        writer.WriteUInt32VarInt((uint)gamerule.Kind);
        switch (gamerule.Kind)
        {
            case BedrockGameRuleKind.Bool:
                writer.WriteBoolean(gamerule.BoolValue);
                break;
            case BedrockGameRuleKind.Int:
                writer.WriteInt32LittleEndian(gamerule.IntValue);
                break;
            case BedrockGameRuleKind.Float:
                writer.WriteSingleLittleEndian(gamerule.FloatValue);
                break;
            default:
                throw new BedrockCodecException($"The gamerule '{gamerule.Name}' carries unknown value kind {gamerule.Kind}.");
        }
    }

    private static BedrockServerJoinInformation ReadServerJoinInformation(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        BedrockGatheringJoinInfo? gathering = reader.ReadBoolean() ? ReadGatheringJoinInfo(ref reader) : null;
        BedrockStoreEntryPointInfo? storeEntry = reader.ReadBoolean() ? new BedrockStoreEntryPointInfo(reader.ReadString(), reader.ReadString()) : null;
        BedrockPresenceInfo? presence = reader.ReadBoolean()
            ? new BedrockPresenceInfo(ReadOptionalString(ref reader), ReadOptionalString(ref reader), ReadOptionalString(ref reader))
            : null;
        _ = limits;
        return new BedrockServerJoinInformation(gathering, storeEntry, presence);
    }

    private static BedrockGatheringJoinInfo ReadGatheringJoinInfo(ref BedrockReader reader) =>
        new(
            reader.ReadUuid(),
            reader.ReadString(),
            ReadOptionalUuid(ref reader),
            ReadOptionalString(ref reader),
            reader.ReadString(),
            ReadOptionalUuid(ref reader),
            ReadOptionalString(ref reader),
            ReadOptionalString(ref reader));

    private static void WriteServerJoinInformation(BedrockWriter writer, BedrockServerJoinInformation info)
    {
        writer.WriteBoolean(info.Gathering is not null);
        if (info.Gathering is not null)
            WriteGatheringJoinInfo(writer, info.Gathering);
        writer.WriteBoolean(info.StoreEntry is not null);
        if (info.StoreEntry is not null)
        {
            writer.WriteString(info.StoreEntry.StoreId);
            writer.WriteString(info.StoreEntry.StoreName);
        }

        writer.WriteBoolean(info.Presence is not null);
        if (info.Presence is not null)
        {
            WriteOptionalString(writer, info.Presence.ExperienceName);
            WriteOptionalString(writer, info.Presence.WorldName);
            WriteOptionalString(writer, info.Presence.RichPresenceId);
        }
    }

    private static void WriteGatheringJoinInfo(BedrockWriter writer, BedrockGatheringJoinInfo gathering)
    {
        writer.WriteUuid(gathering.ExperienceId);
        writer.WriteString(gathering.ExperienceName);
        WriteOptionalUuid(writer, gathering.ExperienceWorldId);
        WriteOptionalString(writer, gathering.ExperienceWorldName);
        writer.WriteString(gathering.CreatorId);
        WriteOptionalUuid(writer, gathering.TargetId);
        WriteOptionalString(writer, gathering.ScenarioId);
        WriteOptionalString(writer, gathering.ServerId);
    }

    private static string? ReadOptionalString(ref BedrockReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

    private static void WriteOptionalString(BedrockWriter writer, string? value)
    {
        writer.WriteBoolean(value is not null);
        if (value is not null)
            writer.WriteString(value);
    }

    private static Guid? ReadOptionalUuid(ref BedrockReader reader) => reader.ReadBoolean() ? reader.ReadUuid() : null;

    private static void WriteOptionalUuid(BedrockWriter writer, Guid? value)
    {
        writer.WriteBoolean(value.HasValue);
        if (value.HasValue)
            writer.WriteUuid(value.Value);
    }
}
