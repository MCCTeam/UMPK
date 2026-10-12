using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Carries the client-owned request identifier together with the encoded protocol-2169 outbound payload for one admitted inventory request.</summary>
internal readonly record struct BedrockItemStackSubmitResult(int RequestId, byte[] OutboundPayload);

/// <summary>Builds the single-action protocol-2169 item-stack request for a client-owned negative request identifier.</summary>
/// <remarks>Builder scope covers stable player physical slots 0 through 35 for take, place, and swap; Session admission and runtime acceptance remain independently qualified. Creative and drop request builders remain disabled.</remarks>
internal static class BedrockItemStackEgress
{
    /// <summary>Identifies the hotbar container in the protocol-2169 full container name for slots inside the supported hotbar range.</summary>
    internal const byte HotbarContainerId = 28;
    /// <summary>Identifies the inventory container in the protocol-2169 full container name for supported player slots 9 through 35.</summary>
    internal const byte InventoryContainerId = 29;
    /// <summary>Bounds the supported hotbar slot window to slots 0 through 8 as client support policy rather than proven server acceptance.</summary>
    internal const int MaxHotbarSlot = BedrockPlayerInventoryAddressing.HotbarLastSlot;
    /// <summary>Carries the protocol-2169 legacy take identifier used by the single supported take shape.</summary>
    internal const byte SupportedLegacyTakeId = 0;
    /// <summary>Carries the protocol-2169 legacy place identifier used by the single supported place shape.</summary>
    internal const byte SupportedLegacyPlaceId = 1;
    /// <summary>Carries the protocol-2169 legacy swap identifier used by the single supported swap shape.</summary>
    internal const byte SupportedLegacySwapId = 2;
    /// <summary>Carries the protocol-2169 cause value used by every client-built item-stack request.</summary>
    internal const int SupportedCause = 0;
    /// <summary>Carries the single client-owned negative request identifier admitted while only one inventory request stays outstanding.</summary>
    internal const int ClientRequestId = -1;

    /// <summary>Selects the qualified request namespace for a supported player physical slot without rebasing its index.</summary>
    internal static byte ContainerForSlot(byte slot)
    {
        if (!BedrockPlayerInventoryAddressing.TryGetRequestContainer(slot, out BedrockFullContainerName name))
            throw new InvalidOperationException($"The item stack slot {slot} is outside the supported player range.");
        return name.ContainerId;
    }

    /// <summary>Builds one single-action protocol-2169 place request after rejecting non-negative identifiers, out-of-range or equal slots, and zero counts.</summary>
    /// <remarks>The builder also checks the configured action budget before creating a request.</remarks>
    internal static BedrockItemStackRequestPacket BuildPlace(int requestId, byte sourceSlot, byte destinationSlot, byte count, int sourceStackId, int destinationStackId)
    {
        if (requestId >= 0)
            throw new InvalidOperationException($"The item stack request identifier {requestId} is not client owned. Client requests use negative identifiers.");
        if (sourceSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack source slot {sourceSlot} is outside the supported player range.");
        if (destinationSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack destination slot {destinationSlot} is outside the supported player range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The item stack source and destination slots must differ.");
        if (count == 0)
            throw new InvalidOperationException("The item stack transfer count must be positive.");
        if (1 > BedrockItemStackCodecs.MaxActionsPerRequest)
            throw new InvalidOperationException("The item stack action budget is not configured.");

        var source = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(sourceSlot), null), sourceSlot, sourceStackId);
        var destination = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(destinationSlot), null), destinationSlot, destinationStackId);
        var action = new BedrockItemStackAction(BedrockItemStackActionType.Place, SupportedLegacyPlaceId, new BedrockPlacePayload(count, source, destination));
        return new BedrockItemStackRequestPacket([new BedrockItemStackRequest(requestId, [action], [], SupportedCause)]);
    }

    /// <summary>Builds one single-action protocol-2169 take request after rejecting non-negative identifiers, out-of-range or equal slots, and zero counts.</summary>
    /// <remarks>The builder also checks the configured action budget before creating a request.</remarks>
    internal static BedrockItemStackRequestPacket BuildTake(int requestId, byte sourceSlot, byte destinationSlot, byte count, int sourceStackId, int destinationStackId)
    {
        if (requestId >= 0)
            throw new InvalidOperationException($"The item stack request identifier {requestId} is not client owned. Client requests use negative identifiers.");
        if (sourceSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack source slot {sourceSlot} is outside the supported player range.");
        if (destinationSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack destination slot {destinationSlot} is outside the supported player range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The item stack source and destination slots must differ.");
        if (count == 0)
            throw new InvalidOperationException("The item stack transfer count must be positive.");
        if (1 > BedrockItemStackCodecs.MaxActionsPerRequest)
            throw new InvalidOperationException("The item stack action budget is not configured.");

        var source = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(sourceSlot), null), sourceSlot, sourceStackId);
        var destination = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(destinationSlot), null), destinationSlot, destinationStackId);
        var action = new BedrockItemStackAction(BedrockItemStackActionType.Take, SupportedLegacyTakeId, new BedrockTakePayload(count, source, destination));
        return new BedrockItemStackRequestPacket([new BedrockItemStackRequest(requestId, [action], [], SupportedCause)]);
    }

    /// <summary>Builds one single-action protocol-2169 swap request after rejecting non-negative identifiers and out-of-range or equal slots.</summary>
    internal static BedrockItemStackRequestPacket BuildSwap(int requestId, byte sourceSlot, byte destinationSlot, int sourceStackId, int destinationStackId)
    {
        if (requestId >= 0)
            throw new InvalidOperationException($"The item stack request identifier {requestId} is not client owned. Client requests use negative identifiers.");
        if (sourceSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack source slot {sourceSlot} is outside the supported player range.");
        if (destinationSlot > BedrockPlayerInventoryAddressing.MaxPlayerSlot)
            throw new InvalidOperationException($"The item stack destination slot {destinationSlot} is outside the supported player range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The item stack source and destination slots must differ.");

        var source = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(sourceSlot), null), sourceSlot, sourceStackId);
        var destination = new BedrockStackRequestSlot(new BedrockFullContainerName(ContainerForSlot(destinationSlot), null), destinationSlot, destinationStackId);
        var action = new BedrockItemStackAction(BedrockItemStackActionType.Swap, SupportedLegacySwapId, new BedrockSwapPayload(source, destination));
        return new BedrockItemStackRequestPacket([new BedrockItemStackRequest(requestId, [action], [], SupportedCause)]);
    }
}
