using Umpk.Game.Entities;

namespace Umpk.Client.Internal;

/// <summary>Binds the original 1.11 Horse-family placement enclosure without claiming exact geometry, picking or locomotion.</summary>
internal static class LegacyHorsePlacementEnvelope
{
    private const int Protocol = 315;
    private const int EnclosurePadding = 2;

    public static LegacyPlacementEnvelope? Create(Entity entity, int protocol)
        => protocol == Protocol && entity.Type.Id.IsMinecraft
            && entity.Type.Id.Path is "horse" or "skeleton_horse" or "zombie_horse" or "donkey" or "mule" or "llama"
            ? new(entity.Position, Protocol, MetadataIsKnown, EnclosurePadding) : null;

    // Original xl/sj uses 1.3964844F by 1.6F for five variants and .5 baby scaling; retained-minimum growth reaches +1.04736328125 horizontally. Llama xp configures .9F by 1.87F and can retain its earlier horse-constructor body until metadata refresh. Both families, repeated age toggles and inherited <1/4096 interpolation error fit strictly inside the outward two-block enclosure.
    private static bool MetadataIsKnown(Entity entity)
    {
        if (!Known(12, MetadataValueKind.Boolean) || !Known(13, MetadataValueKind.Byte) || !Known(14, MetadataValueKind.OptionalUuid))
            return false;
        return entity.Type.Id.Path switch
        {
            "horse" => Known(15, MetadataValueKind.VarInt) && Known(16, MetadataValueKind.VarInt),
            "donkey" or "mule" => Known(15, MetadataValueKind.Boolean),
            "llama" => Known(15, MetadataValueKind.Boolean) && Known(16, MetadataValueKind.VarInt)
                && Known(17, MetadataValueKind.VarInt) && Known(18, MetadataValueKind.VarInt),
            "skeleton_horse" or "zombie_horse" => true,
            _ => false,
        };

        bool Known(int index, MetadataValueKind kind)
            => !entity.Metadata.TryGet(index, out MetadataValue value) || value.Kind == kind;
    }
}
