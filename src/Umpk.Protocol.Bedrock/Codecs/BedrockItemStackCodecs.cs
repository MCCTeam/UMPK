using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockFullContainerName(byte ContainerId, uint? DynamicContainerId);

internal readonly record struct BedrockStackRequestSlot(BedrockFullContainerName Container, byte Slot, int StackId);

internal enum BedrockItemStackActionType
{
    Take = 0,
    Place = 1,
    Swap = 2,
    Drop = 3,
    Destroy = 4,
    Consume = 5,
    Create = 6,
    LabTableCombine = 7,
    BeaconPayment = 8,
    MineBlock = 9,
    CraftRecipe = 10,
    CraftRecipeAuto = 11,
    CraftCreative = 12,
    Optional = 13,
    CraftGrindstoneRequest = 14,
    CraftLoomRequest = 15,
    NonImplemented = 16,
    ResultsDeprecated = 17,
}

internal abstract record BedrockItemStackActionPayload;

internal sealed record BedrockTakePayload(byte Count, BedrockStackRequestSlot Source, BedrockStackRequestSlot Destination) : BedrockItemStackActionPayload;

internal sealed record BedrockPlacePayload(byte Count, BedrockStackRequestSlot Source, BedrockStackRequestSlot Destination) : BedrockItemStackActionPayload;

internal sealed record BedrockSwapPayload(BedrockStackRequestSlot Source, BedrockStackRequestSlot Destination) : BedrockItemStackActionPayload;

internal sealed record BedrockDropPayload(byte Count, BedrockStackRequestSlot Source, bool Randomly) : BedrockItemStackActionPayload;

internal sealed record BedrockDestroyPayload(byte Count, BedrockStackRequestSlot Source) : BedrockItemStackActionPayload;

internal sealed record BedrockConsumePayload(byte Count, BedrockStackRequestSlot Source) : BedrockItemStackActionPayload;

internal sealed record BedrockCreatePayload(byte ResultSlotId) : BedrockItemStackActionPayload;

internal sealed record BedrockLabTableCombinePayload : BedrockItemStackActionPayload
{
    internal static BedrockLabTableCombinePayload Instance { get; } = new();

    private BedrockLabTableCombinePayload()
    {
    }
}

internal sealed record BedrockBeaconPaymentPayload(int PrimaryEffect, int SecondaryEffect) : BedrockItemStackActionPayload;

internal sealed record BedrockMineBlockPayload(int HotbarSlot, int PredictedDurability, int NetworkId) : BedrockItemStackActionPayload;

internal sealed record BedrockCraftRecipePayload(int RecipeNetworkId, byte TimesCrafted) : BedrockItemStackActionPayload;

internal sealed record BedrockCraftRecipeAutoPayload(int RecipeNetworkId, byte TimesCrafted, ImmutableArray<BedrockRecipeIngredient> Ingredients) : BedrockItemStackActionPayload
{
    public bool Equals(BedrockCraftRecipeAutoPayload? other) =>
        other is not null
        && RecipeNetworkId == other.RecipeNetworkId
        && TimesCrafted == other.TimesCrafted
        && Ingredients.SequenceEqual(other.Ingredients);

    public override int GetHashCode() => HashCode.Combine(RecipeNetworkId, TimesCrafted, Ingredients.Length);
}

internal sealed record BedrockCraftCreativePayload(int ItemId, byte TimesCrafted) : BedrockItemStackActionPayload;

internal sealed record BedrockOptionalPayload(int RecipeNetworkId, int FilteredStringIndex) : BedrockItemStackActionPayload;

internal sealed record BedrockCraftGrindstoneRequestPayload(int RecipeNetworkId, byte TimesCrafted, int Cost) : BedrockItemStackActionPayload;

internal sealed record BedrockCraftLoomRequestPayload(string Pattern, byte TimesCrafted) : BedrockItemStackActionPayload;

internal sealed record BedrockNonImplementedPayload : BedrockItemStackActionPayload
{
    internal static BedrockNonImplementedPayload Instance { get; } = new();

    private BedrockNonImplementedPayload()
    {
    }
}

internal sealed record BedrockResultsDeprecatedPayload(ImmutableArray<BedrockItemStackResultDescriptor> ResultItems, byte TimesCrafted) : BedrockItemStackActionPayload
{
    public bool Equals(BedrockResultsDeprecatedPayload? other) =>
        other is not null && TimesCrafted == other.TimesCrafted && ResultItems.SequenceEqual(other.ResultItems);

    public override int GetHashCode() => HashCode.Combine(ResultItems.Length, TimesCrafted);
}

