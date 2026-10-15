using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class ItemAttackDatasetTests
{
    [Theory]
    [InlineData("schema")]
    [InlineData("missing-item")]
    [InlineData("unknown-item")]
    [InlineData("unavailable-component")]
    public void InvalidAttackDefaultsAreRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        VersionData version = DatasetLoader.Load(builder.Root).ByProtocol[770];
        JsonObject items = [];
        foreach (RegistryItem item in version.Items)
            items[item.Name] = new JsonObject { ["piercing_weapon"] = null, ["minimum_attack_charge"] = null };
        string first = version.Items[0].Name;
        JsonObject measurement = new() { ["_provenance"] = new JsonObject { ["kind"] = "extracted" }, ["schema"] = 1, ["items"] = items };
        switch (defect)
        {
            case "schema": measurement["schema"] = 2; break;
            case "missing-item": items.Remove(first); break;
            case "unknown-item": items["minecraft:missing"] = new JsonObject { ["piercing_weapon"] = null, ["minimum_attack_charge"] = null }; break;
            case "unavailable-component": items[first]!["minimum_attack_charge"] = 1.0F; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770", "item-attack.json"), measurement.ToJsonString());
        Assert.Contains(Validator.Validate(DatasetLoader.Load(builder.Root)), problem => problem.Contains("item-attack.json", StringComparison.Ordinal));
    }

    [Fact]
    public void MeasuredAttackDefaultsReceiveAGeneratedTable()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        VersionData version = DatasetLoader.Load(builder.Root).ByProtocol[770];
        JsonObject items = [];
        foreach (RegistryItem item in version.Items)
            items[item.Name] = new JsonObject { ["piercing_weapon"] = null, ["minimum_attack_charge"] = null };
        JsonObject measurement = new() { ["_provenance"] = new JsonObject { ["kind"] = "extracted" }, ["schema"] = 1, ["items"] = items };
        File.WriteAllText(Path.Combine(builder.Root, "770", "item-attack.json"), measurement.ToJsonString());
        var files = new Emitter(DatasetLoader.Load(builder.Root)).Emit();
        Assert.Contains("ItemAttackDefaults => SharedTables.", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemAttackDefaults =>", files["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("770 => global::Umpk.Data.Java.V770.Descriptor.ItemAttackDefaults,", files["JavaGameData.Dispatch.g.cs"].Text, StringComparison.Ordinal);
    }
}
