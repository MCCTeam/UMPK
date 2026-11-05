using Umpk.Game.Registries;

namespace Umpk.Client.Internal;

/// <summary>Native ordinary-living scale refresh policy. Ranged attribute resolution clamps the double before this profile narrows it to float.</summary>
internal readonly record struct LivingScaleProfile(bool UsesDirtyAttributes, bool KeepsRespawnBaseValue)
{
    public static bool Supports(int protocol) => protocol is >= 766 and <= 777;

    public static LivingScaleProfile ForProtocol(int protocol)
    {
        if (!Supports(protocol))
            throw new ArgumentOutOfRangeException(nameof(protocol));
        return new LivingScaleProfile(protocol >= 770, protocol >= 767);
    }

    public bool NeedsRefresh(float appliedScale, float scale, bool dirty) => UsesDirtyAttributes ? dirty : appliedScale != scale;

    public static EntityGeometryDimensions Apply(EntityGeometryDimensions dimensions, float scale)
        => dimensions.Fixed || scale == 1F ? dimensions : dimensions with { Width = dimensions.Width * scale, Height = dimensions.Height * scale };
}
