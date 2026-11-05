using Umpk.Game.Entities;

namespace Umpk.Client.Internal;

/// <summary>Binds the separately proven 1.11 Rabbit enclosure, without claiming exact geometry, picking or locomotion.</summary>
internal static class LegacyRabbitPlacementEnvelope
{
    private const int Protocol = 315;

    public static LegacyPlacementEnvelope? Create(Entity entity, int protocol)
        => protocol == Protocol && entity.Type.Id.IsMinecraft && entity.Type.Id.Path == "rabbit"
            ? new(entity.Position, Protocol, MetadataIsKnown) : null;

    // Original xd/sj uses adult .4F by .5F and Boolean child scaling .5. Shrink recenters and growth retains its minimum; its greatest positive horizontal offset is .30000000447034836 and its height is .5, below the unchanged outward one-block enclosure. RabbitType is VarInt, independent of Wolf byte flags.
    private static bool MetadataIsKnown(Entity entity)
        => (!entity.Metadata.TryGet(12, out MetadataValue age) || age.Kind == MetadataValueKind.Boolean)
            && (!entity.Metadata.TryGet(13, out MetadataValue type) || type.Kind == MetadataValueKind.VarInt);
}
