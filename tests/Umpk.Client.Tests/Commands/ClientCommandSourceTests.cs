using Umpk.Client.Commands;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary><see cref="ClientCommandSource.GetService{T}"/> resolving <see cref="IRegistrySuggestionSource"/>: the client-side seam that lets an <see cref="Arguments.RegistryId"/> argument suggest real registry entries from a live session, through <see cref="RegistrySuggestionSource"/>.</summary>
public sealed class ClientCommandSourceTests
{
    private static UmpkClient BuildClient() => new UmpkClientBuilder()
        .UseVersion(JavaVersions.V1_21_5)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .Build();

    [Fact]
    public async Task The_client_source_resolves_a_registry_suggestion_source()
    {
        await using UmpkClient client = BuildClient();
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);

        var registrySource = source.GetService<IRegistrySuggestionSource>();

        Assert.NotNull(registrySource);
    }

    [Fact]
    public async Task Item_ids_are_enumerated_for_a_pre_flattening_session()
    {
        await using UmpkClient client = BuildClient();
        client.State.Registries = JavaGameData.Registries(47);
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);
        var registrySource = source.GetService<IRegistrySuggestionSource>()!;

        var ids = registrySource.GetEntries(RegistryIds.Item).ToList();

        // A literal, known id - not merely "the list is non-empty" - proves the pre-flattening composite (id<<16)|damage network keys still resolve to the SAME canonical Identifier a modern session would use.
        Assert.Contains(Identifier.Minecraft("stone"), ids);
    }

    [Fact]
    public async Task Entity_type_ids_are_enumerated_for_a_modern_session()
    {
        await using UmpkClient client = BuildClient();
        client.State.Registries = JavaGameData.Registries(766);
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);
        var registrySource = source.GetService<IRegistrySuggestionSource>()!;

        var ids = registrySource.GetEntries(RegistryIds.EntityType).ToList();

        Assert.Contains(Identifier.Minecraft("zombie"), ids);
    }

    [Fact]
    public async Task An_unknown_registry_id_yields_no_candidates()
    {
        await using UmpkClient client = BuildClient();
        client.State.Registries = JavaGameData.Registries(766);
        var source = new ClientCommandSource(client, (_, _) => ValueTask.CompletedTask);
        var registrySource = source.GetService<IRegistrySuggestionSource>()!;

        var ids = registrySource.GetEntries(new Identifier("umpk", "not_a_real_registry")).ToList();

        Assert.Empty(ids);
    }
}
