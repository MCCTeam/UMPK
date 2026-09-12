using Umpk.Game.Players;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Players;

public sealed class PlayersStateTests
{
    private static GameProfile Profile(string name) => new(Guid.NewGuid(), name);

    [Fact]
    public void TabList_Upsert_Update_Remove()
    {
        var list = new TabList();
        GameProfile profile = Profile("alice");
        var entry = new TabListEntry(profile) { GameMode = GameMode.Survival, Latency = 40 };

        list.Upsert(entry);
        Assert.Equal(1, list.Count);
        Assert.True(list.TryGet(profile.Id, out TabListEntry? got));

        // per-field update mutates the tracked entry
        got.Latency = 120;
        got.DisplayName = Component.Text("Alice!");
        got.Listed = false;
        Assert.True(list.TryGet(profile.Id, out TabListEntry? again));
        Assert.Equal(120, again.Latency);
        Assert.Equal("Alice!", again.DisplayName!.ToPlainText());
        Assert.False(again.Listed);

        Assert.True(list.Remove(profile.Id));
        Assert.Equal(0, list.Count);
        Assert.False(list.TryGet(profile.Id, out _));
    }

    [Fact]
    public void TabList_Upsert_Same_Uuid_Replaces()
    {
        var list = new TabList();
        GameProfile profile = Profile("bob");
        list.Upsert(new TabListEntry(profile) { Latency = 10 });
        list.Upsert(new TabListEntry(profile) { Latency = 99 });

        Assert.Equal(1, list.Count);
        Assert.True(list.TryGet(profile.Id, out TabListEntry? entry));
        Assert.Equal(99, entry.Latency);
    }

    [Fact]
    public void TabList_Header_Footer_And_Clear()
    {
        var list = new TabList();
        list.Header = Component.Text("Welcome");
        list.Footer = Component.Text("Bye");
        list.Upsert(new TabListEntry(Profile("x")));

        list.Clear();
        Assert.Null(list.Header);
        Assert.Null(list.Footer);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void BossBar_Add_Update_Remove()
    {
        var state = new BossBarState();
        var uuid = Guid.NewGuid();
        state.Add(new BossBar(uuid, Component.Text("Boss"), 1.0f, BossBarColor.Red, BossBarOverlay.Notched10, BossBarFlags.DarkenScreen));

        Assert.True(state.TryGet(uuid, out BossBar? bar));
        Assert.Equal(1.0f, bar.Progress);

        bar.SetProgress(0.5f);
        bar.Color = BossBarColor.Blue;
        bar.Title = Component.Text("Half");
        Assert.Equal(0.5f, bar.Progress);
        Assert.Equal(BossBarColor.Blue, bar.Color);
        Assert.Equal("Half", bar.Title.ToPlainText());

        Assert.True(state.Remove(uuid));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void BossBar_Progress_Clamped()
    {
        var bar = new BossBar(Guid.NewGuid(), Component.Text("B"), 5f, BossBarColor.Pink, BossBarOverlay.Progress, BossBarFlags.None);
        Assert.Equal(1.0f, bar.Progress);
        bar.SetProgress(-2f);
        Assert.Equal(0.0f, bar.Progress);
    }

    [Fact]
    public void Map_Region_Update_Writes_Pixels()
    {
        var state = new MapState();
        MapData map = state.GetOrCreate(1);
        map.Scale = 2;
        map.Locked = true;

        // Fill a 2x2 region at (10, 20).
        byte[] region = [11, 12, 13, 14];
        map.UpdateRegion(10, 20, 2, 2, region);

        Assert.Equal(11, map.GetColor(10, 20));
        Assert.Equal(12, map.GetColor(11, 20));
        Assert.Equal(13, map.GetColor(10, 21));
        Assert.Equal(14, map.GetColor(11, 21));
        // Untouched pixel stays 0.
        Assert.Equal(0, map.GetColor(0, 0));
        Assert.Equal(2, map.Scale);
        Assert.True(map.Locked);
    }

    [Fact]
    public void Map_Icons_Replaced_Wholesale()
    {
        var map = new MapData(2);
        map.SetIcons([new MapIcon(1, 0, 0, 8, null)]);
        Assert.Single(map.Icons);
        map.SetIcons([new MapIcon(2, 5, 5, 0, Component.Text("Home")), new MapIcon(3, -5, -5, 4, null)]);
        Assert.Equal(2, map.Icons.Count);
    }

    [Fact]
    public void Map_Region_Out_Of_Bounds_Throws()
    {
        var map = new MapData(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.UpdateRegion(127, 0, 2, 1, new byte[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.UpdateRegion(0, 0, 2, 2, new byte[3]));
    }

    [Fact]
    public void MapState_GetOrCreate_Idempotent()
    {
        var state = new MapState();
        MapData first = state.GetOrCreate(5);
        MapData second = state.GetOrCreate(5);
        Assert.Same(first, second);
        Assert.Equal(1, state.Count);
    }
}
