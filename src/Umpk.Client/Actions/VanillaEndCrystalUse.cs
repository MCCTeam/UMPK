using Umpk.Game.Items;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>The original End Crystal item rule and its entity-query index boundaries, after the separately owned controller phases.</summary>
internal readonly record struct VanillaEndCrystalUse(int Protocol, bool HasRules, bool TwoReplaceableCells, bool SectionStorage, bool AsymmetricSectionSearch, bool UsesComponents)
{
    // Native 1.9–1.12.2 uses two air-or-isReplaceable cells; 1.13 uses only above.isAir. 1.17 replaces chunk entity lists with accessible sections; 1.19 changes vertical search grace from ±2 to −4/+0. 26.3 SUCCESS has local predicted swing.
    public static VanillaEndCrystalUse ForProtocol(int protocol) => new(protocol, protocol is >= 107 and <= 777, protocol < 393, protocol >= 755, protocol >= 759, protocol >= 766);

    public bool Supports(Identifier item) => HasRules && item == Identifier.Minecraft("end_crystal");

    public bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        // Legacy NBT does not change the Survival item method. Patched Adventure predicates and modern effective components require their own facts.
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (UsesComponents || !entry.Type.Equals(DataComponents.LegacyNbt))
                return false;
        return true;
    }

    public bool IncludesSection(in Aabb query, int x, int y, int z)
    {
        int minY = Section(query.MinY - (AsymmetricSectionSearch ? 4 : 2));
        int maxY = Section(query.MaxY + (AsymmetricSectionSearch ? 0 : 2));
        if (!SectionStorage)
        {
            minY = Math.Clamp(minY, 0, 15);
            maxY = Math.Clamp(maxY, 0, 15);
        }
        return x >= Section(query.MinX - 2) && x <= Section(query.MaxX + 2)
            && y >= minY && y <= maxY
            && z >= Section(query.MinZ - 2) && z <= Section(query.MaxZ + 2);
    }

    public static bool TryGetSection(Vec3d position, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)
            || position.X is <= int.MinValue or >= int.MaxValue || position.Y is <= int.MinValue or >= int.MaxValue || position.Z is <= int.MinValue or >= int.MaxValue)
            return false;
        x = Section(position.X);
        y = Section(position.Y);
        z = Section(position.Z);
        return true;
    }

    public static bool HasFiniteBounds(in Aabb bounds)
        => double.IsFinite(bounds.MinX) && double.IsFinite(bounds.MinY) && double.IsFinite(bounds.MinZ)
            && double.IsFinite(bounds.MaxX) && double.IsFinite(bounds.MaxY) && double.IsFinite(bounds.MaxZ);

    private static int Section(double coordinate) => (int)Math.Floor(coordinate / 16);
}