internal enum BedrockRecipeIngredientKind
{
    Invalid = 0,
    Name = 1,
    Molang = 2,
    ItemTag = 3,
}

internal sealed class BedrockRecipeIngredient : IEquatable<BedrockRecipeIngredient>
{
    internal BedrockRecipeIngredient(BedrockRecipeIngredientKind kind, byte legacyType, string? name, int? metadata, string? expression, short? version, string? tag)
    {
        Kind = kind;
        LegacyType = legacyType;
        Name = name;
        Metadata = metadata;
        Expression = expression;
        Version = version;
        Tag = tag;
    }

    internal BedrockRecipeIngredientKind Kind { get; }

    internal byte LegacyType { get; }

    internal string? Name { get; }

    internal int? Metadata { get; }

    internal string? Expression { get; }

    internal short? Version { get; }

    internal string? Tag { get; }

    public bool Equals(BedrockRecipeIngredient? other) =>
        other is not null
        && Kind == other.Kind
        && LegacyType == other.LegacyType
        && Name == other.Name
        && Metadata == other.Metadata
        && Expression == other.Expression
        && Version == other.Version
        && Tag == other.Tag;

    public override bool Equals(object? obj) => Equals(obj as BedrockRecipeIngredient);

    public override int GetHashCode() => HashCode.Combine((int)Kind, LegacyType, Name, Tag);
}

internal enum BedrockResultDescriptorKind
{
    Invalid = 0,
    Name = 1,
}

internal sealed class BedrockItemStackResultDescriptor : IEquatable<BedrockItemStackResultDescriptor>
{
    internal BedrockItemStackResultDescriptor(int type, byte legacyType, string? name, int? metadata, short count, uint blockRuntimeId, byte[] extraData)
    {
        ArgumentNullException.ThrowIfNull(extraData);
        Type = type;
        LegacyType = legacyType;
        Name = name;
        Metadata = metadata;
        Count = count;
        BlockRuntimeId = blockRuntimeId;
        ExtraData = (byte[])extraData.Clone();
    }

    internal int Type { get; }

    internal byte LegacyType { get; }

    internal string? Name { get; }

    internal int? Metadata { get; }

    internal short Count { get; }

    internal uint BlockRuntimeId { get; }

    internal byte[] ExtraData { get; }

    public bool Equals(BedrockItemStackResultDescriptor? other) =>
        other is not null
        && Type == other.Type
        && LegacyType == other.LegacyType
        && Name == other.Name
        && Metadata == other.Metadata
        && Count == other.Count
        && BlockRuntimeId == other.BlockRuntimeId
        && ExtraData.SequenceEqual(other.ExtraData);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackResultDescriptor);

    public override int GetHashCode() => HashCode.Combine(Type, LegacyType, Count, BlockRuntimeId);
}

internal sealed class BedrockItemStackAction : IEquatable<BedrockItemStackAction>
{
    internal BedrockItemStackAction(BedrockItemStackActionType type, byte legacyTypeId, BedrockItemStackActionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!Matches(type, payload))
            throw new BedrockCodecException($"The item stack action {type} carries an incompatible payload.");
        Type = type;
        LegacyTypeId = legacyTypeId;
        Payload = payload;
    }

    internal BedrockItemStackActionType Type { get; }

    internal byte LegacyTypeId { get; }

    internal BedrockItemStackActionPayload Payload { get; }

    public bool Equals(BedrockItemStackAction? other) =>
        other is not null && Type == other.Type && LegacyTypeId == other.LegacyTypeId && Payload.Equals(other.Payload);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackAction);

    public override int GetHashCode() => HashCode.Combine((int)Type, LegacyTypeId, Payload);

    private static bool Matches(BedrockItemStackActionType type, BedrockItemStackActionPayload payload) => (type, payload) switch
    {
        (BedrockItemStackActionType.Take, BedrockTakePayload) => true,
        (BedrockItemStackActionType.Place, BedrockPlacePayload) => true,
        (BedrockItemStackActionType.Swap, BedrockSwapPayload) => true,
        (BedrockItemStackActionType.Drop, BedrockDropPayload) => true,
        (BedrockItemStackActionType.Destroy, BedrockDestroyPayload) => true,
        (BedrockItemStackActionType.Consume, BedrockConsumePayload) => true,
        (BedrockItemStackActionType.Create, BedrockCreatePayload) => true,
        (BedrockItemStackActionType.LabTableCombine, BedrockLabTableCombinePayload) => true,
        (BedrockItemStackActionType.BeaconPayment, BedrockBeaconPaymentPayload) => true,
        (BedrockItemStackActionType.MineBlock, BedrockMineBlockPayload) => true,
        (BedrockItemStackActionType.CraftRecipe, BedrockCraftRecipePayload) => true,
        (BedrockItemStackActionType.CraftRecipeAuto, BedrockCraftRecipeAutoPayload) => true,
        (BedrockItemStackActionType.CraftCreative, BedrockCraftCreativePayload) => true,
        (BedrockItemStackActionType.Optional, BedrockOptionalPayload) => true,
        (BedrockItemStackActionType.CraftGrindstoneRequest, BedrockCraftGrindstoneRequestPayload) => true,
        (BedrockItemStackActionType.CraftLoomRequest, BedrockCraftLoomRequestPayload) => true,
        (BedrockItemStackActionType.NonImplemented, BedrockNonImplementedPayload) => true,
        (BedrockItemStackActionType.ResultsDeprecated, BedrockResultsDeprecatedPayload) => true,
        _ => false,
    };
}

