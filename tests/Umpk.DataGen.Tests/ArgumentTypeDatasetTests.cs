using Xunit;

namespace Umpk.DataGen.Tests;

/// <summary>The 1.13 bands' brigadier argument-type tables, pinned to the registration order of each version's own <c>ArgumentTypes</c> registry.</summary>
/// <remarks>
/// <para>Before 1.19 a command-tree argument node names its parser with a resource-location STRING, so on these bands the names are the wire and a numeric id has no protocol meaning.</para>
/// <para><c>minecraft:nbt_compound_tag</c> and <c>minecraft:nbt_tag</c> are the 1.14 split of 1.13's single <c>minecraft:nbt</c>, while <c>minecraft:time</c>, <c>minecraft:uuid</c> and <c>minecraft:angle</c> arrived at 1.14, 1.16 and 1.16.2.</para>
/// <para><c>minecraft:float_range</c> is registered even though no built-in 1.13.x command references it.</para>
/// <para>One table cannot serve all three bands: 1.13.1 inserted <c>minecraft:column_pos</c> after <c>block_pos</c> and appended <c>minecraft:dimension</c>, so 393 has 37 entries where 401 and 404 have 39. Protocols 401 and 404 are identical to each other.</para>
/// </remarks>
public sealed class ArgumentTypeDatasetTests
{
    /// <summary>1.13 (protocol 393): the brigadier five, then 32 minecraft parsers.</summary>
    private static readonly string[] V393 =
    [
        "brigadier:bool", "brigadier:float", "brigadier:double", "brigadier:integer", "brigadier:string",
        "minecraft:entity", "minecraft:game_profile", "minecraft:block_pos", "minecraft:vec3",
        "minecraft:vec2", "minecraft:block_state", "minecraft:block_predicate", "minecraft:item_stack",
        "minecraft:item_predicate", "minecraft:color", "minecraft:component", "minecraft:message",
        "minecraft:nbt", "minecraft:nbt_path", "minecraft:objective", "minecraft:objective_criteria",
        "minecraft:operation", "minecraft:particle", "minecraft:rotation", "minecraft:scoreboard_slot",
        "minecraft:score_holder", "minecraft:swizzle", "minecraft:team", "minecraft:item_slot",
        "minecraft:resource_location", "minecraft:mob_effect", "minecraft:function",
        "minecraft:entity_anchor", "minecraft:int_range", "minecraft:float_range",
        "minecraft:item_enchantment", "minecraft:entity_summon",
    ];

    /// <summary>1.13.1 / 1.13.2 (protocols 401 / 404): 393 plus column_pos and dimension.</summary>
    private static readonly string[] V401 =
    [
        "brigadier:bool", "brigadier:float", "brigadier:double", "brigadier:integer", "brigadier:string",
        "minecraft:entity", "minecraft:game_profile", "minecraft:block_pos", "minecraft:column_pos",
        "minecraft:vec3", "minecraft:vec2", "minecraft:block_state", "minecraft:block_predicate",
        "minecraft:item_stack", "minecraft:item_predicate", "minecraft:color", "minecraft:component",
        "minecraft:message", "minecraft:nbt", "minecraft:nbt_path", "minecraft:objective",
        "minecraft:objective_criteria", "minecraft:operation", "minecraft:particle", "minecraft:rotation",
        "minecraft:scoreboard_slot", "minecraft:score_holder", "minecraft:swizzle", "minecraft:team",
        "minecraft:item_slot", "minecraft:resource_location", "minecraft:mob_effect", "minecraft:function",
        "minecraft:entity_anchor", "minecraft:int_range", "minecraft:float_range",
        "minecraft:item_enchantment", "minecraft:entity_summon", "minecraft:dimension",
    ];

    public static TheoryData<int, string[]> Bands => new()
    {
        { 393, V393 },
        { 401, V401 },
        { 404, V401 },
    };

    [Theory]
    [MemberData(nameof(Bands))]
    public void The_1_13_argument_type_table_is_the_versions_own_registry(int protocol, string[] expected)
    {
        IReadOnlyList<RegistryItem> actual = Load(protocol);

        Assert.Equal(expected.Length, actual.Count);
        Assert.Equal(expected, actual.Select(a => a.Name).ToArray());

        // Two consumers assume the ids are a contiguous 0..n-1 index. They are NOT wire ids on this era; the parser is named by string until 1.19.
        Assert.Equal(Enumerable.Range(0, expected.Length), actual.Select(a => a.Id));
    }

    /// <summary>Parsers introduced after 1.13 must remain absent from these tables.</summary>
    [Theory]
    [InlineData("minecraft:nbt_compound_tag")]  // 1.14 split of minecraft:nbt
    [InlineData("minecraft:nbt_tag")]           // 1.14 split of minecraft:nbt
    [InlineData("minecraft:time")]              // 1.14
    [InlineData("minecraft:uuid")]              // 1.16
    [InlineData("minecraft:angle")]             // 1.16.2
    public void No_1_13_band_carries_a_later_eras_parser(string parser)
    {
        foreach (int protocol in new[] { 393, 401, 404 })
            Assert.DoesNotContain(parser, Load(protocol).Select(a => a.Name));

    }

    /// <summary><c>minecraft:nbt</c> is present on every band, while the two 1.13.1 additions are absent on 393.</summary>
    [Fact]
    public void The_1_13_specific_entries_are_right()
    {
        foreach (int protocol in new[] { 393, 401, 404 })
            Assert.Contains("minecraft:nbt", Load(protocol).Select(a => a.Name));

        string[] on393 = [.. Load(393).Select(a => a.Name)];
        Assert.DoesNotContain("minecraft:column_pos", on393);
        Assert.DoesNotContain("minecraft:dimension", on393);

        Assert.Equal(Load(401).Select(a => a.Name), Load(404).Select(a => a.Name));
    }

    private static IReadOnlyList<RegistryItem> Load(int protocol) =>
        DatasetLoader.Load(LocateDataRoot()).ByProtocol[protocol].ArgumentTypes;

    private static string LocateDataRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null && !Directory.Exists(Path.Combine(cursor.FullName, "data", "java")))
            cursor = cursor.Parent;

        if (cursor is null)
            throw new DirectoryNotFoundException("could not locate data/java from the test output dir");

        return Path.Combine(cursor.FullName, "data", "java");
    }
}
