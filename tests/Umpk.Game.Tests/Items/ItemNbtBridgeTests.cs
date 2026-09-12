using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Xunit;

namespace Umpk.Game.Tests.Items;

public class ItemNbtBridgeTests
{
    private static LegacyItemBridge Bridge() =>
        new(new FakeItemBridgeSource(), ItemTestData.Enchantments);

    private static DataComponentMap MapFrom(NbtCompound root)
    {
        var result = Bridge().ToComponents("V1_8", root);
        return DataComponentMap.Create(new Dictionary<DataComponentType, object>(), result.Patch);
    }

    [Fact]
    public void MapsDisplayName_ToCustomName()
    {
        var root = new NbtCompound();
        var display = new NbtCompound();
        display.PutString("Name", "Sword of Testing");
        root.Put("display", display);

        var map = MapFrom(root);
        Assert.True(map.TryGet(DataComponents.CustomName, out var name));
        Assert.Equal("Sword of Testing", name.Name.ToPlainText());
    }

    [Fact]
    public void MapsDisplayLore_ToLore()
    {
        var root = new NbtCompound();
        var display = new NbtCompound();
        var lore = new NbtList(NbtTagType.String);
        lore.Add(new NbtString("line one"));
        lore.Add(new NbtString("line two"));
        display.Put("Lore", lore);
        root.Put("display", display);

        var map = MapFrom(root);
        Assert.True(map.TryGet(DataComponents.Lore, out var value));
        Assert.Equal(2, value.Lines.Count);
        Assert.Equal("line two", value.Lines[1].ToPlainText());
    }

    [Fact]
    public void MapsDamageAndUnbreakable()
    {
        var root = new NbtCompound();
        root.PutInt("Damage", 55);
        root.PutBool("Unbreakable", true);

        var map = MapFrom(root);
        Assert.True(map.TryGet(DataComponents.Damage, out var dmg));
        Assert.Equal(55, dmg.Value);
        Assert.True(map.Has(DataComponents.Unbreakable));
    }

    [Fact]
    public void MapsNumericEnchantments_UsingWireLayoutTable()
    {
        var root = new NbtCompound();
        var ench = new NbtList(NbtTagType.Compound);
        var entry = new NbtCompound();
        entry.PutShort("id", 16); // sharpness in the V1_8 table
        entry.PutShort("lvl", 3);
        ench.Add(entry);
        root.Put("ench", ench);

        var map = MapFrom(root);
        Assert.True(map.TryGet(DataComponents.Enchantments, out var enchantments));
        Assert.Single(enchantments.Enchantments);
        Assert.Equal(Identifier.Minecraft("sharpness"), enchantments.Enchantments[0].Enchantment.Id);
        Assert.Equal(3, enchantments.Enchantments[0].Level);
    }

    [Fact]
    public void UnrecognizedNbt_IsRetainedInLegacyEscapeHatch()
    {
        var root = new NbtCompound();
        root.PutInt("CustomModelData", 7);
        root.PutString("SomeModField", "value");

        var map = MapFrom(root);
        Assert.True(map.TryGet(DataComponents.LegacyNbt, out var residual));
        Assert.True(residual.Nbt.ContainsKey("CustomModelData"));
        Assert.True(residual.Nbt.ContainsKey("SomeModField"));
    }

    [Fact]
    public void RoundTrip_ThroughComponentsAndBack_PreservesWellKnownKeys()
    {
        var root = new NbtCompound();
        root.PutInt("Damage", 12);
        root.PutBool("Unbreakable", true);
        var display = new NbtCompound();
        display.PutString("Name", "Named");
        root.Put("display", display);
        var ench = new NbtList(NbtTagType.Compound);
        var e = new NbtCompound();
        e.PutShort("id", 34); // unbreaking
        e.PutShort("lvl", 2);
        ench.Add(e);
        root.Put("ench", ench);

        var bridge = Bridge();
        var result = bridge.ToComponents("V1_8", root);
        var map = DataComponentMap.Create(new Dictionary<DataComponentType, object>(), result.Patch);
        var rebuilt = bridge.ToLegacyNbt("V1_8", map);

        Assert.Equal(12, rebuilt.GetInt("Damage"));
        Assert.True(rebuilt.GetBool("Unbreakable"));
        Assert.Equal("Named", rebuilt.GetCompound("display")!.GetString("Name"));
        var rebuiltEnch = rebuilt.GetList("ench")!;
        Assert.Single(rebuiltEnch);
        Assert.Equal(34, ((NbtCompound)rebuiltEnch[0]).GetShort("id"));
        Assert.Equal(2, ((NbtCompound)rebuiltEnch[0]).GetShort("lvl"));
    }

    [Fact]
    public void UnknownWireLayout_DoesNotResolveNumericEnchantment()
    {
        // Numeric id 16 resolves to sharpness under the V1_8 table (MapsNumericEnchantments_UsingEraTable). Under an era the source has no table for, TryMapEnchantmentId returns false, so no enchantment resolves: the era string is load-bearing, not ignored.
        var root = new NbtCompound();
        var ench = new NbtList(NbtTagType.Compound);
        var entry = new NbtCompound();
        entry.PutShort("id", 16);
        entry.PutShort("lvl", 3);
        ench.Add(entry);
        root.Put("ench", ench);

        var result = Bridge().ToComponents("V1_UNKNOWN", root);
        var map = DataComponentMap.Create(new Dictionary<DataComponentType, object>(), result.Patch);

        Assert.True(map.TryGet(DataComponents.Enchantments, out var enchantments));
        Assert.Empty(enchantments.Enchantments);
    }

    [Fact]
    public void ResidualNbt_SurvivesRoundTrip()
    {
        var root = new NbtCompound();
        root.PutInt("CustomModelData", 99);

        var bridge = Bridge();
        var result = bridge.ToComponents("V1_8", root);
        var map = DataComponentMap.Create(new Dictionary<DataComponentType, object>(), result.Patch);
        var rebuilt = bridge.ToLegacyNbt("V1_8", map);

        Assert.Equal(99, rebuilt.GetInt("CustomModelData"));
    }
}