internal sealed class BedrockItemStackRequest : IEquatable<BedrockItemStackRequest>
{
    internal BedrockItemStackRequest(int requestId, IReadOnlyList<BedrockItemStackAction> actions, IReadOnlyList<string> customNames, int cause)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(customNames);
        if (requestId == 0)
            throw new BedrockCodecException("The item stack request identifier 0 is a sentinel and cannot identify a request.");
        RequestId = requestId;
        Actions = [.. actions];
        CustomNames = [.. customNames];
        Cause = cause;
    }

    internal int RequestId { get; }

    internal ImmutableArray<BedrockItemStackAction> Actions { get; }

    internal ImmutableArray<string> CustomNames { get; }

    internal int Cause { get; }

    public bool Equals(BedrockItemStackRequest? other) =>
        other is not null
        && RequestId == other.RequestId
        && Actions.SequenceEqual(other.Actions)
        && CustomNames.SequenceEqual(other.CustomNames)
        && Cause == other.Cause;

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackRequest);

    public override int GetHashCode() => HashCode.Combine(RequestId, Actions.Length, Cause);
}

internal sealed class BedrockItemStackRequestPacket : IEquatable<BedrockItemStackRequestPacket>
{
    internal BedrockItemStackRequestPacket(IReadOnlyList<BedrockItemStackRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        Requests = [.. requests];
    }

    internal ImmutableArray<BedrockItemStackRequest> Requests { get; }

    public bool Equals(BedrockItemStackRequestPacket? other) =>
        other is not null && Requests.SequenceEqual(other.Requests);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackRequestPacket);

    public override int GetHashCode() => Requests.Length;
}

internal sealed class BedrockItemStackResponseSlot : IEquatable<BedrockItemStackResponseSlot>
{
    internal BedrockItemStackResponseSlot(byte slot, byte hotbarSlot, byte count, int? stackId, string customName, string filteredCustomName, int durabilityCorrection)
    {
        ArgumentNullException.ThrowIfNull(customName);
        ArgumentNullException.ThrowIfNull(filteredCustomName);
        Slot = slot;
        HotbarSlot = hotbarSlot;
        Count = count;
        StackId = stackId;
        CustomName = customName;
        FilteredCustomName = filteredCustomName;
        DurabilityCorrection = durabilityCorrection;
    }

    internal byte Slot { get; }

    internal byte HotbarSlot { get; }

    internal byte Count { get; }

    internal int? StackId { get; }

    internal string CustomName { get; }

    internal string FilteredCustomName { get; }

    internal int DurabilityCorrection { get; }

    public bool Equals(BedrockItemStackResponseSlot? other) =>
        other is not null
        && Slot == other.Slot
        && HotbarSlot == other.HotbarSlot
        && Count == other.Count
        && StackId == other.StackId
        && CustomName == other.CustomName
        && FilteredCustomName == other.FilteredCustomName
        && DurabilityCorrection == other.DurabilityCorrection;

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackResponseSlot);

    public override int GetHashCode() => HashCode.Combine(Slot, HotbarSlot, Count, StackId);
}

internal sealed class BedrockItemStackResponseContainer : IEquatable<BedrockItemStackResponseContainer>
{
    internal BedrockItemStackResponseContainer(BedrockFullContainerName slotType, IReadOnlyList<BedrockItemStackResponseSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        SlotType = slotType;
        Slots = [.. slots];
    }

    internal BedrockFullContainerName SlotType { get; }

    internal ImmutableArray<BedrockItemStackResponseSlot> Slots { get; }

