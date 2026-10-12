using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Maps stable player physical indices to their qualified request namespaces without rebasing main-inventory indices.</summary>
internal static class BedrockPlayerInventoryAddressing
{
    internal const int MaxPlayerSlot = 35;
    internal const int HotbarLastSlot = 8;

    internal static bool TryGetRequestContainer(int physicalSlot, out BedrockFullContainerName name)
    {
        name = default;
        if (physicalSlot is < 0 or > MaxPlayerSlot)
            return false;
        name = new BedrockFullContainerName(physicalSlot <= HotbarLastSlot ? (byte)28 : (byte)29, null);
        return true;
    }

    internal static bool IsSupportedPhysicalSlot(int physicalSlot, int actualCapacity) =>
        physicalSlot is >= 0 and <= MaxPlayerSlot && physicalSlot < actualCapacity;
}
