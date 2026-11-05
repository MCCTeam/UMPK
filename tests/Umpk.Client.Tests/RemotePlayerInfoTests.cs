using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RemotePlayerInfoTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("protocol").GetInt32(), release.GetProperty("version").GetString()!, release.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void LookupAndCacheLifetimeMatchEveryNativeQuery(int protocol, string release, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out var version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        var state = new EntityMotionState();
        var players = new TabList();
        Entity actor = Player(protocol);
        var first = Entry(actor, GameMode.Survival);
        var second = Entry(actor, GameMode.Spectator);
        var third = Entry(actor, GameMode.Survival);
        state.RegisterRemotePlayer(actor, protocol);
        foreach (JsonElement row in document.RootElement.GetProperty("cases")[caseIndex].EnumerateArray())
        {
            switch (row.GetProperty("operation").GetString())
            {
                case "missing": break;
                case "add_survival": players.Upsert(first); break;
                case "replace_with_spectator": players.Upsert(second); break;
                case "remove_current": players.Remove(actor.Uuid); break;
                case "mutate_detached_first": first.GameMode = GameMode.Spectator; break;
                case "add_new_survival": players.Upsert(third); break;
                case "fresh_actor":
                    state.Remove(actor);
                    actor = Player(protocol, actor.Uuid);
                    state.RegisterRemotePlayer(actor, protocol);
                    break;
                case "mutate_current_to_spectator": third.GameMode = GameMode.Spectator; break;
                case "fresh_actor_missing":
                    players.Clear();
                    state.Remove(actor);
                    actor = Player(protocol, actor.Uuid);
                    state.RegisterRemotePlayer(actor, protocol);
                    break;
                case "retry_missing_with_spectator": players.Upsert(second); break;
                default: throw new InvalidOperationException("Unmeasured native player-info operation");
            }
            Assert.Equal(row.GetProperty("spectator").GetBoolean(), state.IsSpectator(actor, players));
        }
    }

    [Theory]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(770)]
    [InlineData(777)]
    public void RemovingOrClearingTrackedPlayersDropsOnlyTheirOwnedCache(int protocol)
    {
        var state = new EntityMotionState();
        var players = new TabList();
        Entity actor = Player(protocol);
        players.Upsert(Entry(actor, GameMode.Spectator));
        state.RegisterRemotePlayer(actor, protocol);
        Assert.True(state.IsSpectator(actor, players));
        state.Remove(actor);
        Assert.False(state.IsSpectator(actor, players));
        players.Upsert(Entry(actor, GameMode.Survival));
        Entity replacement = Player(protocol, actor.Uuid);
        state.RegisterRemotePlayer(replacement, protocol);
        Assert.False(state.IsSpectator(replacement, players));
        state.Clear();
        players.Upsert(Entry(actor, GameMode.Spectator));
        state.RegisterRemotePlayer(replacement, protocol);
        Assert.True(state.IsSpectator(replacement, players));
    }

    [Fact]
    public void ATabListUuidDoesNotTurnAnotherEntityTypeIntoAPlayer()
    {
        Assert.True(JavaGameData.Registries(777).EntityTypes.TryGet(Identifier.Minecraft("villager"), out RegistryEntry<EntityTypeDefinition> type));
        var actor = new Entity(42, Guid.NewGuid(), type);
        var players = new TabList();
        players.Upsert(Entry(actor, GameMode.Spectator));
        Assert.False(new EntityMotionState().IsSpectator(actor, players));
    }

    [Theory]
    [InlineData(498)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(777)]
    public async Task UnknownInfoUpdatesDoNotPopulateTheNativePlayerCache(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var harness = new ApplierHarness(version);
        Entity actor = Player(protocol);
        harness.State.EntityMotion.RegisterRemotePlayer(actor, protocol);
        await ApplyInfo(harness, protocol, actor.Uuid, "Ignored", GameMode.Spectator, add: false);
        Assert.Empty(harness.State.TabList.Entries);
        Assert.False(harness.State.EntityMotion.IsSpectator(actor, harness.State.TabList));
        await ApplyInfo(harness, protocol, actor.Uuid, "KnownPeer", GameMode.Survival, add: true);
        Assert.False(harness.State.EntityMotion.IsSpectator(actor, harness.State.TabList));
        Assert.Equal("KnownPeer", Assert.Single(harness.State.TabList.Entries).Profile.Name);
    }

    public static IEnumerable<object[]> DuplicateAddCases()
    {
        foreach (object[] release in NativeReleases())
            foreach (bool changedProfile in new[] { false, true })
                yield return [release[0], release[1], changedProfile];
    }

    [Theory]
    [MemberData(nameof(DuplicateAddCases))]
    public async Task DuplicateAddsFollowTheNativeInfoObjectLifetime(int protocol, string release, bool changedProfile)
    {
        Assert.True(JavaVersions.TryGetByName(release, out var version));
        Assert.Equal(protocol, version.Version.Protocol);
        var harness = new ApplierHarness(version);
        Entity actor = Player(protocol);
        harness.State.EntityMotion.RegisterRemotePlayer(actor, protocol);
        await ApplyInfo(harness, protocol, actor.Uuid, "FirstProfile", GameMode.Survival, add: true);
        TabListEntry first = Assert.Single(harness.State.TabList.Entries);
        Assert.False(harness.State.EntityMotion.IsSpectator(actor, harness.State.TabList));
        string nextName = changedProfile ? "OtherProfile" : "FirstProfile";
        await ApplyInfo(harness, protocol, actor.Uuid, nextName, GameMode.Spectator, add: true);
        TabListEntry current = Assert.Single(harness.State.TabList.Entries);
        if (protocol >= 760)
            Assert.Same(first, current);
        else
            Assert.NotSame(first, current);
        Assert.Equal(protocol >= 760 ? "FirstProfile" : nextName, current.Profile.Name);
        Assert.Equal(GameMode.Spectator, current.GameMode);
        Assert.True(harness.State.EntityMotion.IsSpectator(actor, harness.State.TabList));
    }

    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    public async Task RepeatedLegacyAddsKeepConstructorPayloadOnlyInTheRetainedInfoEra(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var harness = new ApplierHarness(version);
        Guid id = Guid.NewGuid();
        await ApplyKey(1);
        object first = Assert.Single(harness.State.TabList.Entries).ChatSession!;
        await ApplyKey(2);
        object second = Assert.Single(harness.State.TabList.Entries).ChatSession!;
        if (protocol >= 760)
            Assert.Same(first, second);
        else
            Assert.NotSame(first, second);
        Assert.Equal(new byte[] { protocol >= 760 ? (byte)1 : (byte)2 }, Assert.IsType<ProfilePublicKeyData>(second).KeyDer);

        ValueTask ApplyKey(byte marker) => harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "player_info", new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer, [new LegacyPlayerListEntry(id, "KeyPeer", [], 0, 0, null) { ProfileKey = new ProfilePublicKeyData(1000, [marker], [marker]) }])));
    }

    private static ValueTask ApplyInfo(ApplierHarness harness, int protocol, Guid id, string name, GameMode mode, bool add)
    {
        object packet = protocol < 761
            ? BoundDescriptorCodec.RoundTrip(protocol, "player_info", new ClientboundLegacyPlayerListItemPacket(add ? LegacyPlayerListAction.AddPlayer : LegacyPlayerListAction.UpdateGameMode, [new(id, name, [], (int)mode, 0, null)]))
            : BoundDescriptorCodec.RoundTrip(protocol, "player_info_update", new ClientboundPlayerInfoUpdatePacket((add ? PlayerInfoActions.AddPlayer : PlayerInfoActions.None) | PlayerInfoActions.UpdateGameMode, [new(id, name, [], false, null, mode, false, 0, null, 0, true)]));
        return harness.ApplyAsync(packet);
    }

    private static Entity Player(int protocol, Guid? id = null)
    {
        Assert.True(JavaGameData.Registries(protocol).EntityTypes.TryGet(Identifier.Minecraft("player"), out RegistryEntry<EntityTypeDefinition> type));
        return new Entity(42, id ?? Guid.NewGuid(), type);
    }

    private static TabListEntry Entry(Entity actor, GameMode mode) => new(new GameProfile(actor.Uuid, "GeometryPeer")) { GameMode = mode, Listed = false };

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(RemotePlayerInfoTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.PlayerInfoLookup.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