    public bool Equals(BedrockItemStackResponseContainer? other) =>
        other is not null && SlotType.Equals(other.SlotType) && Slots.SequenceEqual(other.Slots);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackResponseContainer);

    public override int GetHashCode() => HashCode.Combine(SlotType, Slots.Length);
}

internal sealed class BedrockItemStackResponse : IEquatable<BedrockItemStackResponse>
{
    internal const byte OkStatus = 0;
    internal const byte ErrorStatus = 1;

    internal BedrockItemStackResponse(byte status, int requestId, bool containersPresent, IReadOnlyList<BedrockItemStackResponseContainer> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        Status = status;
        RequestId = requestId;
        ContainersPresent = containersPresent;
        Containers = [.. containers];
    }

    internal byte Status { get; }

    internal bool IsOk => Status == OkStatus;

    internal int RequestId { get; }

    internal bool ContainersPresent { get; }

    internal ImmutableArray<BedrockItemStackResponseContainer> Containers { get; }

    public bool Equals(BedrockItemStackResponse? other) =>
        other is not null
        && Status == other.Status
        && RequestId == other.RequestId
        && ContainersPresent == other.ContainersPresent
        && Containers.SequenceEqual(other.Containers);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackResponse);

    public override int GetHashCode() => HashCode.Combine(Status, RequestId, Containers.Length);
}

internal sealed class BedrockItemStackResponsePacket : IEquatable<BedrockItemStackResponsePacket>
{
    internal BedrockItemStackResponsePacket(IReadOnlyList<BedrockItemStackResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        Responses = [.. responses];
    }

    internal ImmutableArray<BedrockItemStackResponse> Responses { get; }

    public bool Equals(BedrockItemStackResponsePacket? other) =>
        other is not null && Responses.SequenceEqual(other.Responses);

    public override bool Equals(object? obj) => Equals(obj as BedrockItemStackResponsePacket);

    public override int GetHashCode() => Responses.Length;
}

internal static class BedrockItemStackCodecs
{
    internal const int MaxRequestsPerPacket = 4096;
    internal const int MaxActionsPerRequest = 256;
    internal const int MaxCustomNamesPerRequest = 256;
    internal const int MaxIngredientsPerAction = 256;
    internal const int MaxResultItemsPerAction = 256;
    internal const int MaxResponsesPerPacket = 4096;
    internal const int MaxContainersPerResponse = 64;
    internal const int MaxSlotsPerResponseContainer = 1024;

