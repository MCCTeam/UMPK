using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.World;

// Cell data states stay explicit so an unloaded or unknown cell can never answer as air.
internal enum BedrockCellAvailability
{
    Absent = 0,
    KnownAir = 1,
    Partial = 2,
    Complete = 3,
}

internal enum BedrockColumnAvailability
{
    Absent = 0,
    Partial = 1,
    Complete = 2,
}

// Layers stay independent so waterlogged and other multi-layer states keep both identities.
internal readonly record struct BedrockBlockLayers(BedrockBlockStateIdentity Primary, BedrockBlockStateIdentity? Secondary);

// Collision remains unavailable until the world store has qualified shape data.
internal readonly record struct BedrockBlockQuery(BedrockCellAvailability Availability, BedrockBlockLayers? Cell)
{
    internal bool CollisionAvailable => false;
}

// Bulk results count applied and refused edits separately so partial terrain never hides missing state.
internal readonly record struct BedrockSubChunkBlocksApplyResult(int Applied, int Refused);

// Snapshots carry counts and generations only; cell identities stay in the store behind bounded queries.
internal sealed record BedrockWorldSnapshot(
    int WorldGeneration,
    int ColumnCount,
    int SectionCount,
    long RetainedBytes,
    int BlockActorCount)
{
    internal bool CollisionAvailable => false;

    internal bool NavigationAllowed => false;
}
