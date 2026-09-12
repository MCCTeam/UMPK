using Xunit;

namespace Umpk.Commands.Tests;

/// <summary><see cref="Arguments.RegistryId"/>: parsing is identifier-only and registry-blind (see the method's own doc for why), while suggestions are drawn from whatever <see cref="IRegistrySuggestionSource"/> the command source resolves, using the protocol's colon-aware resource filtering rule.</summary>
public sealed class RegistryArgumentTests
{
    private static readonly Identifier ItemRegistry = Identifier.Minecraft("item");
    private static readonly Identifier BlockRegistry = Identifier.Minecraft("block");

    private static async Task<(bool Success, Identifier Value)> RunAsync(string input, Identifier registryId)
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        Identifier captured = default;
        scope.Register(b => b.Literal("cmd", n =>
            n.ThenArgument("arg", Arguments.RegistryId(registryId), a =>
                a.Executes(ctx =>
                {
                    captured = ctx.GetArgument<Identifier>("arg");
                    return new ValueTask<int>(1);
                }))));

        var result = await service.ExecuteAsync("cmd " + input, new TestSource());
        return (result.Success, captured);
    }

    private static async Task<List<string>> SuggestAsync(string input, Identifier registryId, TestSource source)
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("give", n =>
            n.ThenArgument("item", Arguments.RegistryId(registryId), a =>
                a.Executes(_ => new ValueTask<int>(1)))));

        var result = await service.CompleteAsync(input, input.Length, source);
        return result.Suggestions.Select(s => s.Text).ToList();
    }

    [Fact]
    public async Task A_bare_id_parses_into_the_minecraft_namespace()
    {
        var (ok, value) = await RunAsync("stone", ItemRegistry);

        Assert.True(ok);
        Assert.Equal("minecraft", value.Namespace);
        Assert.Equal("stone", value.Path);
    }

    [Fact]
    public async Task A_namespaced_id_parses_verbatim()
    {
        var (ok, value) = await RunAsync("modded:widget", ItemRegistry);

        Assert.True(ok);
        Assert.Equal("modded", value.Namespace);
        Assert.Equal("widget", value.Path);
    }

    [Fact]
    public async Task An_id_absent_from_the_registry_still_parses()
    {
        // No IRegistrySuggestionSource is attached at all, so there is nothing to validate against - and parsing does not even try: Arguments.RegistryId never rejects a well-formed id.
        var (ok, value) = await RunAsync("nonexistent_item", ItemRegistry);

        Assert.True(ok);
        Assert.Equal("minecraft:nonexistent_item", value.ToString());
    }

    [Fact]
    public async Task Suggestions_come_from_the_registry_the_argument_names()
    {
        var source = new TestSource().AddService<IRegistrySuggestionSource>(
            new FakeRegistrySource(new Dictionary<Identifier, Identifier[]>
            {
                [ItemRegistry] = [Identifier.Minecraft("stone"), Identifier.Minecraft("dirt")],
            }));

        var texts = await SuggestAsync("give ", ItemRegistry, source);

        Assert.Contains("minecraft:stone", texts);
        Assert.Contains("minecraft:dirt", texts);
    }

    [Fact]
    public async Task Suggestions_are_empty_when_the_source_offers_no_registry_lookup()
    {
        var texts = await SuggestAsync("give ", ItemRegistry, new TestSource());

        Assert.Empty(texts);
    }

    [Fact]
    public async Task Suggestions_match_after_an_underscore_like_vanilla()
    {
        var source = new TestSource().AddService<IRegistrySuggestionSource>(
            new FakeRegistrySource(new Dictionary<Identifier, Identifier[]>
            {
                [ItemRegistry] = [Identifier.Minecraft("diamond_sword"), Identifier.Minecraft("stone")],
            }));

        // "sword" is not a prefix of "diamond_sword", but it IS a prefix starting right after the '_' - vanilla's MATCH_SPLITTER rule, ported by SuggestionMatching.MatchesSubStr.
        var texts = await SuggestAsync("give sword", ItemRegistry, source);

        Assert.Contains("minecraft:diamond_sword", texts);
        Assert.DoesNotContain("minecraft:stone", texts);
    }

    [Fact]
    public async Task Suggestions_match_a_namespace_prefix()
    {
        var source = new TestSource().AddService<IRegistrySuggestionSource>(
            new FakeRegistrySource(new Dictionary<Identifier, Identifier[]>
            {
                [ItemRegistry] = [new Identifier("modded", "widget"), Identifier.Minecraft("stone")],
            }));

        var texts = await SuggestAsync("give mod", ItemRegistry, source);

        Assert.Contains("modded:widget", texts);
        Assert.DoesNotContain("minecraft:stone", texts);
    }

    [Fact]
    public async Task A_typed_colon_matches_the_whole_id_only()
    {
        var source = new TestSource().AddService<IRegistrySuggestionSource>(
            new FakeRegistrySource(new Dictionary<Identifier, Identifier[]>
            {
                [ItemRegistry] = [Identifier.Minecraft("stone"), new Identifier("modded", "stonecutter")],
            }));

        // Typing "minecraft:st" contains a colon, so vanilla's colon-aware branch matches the whole id.ToString() only - the namespace/path split used for a bare token no longer applies, so "modded:stonecutter" (whose PATH starts with "st") is not offered even though it would match under the no-colon rule.
        var texts = await SuggestAsync("give minecraft:st", ItemRegistry, source);

        Assert.Contains("minecraft:stone", texts);
        Assert.DoesNotContain("modded:stonecutter", texts);
    }

    [Fact]
    public async Task Two_registry_arguments_suggest_from_different_registries()
    {
        var source = new TestSource().AddService<IRegistrySuggestionSource>(
            new FakeRegistrySource(new Dictionary<Identifier, Identifier[]>
            {
                [ItemRegistry] = [Identifier.Minecraft("stone")],
                [BlockRegistry] = [Identifier.Minecraft("dirt")],
            }));

        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("give_item", n =>
            n.ThenArgument("item", Arguments.RegistryId(ItemRegistry), a =>
                a.Executes(_ => new ValueTask<int>(1)))));
        scope.Register(b => b.Literal("give_block", n =>
            n.ThenArgument("block", Arguments.RegistryId(BlockRegistry), a =>
                a.Executes(_ => new ValueTask<int>(1)))));

        var itemResult = await service.CompleteAsync("give_item ", "give_item ".Length, source);
        var blockResult = await service.CompleteAsync("give_block ", "give_block ".Length, source);
        var itemTexts = itemResult.Suggestions.Select(s => s.Text).ToList();
        var blockTexts = blockResult.Suggestions.Select(s => s.Text).ToList();

        Assert.Contains("minecraft:stone", itemTexts);
        Assert.DoesNotContain("minecraft:dirt", itemTexts);
        Assert.Contains("minecraft:dirt", blockTexts);
        Assert.DoesNotContain("minecraft:stone", blockTexts);
    }

    private sealed class FakeRegistrySource(Dictionary<Identifier, Identifier[]> entries) : IRegistrySuggestionSource
    {
        public IEnumerable<Identifier> GetEntries(Identifier registryId) =>
            entries.TryGetValue(registryId, out var ids) ? ids : [];
    }
}