    internal static byte[] Encode(BedrockItemStackRequestPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt32VarInt(checked((uint)packet.Requests.Length));
            foreach (BedrockItemStackRequest request in packet.Requests)
                WriteRequest(writer, request);
        });
    }

    internal static BedrockItemStackRequestPacket DecodeRequest(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxRequestsPerPacket || count > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The item stack request count {count} exceeds its element limit.");
        var requests = new List<BedrockItemStackRequest>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            requests.Add(ReadRequest(ref reader, actualLimits));
        EnsureEnd(reader, "request");
        return new BedrockItemStackRequestPacket(requests);
    }

    internal static byte[] Encode(BedrockItemStackResponsePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return Encode(writer =>
        {
            writer.WriteUInt32VarInt(checked((uint)packet.Responses.Length));
            foreach (BedrockItemStackResponse response in packet.Responses)
                WriteResponse(writer, response);
        });
    }

    internal static BedrockItemStackResponsePacket DecodeResponse(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxResponsesPerPacket || count > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The item stack response count {count} exceeds its element limit.");
        var responses = new List<BedrockItemStackResponse>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            responses.Add(ReadResponse(ref reader, actualLimits));
        EnsureEnd(reader, "response");
        return new BedrockItemStackResponsePacket(responses);
    }

    private static void WriteRequest(BedrockWriter writer, BedrockItemStackRequest request)
    {
        writer.WriteZigZag32(request.RequestId);
        writer.WriteUInt32VarInt(checked((uint)request.Actions.Length));
        foreach (BedrockItemStackAction action in request.Actions)
            WriteAction(writer, action);
        writer.WriteUInt32VarInt(checked((uint)request.CustomNames.Length));
        foreach (string name in request.CustomNames)
            writer.WriteString(name);
        writer.WriteInt32LittleEndian(request.Cause);
    }

    private static BedrockItemStackRequest ReadRequest(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int requestId = reader.ReadZigZag32();
        uint actionCount = reader.ReadUInt32VarInt();
        if (actionCount > MaxActionsPerRequest || actionCount > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The item stack action count {actionCount} exceeds its element limit.");
        var actions = new List<BedrockItemStackAction>(checked((int)Math.Min(actionCount, 8)));
        for (uint index = 0; index < actionCount; index++)
            actions.Add(ReadAction(ref reader, limits));
        uint nameCount = reader.ReadUInt32VarInt();
        if (nameCount > MaxCustomNamesPerRequest || nameCount > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The item stack custom name count {nameCount} exceeds its element limit.");
        var names = new List<string>(checked((int)Math.Min(nameCount, 4)));
        for (uint index = 0; index < nameCount; index++)
            names.Add(reader.ReadString());
        int cause = reader.ReadInt32LittleEndian();
        return new BedrockItemStackRequest(requestId, actions, names, cause);
    }

    private static void WriteAction(BedrockWriter writer, BedrockItemStackAction action)
    {
        writer.WriteUInt32VarInt(checked((uint)action.Type));
        writer.WriteByte(action.LegacyTypeId);
        WritePayload(writer, action.Payload);
    }

    private static BedrockItemStackAction ReadAction(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        uint typeId = reader.ReadUInt32VarInt();
        byte legacyTypeId = reader.ReadByte();
        if (typeId > (uint)BedrockItemStackActionType.ResultsDeprecated || !Enum.IsDefined(typeof(BedrockItemStackActionType), (int)typeId))
            throw new BedrockCodecException($"The item stack action type {typeId} is not supported.");
        var type = (BedrockItemStackActionType)typeId;
        return new BedrockItemStackAction(type, legacyTypeId, ReadPayload(ref reader, type, limits));
    }

    private static void WritePayload(BedrockWriter writer, BedrockItemStackActionPayload payload)
    {
        switch (payload)
        {
            case BedrockTakePayload take:
                writer.WriteByte(take.Count);
                WriteSlot(writer, take.Source);
                WriteSlot(writer, take.Destination);
                break;
            case BedrockPlacePayload place:
                writer.WriteByte(place.Count);
                WriteSlot(writer, place.Source);
                WriteSlot(writer, place.Destination);
                break;
            case BedrockSwapPayload swap:
                WriteSlot(writer, swap.Source);
                WriteSlot(writer, swap.Destination);
                break;
            case BedrockDropPayload drop:
                writer.WriteByte(drop.Count);
                WriteSlot(writer, drop.Source);
                writer.WriteBoolean(drop.Randomly);
                break;
            case BedrockDestroyPayload destroy:
                writer.WriteByte(destroy.Count);
                WriteSlot(writer, destroy.Source);
                break;
            case BedrockConsumePayload consume:
                writer.WriteByte(consume.Count);
                WriteSlot(writer, consume.Source);
                break;
            case BedrockCreatePayload create:
                writer.WriteByte(create.ResultSlotId);
                break;
            case BedrockLabTableCombinePayload:
            case BedrockNonImplementedPayload:
                break;
            case BedrockBeaconPaymentPayload beacon:
                writer.WriteZigZag32(beacon.PrimaryEffect);
                writer.WriteZigZag32(beacon.SecondaryEffect);
                break;
            case BedrockMineBlockPayload mine:
                writer.WriteZigZag32(mine.HotbarSlot);
                writer.WriteZigZag32(mine.PredictedDurability);
                writer.WriteInt32LittleEndian(mine.NetworkId);
                break;
            case BedrockCraftRecipePayload recipe:
                writer.WriteUInt32VarInt(checked((uint)recipe.RecipeNetworkId));
                writer.WriteByte(recipe.TimesCrafted);
                break;
            case BedrockCraftRecipeAutoPayload auto:
                writer.WriteUInt32VarInt(checked((uint)auto.RecipeNetworkId));
                writer.WriteByte(auto.TimesCrafted);
                writer.WriteUInt32VarInt(checked((uint)auto.Ingredients.Length));
                foreach (BedrockRecipeIngredient ingredient in auto.Ingredients)
                    WriteIngredient(writer, ingredient);
                break;
            case BedrockCraftCreativePayload creative:
                writer.WriteUInt32VarInt(checked((uint)creative.ItemId));
                writer.WriteByte(creative.TimesCrafted);
                break;
            case BedrockOptionalPayload optional:
                writer.WriteUInt32VarInt(checked((uint)optional.RecipeNetworkId));
                writer.WriteInt32LittleEndian(optional.FilteredStringIndex);
                break;
            case BedrockCraftGrindstoneRequestPayload grindstone:
                writer.WriteInt32LittleEndian(grindstone.RecipeNetworkId);
                writer.WriteByte(grindstone.TimesCrafted);
                writer.WriteZigZag32(grindstone.Cost);
                break;
            case BedrockCraftLoomRequestPayload loom:
                writer.WriteString(loom.Pattern);
                writer.WriteByte(loom.TimesCrafted);
                break;
            case BedrockResultsDeprecatedPayload results:
                writer.WriteUInt32VarInt(checked((uint)results.ResultItems.Length));
                foreach (BedrockItemStackResultDescriptor descriptor in results.ResultItems)
                    WriteResultDescriptor(writer, descriptor);
                writer.WriteByte(results.TimesCrafted);
                break;
            default:
                throw new BedrockCodecException($"The item stack action payload {payload.GetType().Name} is not supported.");
        }
    }

    private static BedrockItemStackActionPayload ReadPayload(ref BedrockReader reader, BedrockItemStackActionType type, BedrockCodecLimits limits)
    {
        switch (type)
        {
            case BedrockItemStackActionType.Take:
                return new BedrockTakePayload(reader.ReadByte(), ReadSlot(ref reader), ReadSlot(ref reader));
            case BedrockItemStackActionType.Place:
                return new BedrockPlacePayload(reader.ReadByte(), ReadSlot(ref reader), ReadSlot(ref reader));
            case BedrockItemStackActionType.Swap:
                return new BedrockSwapPayload(ReadSlot(ref reader), ReadSlot(ref reader));
            case BedrockItemStackActionType.Drop:
                {
                    byte count = reader.ReadByte();
                    BedrockStackRequestSlot source = ReadSlot(ref reader);
                    return new BedrockDropPayload(count, source, reader.ReadBoolean());
                }
            case BedrockItemStackActionType.Destroy:
                return new BedrockDestroyPayload(reader.ReadByte(), ReadSlot(ref reader));
            case BedrockItemStackActionType.Consume:
                return new BedrockConsumePayload(reader.ReadByte(), ReadSlot(ref reader));
            case BedrockItemStackActionType.Create:
                return new BedrockCreatePayload(reader.ReadByte());
            case BedrockItemStackActionType.LabTableCombine:
                return BedrockLabTableCombinePayload.Instance;
            case BedrockItemStackActionType.BeaconPayment:
                return new BedrockBeaconPaymentPayload(reader.ReadZigZag32(), reader.ReadZigZag32());
            case BedrockItemStackActionType.MineBlock:
                return new BedrockMineBlockPayload(reader.ReadZigZag32(), reader.ReadZigZag32(), reader.ReadInt32LittleEndian());
            case BedrockItemStackActionType.CraftRecipe:
                return new BedrockCraftRecipePayload(ReadCountId(ref reader, "recipe"), reader.ReadByte());
            case BedrockItemStackActionType.CraftRecipeAuto:
                return new BedrockCraftRecipeAutoPayload(ReadCountId(ref reader, "recipe"), reader.ReadByte(), ReadIngredients(ref reader, limits));
            case BedrockItemStackActionType.CraftCreative:
                return new BedrockCraftCreativePayload(ReadCountId(ref reader, "creative item"), reader.ReadByte());
            case BedrockItemStackActionType.Optional:
                return new BedrockOptionalPayload(ReadCountId(ref reader, "recipe"), reader.ReadInt32LittleEndian());
            case BedrockItemStackActionType.CraftGrindstoneRequest:
                return new BedrockCraftGrindstoneRequestPayload(reader.ReadInt32LittleEndian(), reader.ReadByte(), reader.ReadZigZag32());
            case BedrockItemStackActionType.CraftLoomRequest:
                {
                    string pattern = reader.ReadString();
                    return new BedrockCraftLoomRequestPayload(pattern, reader.ReadByte());
                }
            case BedrockItemStackActionType.NonImplemented:
                return BedrockNonImplementedPayload.Instance;
            case BedrockItemStackActionType.ResultsDeprecated:
                return new BedrockResultsDeprecatedPayload(ReadResultDescriptors(ref reader, limits), reader.ReadByte());
            default:
                throw new BedrockCodecException($"The item stack action type {(int)type} is not supported.");
        }
    }

    private static void WriteSlot(BedrockWriter writer, BedrockStackRequestSlot slot)
    {
        WriteContainerName(writer, slot.Container);
        writer.WriteByte(slot.Slot);
        writer.WriteInt32LittleEndian(slot.StackId);
    }

    private static BedrockStackRequestSlot ReadSlot(ref BedrockReader reader) =>
        new(ReadContainerName(ref reader), reader.ReadByte(), reader.ReadInt32LittleEndian());

    private static void WriteContainerName(BedrockWriter writer, BedrockFullContainerName name)
    {
        writer.WriteByte(name.ContainerId);
        writer.WriteBoolean(name.DynamicContainerId.HasValue);
        if (name.DynamicContainerId.HasValue)
            writer.WriteUInt32VarInt(name.DynamicContainerId.Value);
    }

    private static BedrockFullContainerName ReadContainerName(ref BedrockReader reader) =>
        new(reader.ReadByte(), reader.ReadBoolean() ? reader.ReadUInt32VarInt() : null);

    private static void WriteIngredient(BedrockWriter writer, BedrockRecipeIngredient ingredient)
    {
        writer.WriteUInt32VarInt(checked((uint)ingredient.Kind));
        writer.WriteByte(ingredient.LegacyType);
        switch (ingredient.Kind)
        {
            case BedrockRecipeIngredientKind.Invalid:
                break;
            case BedrockRecipeIngredientKind.Name:
                writer.WriteString(ingredient.Name ?? throw new BedrockCodecException("The recipe ingredient name is missing."));
                writer.WriteZigZag32(ingredient.Metadata ?? throw new BedrockCodecException("The recipe ingredient metadata is missing."));
                break;
            case BedrockRecipeIngredientKind.Molang:
                writer.WriteString(ingredient.Expression ?? throw new BedrockCodecException("The recipe ingredient expression is missing."));
                writer.WriteInt16LittleEndian(ingredient.Version ?? throw new BedrockCodecException("The recipe ingredient version is missing."));
                break;
            case BedrockRecipeIngredientKind.ItemTag:
                writer.WriteString(ingredient.Tag ?? throw new BedrockCodecException("The recipe ingredient tag is missing."));
                break;
            default:
                throw new BedrockCodecException($"The recipe ingredient kind {(int)ingredient.Kind} is not supported.");
        }
    }

    private static BedrockRecipeIngredient ReadIngredient(ref BedrockReader reader)
    {
        uint kind = reader.ReadUInt32VarInt();
        byte legacyType = reader.ReadByte();
        if (kind > (uint)BedrockRecipeIngredientKind.ItemTag || !Enum.IsDefined(typeof(BedrockRecipeIngredientKind), (int)kind))
            throw new BedrockCodecException($"The recipe ingredient kind {kind} is not supported.");
        switch ((BedrockRecipeIngredientKind)kind)
        {
            case BedrockRecipeIngredientKind.Invalid:
                return new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Invalid, legacyType, null, null, null, null, null);
            case BedrockRecipeIngredientKind.Name:
                return new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Name, legacyType, reader.ReadString(), reader.ReadZigZag32(), null, null, null);
            case BedrockRecipeIngredientKind.Molang:
                {
                    string expression = reader.ReadString();
                    return new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Molang, legacyType, null, null, expression, reader.ReadInt16LittleEndian(), null);
                }
            case BedrockRecipeIngredientKind.ItemTag:
                return new BedrockRecipeIngredient(BedrockRecipeIngredientKind.ItemTag, legacyType, null, null, null, null, reader.ReadString());
            default:
                throw new BedrockCodecException($"The recipe ingredient kind {kind} is not supported.");
        }
    }

    private static ImmutableArray<BedrockRecipeIngredient> ReadIngredients(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxIngredientsPerAction || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The recipe ingredient count {count} exceeds its element limit.");
        var ingredients = new List<BedrockRecipeIngredient>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            ingredients.Add(ReadIngredient(ref reader));
        return [.. ingredients];
    }

    private static void WriteResultDescriptor(BedrockWriter writer, BedrockItemStackResultDescriptor descriptor)
    {
        writer.WriteUInt32VarInt(checked((uint)descriptor.Type));
        writer.WriteByte(descriptor.LegacyType);
        if (descriptor.Type != (int)BedrockResultDescriptorKind.Invalid)
        {
            writer.WriteString(descriptor.Name ?? throw new BedrockCodecException("The result descriptor name is missing."));
            writer.WriteZigZag32(descriptor.Metadata ?? throw new BedrockCodecException("The result descriptor metadata is missing."));
        }

        writer.WriteInt16LittleEndian(descriptor.Count);
        writer.WriteUInt32VarInt(descriptor.BlockRuntimeId);
        writer.WriteByteArray(descriptor.ExtraData);
    }

    private static BedrockItemStackResultDescriptor ReadResultDescriptor(ref BedrockReader reader)
    {
        uint rawType = reader.ReadUInt32VarInt();
        if (rawType > int.MaxValue)
            throw new BedrockCodecException($"The result descriptor type {rawType} exceeds its value limit.");
        int type = (int)rawType;
        byte legacyType = reader.ReadByte();
        string? name = null;
        int? metadata = null;
        if (type != (int)BedrockResultDescriptorKind.Invalid)
        {
            name = reader.ReadString();
            metadata = reader.ReadZigZag32();
        }

        short count = reader.ReadInt16LittleEndian();
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        byte[] extraData = reader.ReadByteArray();
        return new BedrockItemStackResultDescriptor(type, legacyType, name, metadata, count, blockRuntimeId, extraData);
    }

    private static ImmutableArray<BedrockItemStackResultDescriptor> ReadResultDescriptors(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxResultItemsPerAction || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The result item count {count} exceeds its element limit.");
        var descriptors = new List<BedrockItemStackResultDescriptor>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            descriptors.Add(ReadResultDescriptor(ref reader));
        return [.. descriptors];
    }

    private static void WriteResponse(BedrockWriter writer, BedrockItemStackResponse response)
    {
        writer.WriteByte(response.Status);
        writer.WriteZigZag32(response.RequestId);
        writer.WriteBoolean(true);
        writer.WriteBoolean(response.ContainersPresent);
        if (!response.ContainersPresent)
            return;
        writer.WriteUInt32VarInt(checked((uint)response.Containers.Length));
        foreach (BedrockItemStackResponseContainer container in response.Containers)
        {
            WriteContainerName(writer, container.SlotType);
            writer.WriteUInt32VarInt(checked((uint)container.Slots.Length));
            foreach (BedrockItemStackResponseSlot slot in container.Slots)
            {
                writer.WriteByte(slot.Slot);
                writer.WriteByte(slot.HotbarSlot);
                writer.WriteByte(slot.Count);
                writer.WriteBoolean(true);
                writer.WriteBoolean(slot.StackId.HasValue);
                if (slot.StackId.HasValue)
                    writer.WriteZigZag32(slot.StackId.Value);
                writer.WriteString(slot.CustomName);
                writer.WriteString(slot.FilteredCustomName);
                writer.WriteZigZag32(slot.DurabilityCorrection);
            }
        }
    }

    private static BedrockItemStackResponse ReadResponse(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        byte status = reader.ReadByte();
        int requestId = reader.ReadZigZag32();
        bool present = false;
        if (reader.ReadBoolean())
            present = reader.ReadBoolean();
        var containers = new List<BedrockItemStackResponseContainer>();
        if (present)
        {
            uint containerCount = reader.ReadUInt32VarInt();
            if (containerCount > MaxContainersPerResponse || containerCount > limits.MaxCollectionElements)
                throw new BedrockCodecException($"The item stack container count {containerCount} exceeds its element limit.");
            containers.Capacity = checked((int)Math.Min(containerCount, 8));
            for (uint index = 0; index < containerCount; index++)
                containers.Add(ReadResponseContainer(ref reader, limits));
        }

        return new BedrockItemStackResponse(status, requestId, present, containers);
    }

    private static BedrockItemStackResponseContainer ReadResponseContainer(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        BedrockFullContainerName slotType = ReadContainerName(ref reader);
        uint slotCount = reader.ReadUInt32VarInt();
        if (slotCount > MaxSlotsPerResponseContainer || slotCount > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The item stack slot count {slotCount} exceeds its element limit.");
        var slots = new List<BedrockItemStackResponseSlot>(checked((int)Math.Min(slotCount, 8)));
        for (uint index = 0; index < slotCount; index++)
        {
            byte slot = reader.ReadByte();
            byte hotbarSlot = reader.ReadByte();
            byte count = reader.ReadByte();
            int? stackId = reader.ReadBoolean() && reader.ReadBoolean() ? reader.ReadZigZag32() : null;
            string customName = reader.ReadString();
            string filteredCustomName = reader.ReadString();
            int durability = reader.ReadZigZag32();
            slots.Add(new BedrockItemStackResponseSlot(slot, hotbarSlot, count, stackId, customName, filteredCustomName, durability));
        }

        return new BedrockItemStackResponseContainer(slotType, slots);
    }

    private static byte[] Encode(Action<BedrockWriter> write)
    {
        var output = new ArrayBufferWriter<byte>();
        write(new BedrockWriter(output));
        return output.WrittenSpan.ToArray();
    }

    private static int ReadCountId(ref BedrockReader reader, string kind)
    {
        uint value = reader.ReadUInt32VarInt();
        if (value > int.MaxValue)
            throw new BedrockCodecException($"The item stack {kind} identifier {value} exceeds its value limit.");
        return (int)value;
    }

    private static void EnsureEnd(BedrockReader reader, string kind)
    {
        if (!reader.End)
            throw new BedrockCodecException($"The item stack {kind} payload has {reader.Remaining} unread bytes.");
    }
}
