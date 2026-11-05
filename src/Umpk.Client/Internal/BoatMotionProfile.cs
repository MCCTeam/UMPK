using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The finite canonical, unmounted boat family; local control, block effects and peer displacement need their own proofs.</summary>
internal static class BoatMotionProfile
{
    public const float Width = 1.375F;
    public const float Height = .5625F;

    public static bool Supports(int protocol) => protocol is 770 or 771;

    public static bool Supports(Entity entity, int protocol)
        => Supports(protocol) && IsFamily(entity.Type.Id)
            && JavaGameData.Registries(protocol).EntityTypes.TryGet(entity.Type.NetworkId, out RegistryEntry<EntityTypeDefinition> canonical)
            && canonical == entity.Type;

    public static bool IsFamily(Identifier id) => id.IsMinecraft && id.Path is
        "acacia_boat" or "acacia_chest_boat" or "bamboo_raft" or "bamboo_chest_raft"
        or "birch_boat" or "birch_chest_boat" or "cherry_boat" or "cherry_chest_boat"
        or "dark_oak_boat" or "dark_oak_chest_boat" or "jungle_boat" or "jungle_chest_boat"
        or "mangrove_boat" or "mangrove_chest_boat" or "oak_boat" or "oak_chest_boat"
        or "pale_oak_boat" or "pale_oak_chest_boat" or "spruce_boat" or "spruce_chest_boat";

    public static VanillaInterpolationProfile Interpolation(int protocol) => protocol switch
    {
        770 => VanillaInterpolationProfile.HandlerOffsetBox,
        771 => VanillaInterpolationProfile.Handler,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    public static bool IsKnownState(Entity entity)
    {
        if (entity.Vehicle is not null || entity.Passengers.Count != 0 || entity.LeashHolder is not null
            || entity.Pose != EntityPose.Standing || entity.Attributes.Count != 0 || entity.Equipment.Any(pair => pair.Value is not null)
            || !Finite(entity.Position) || !Finite(entity.Velocity) || !float.IsFinite(entity.Yaw) || !float.IsFinite(entity.Pitch))
            return false;
        if (entity.Metadata.TryResolve(EntityMetadataKeys.SharedFlags, out MetadataValue flags) && (flags.Kind != MetadataValueKind.Byte || flags.AsByte() != 0))
            return false;
        if (entity.Metadata.TryResolve(EntityMetadataKeys.NoGravity, out MetadataValue gravity) && (gravity.Kind != MetadataValueKind.Boolean || gravity.AsBoolean()))
            return false;
        return !entity.Metadata.TryResolve(EntityMetadataKeys.Pose, out MetadataValue pose) || pose.Kind == MetadataValueKind.Pose && pose.AsPose() == EntityPose.Standing;
    }

    private static bool Finite(Vec3d value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    internal sealed class GeometrySource : IEntityGeometrySource
    {
        public static GeometrySource Instance { get; } = new();

        public bool TryGetRule(Identifier entityType, out EntityGeometryRule rule)
        {
            rule = new(-1, -1, EntityDimensionsRefresh.Recenter, null);
            return IsFamily(entityType);
        }

        public bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions)
        {
            dimensions = new(Width, Height, false, true);
            return IsFamily(entityType);
        }
    }
}
