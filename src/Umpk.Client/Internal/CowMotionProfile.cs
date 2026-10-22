using Umpk.Client.Actions;
using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The original 1.21.4/1.21.5 ordinary Cow listener and complete inherited tick profile.</summary>
internal readonly record struct CowMotionProfile(VanillaInterpolationProfile Interpolation, bool UsesPendingComponents)
{
    private static readonly Identifier Scale = Identifier.Minecraft("scale");
    private static readonly Identifier Player = Identifier.Minecraft("player");

    public static bool Supports(int protocol) => protocol is 769 or 770;

    public static bool Supports(Entity entity, int protocol) => Supports(protocol) && entity.Type.Id == Identifier.Minecraft("cow");

    public static CowMotionProfile ForProtocol(int protocol) => protocol switch
    {
        769 => new(VanillaInterpolationProfile.ClampedLinearStep, true),
        770 => new(VanillaInterpolationProfile.HandlerOffsetBox, false),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    public static bool IsKnownState(Entity entity, EntityGeometryRule rule)
    {
        if (entity.Pose != EntityPose.Standing || entity.Vehicle is not null || entity.Passengers.Count != 0
            || entity.Attributes.GetValueOrDefault(Scale, 1.0) != 1.0 || entity.Equipment.Any(pair => pair.Value is not null))
            return false;
        if (entity.Metadata.TryGet(rule.MetadataIndex, out MetadataValue baby) && (baby.Kind != MetadataValueKind.Boolean || baby.AsBoolean()))
            return false;
        if (entity.Metadata.TryGet(rule.PoseMetadataIndex, out MetadataValue pose) && (pose.Kind != MetadataValueKind.Pose || pose.AsPose() != EntityPose.Standing))
            return false;
        if (entity.Metadata.TryResolve(EntityMetadataKeys.Health, out MetadataValue health) && (health.Kind != MetadataValueKind.Float || health.AsFloat() != 10F))
            return false;
        return !entity.Metadata.TryResolve(EntityMetadataKeys.SharedFlags, out MetadataValue flags) || flags.Kind == MetadataValueKind.Byte && flags.AsByte() == 0;
    }

    public static Vec3d TickVelocity(Vec3d value, bool hadActiveInterpolation)
    {
        if (!hadActiveInterpolation)
            value = value.Scale(.98);
        return new Vec3d(Math.Abs(value.X) < .003 ? 0 : value.X, Math.Abs(value.Y) < .003 ? 0 : value.Y, Math.Abs(value.Z) < .003 ? 0 : value.Z);
    }

    /// <summary>Encloses every original player pose only to prove spatial nonrelevance when physics storage is disabled. This is not the player's actual body.</summary>
    public static bool TryLocalInfluenceBound(SelfState self, IEntityGeometrySource source, out Aabb bounds)
    {
        bounds = default;
        float scale = (float)self.Attributes.Value(Scale, 1.0);
        if (!float.IsFinite(scale) || scale <= 0 || !double.IsFinite(self.Position.X) || !double.IsFinite(self.Position.Y) || !double.IsFinite(self.Position.Z))
            return false;
        float width = 0, height = 0;
        // Both original Pose enums contain exactly these eighteen values; each Player dimensions fallback is present in the measured geometry source.
        for (int pose = 0; pose < 18; pose++)
        {
            if (!source.TryGetDimensions(Player, false, (EntityPose)pose, out EntityGeometryDimensions dimensions))
                return false;
            dimensions = LivingScaleProfile.Apply(dimensions, scale);
            width = Math.Max(width, dimensions.Width);
            height = Math.Max(height, dimensions.Height);
        }
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            return false;
        bounds = VanillaEntityBounds.Create(self.Position, width, height).Box;
        return true;
    }
}
