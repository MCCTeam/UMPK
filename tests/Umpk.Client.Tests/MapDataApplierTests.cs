using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The map applier must write decoded pixels and icons through <see cref="MapData.UpdateRegion"/> and <see cref="MapData.SetIcons"/> rather than merely creating a blank 128x128 map grid. The tests read concrete pixel and icon values back from the model; map existence alone is not sufficient.</summary>
public sealed class MapDataApplierTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    [Fact]
    public async Task MapItemData_Writes_Pixels_Icons_Scale_And_Lock()
    {
        var harness = new ApplierHarness(Version);

        // A 3x2 patch at (5, 7) with six distinct, non-zero colours.
        byte[] colors = [11, 22, 33, 44, 55, 66];
        var patch = new MapPatch(Columns: 3, Rows: 2, StartX: 5, StartY: 7, Colors: colors);
        var icon = new MapIcon(Type: 4, X: -12, Z: 33, Rotation: 9, DisplayName: Component.Text("Home"));

        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            MapId: 42, Scale: 3, Locked: true, Icons: [icon], Patch: patch, null));

        Assert.True(harness.State.Maps.TryGet(42, out MapData? map));
        Assert.Equal(3, map!.Scale);
        Assert.True(map.Locked);

        // The icon, field by field.
        MapIcon stored = Assert.Single(map.Icons);
        Assert.Equal(4, stored.Type);
        Assert.Equal(-12, stored.X);
        Assert.Equal(33, stored.Z);
        Assert.Equal(9, stored.Rotation);
        Assert.Equal("Home", stored.DisplayName?.ToPlainText());

        // Every pixel of the patch, at its exact map coordinate, row-major over the sub-region.
        Assert.Equal(11, map.GetColor(5, 7));
        Assert.Equal(22, map.GetColor(6, 7));
        Assert.Equal(33, map.GetColor(7, 7));
        Assert.Equal(44, map.GetColor(5, 8));
        Assert.Equal(55, map.GetColor(6, 8));
        Assert.Equal(66, map.GetColor(7, 8));

        // Pixels outside the patch are untouched.
        Assert.Equal(0, map.GetColor(4, 7));
        Assert.Equal(0, map.GetColor(8, 8));
        Assert.Equal(0, map.GetColor(5, 6));
    }

    [Fact]
    public async Task MapItemData_Second_Patch_Merges_Into_The_Same_Grid()
    {
        // Vanilla sends a map as a sequence of column patches; the model must accumulate them rather than replace, or a plugin reading the grid sees only the last strip the server sent.
        var harness = new ApplierHarness(Version);

        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            7, Scale: 0, Locked: false, Icons: null,
            Patch: new MapPatch(1, 1, StartX: 0, StartY: 0, Colors: [90]), null));
        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            7, Scale: 0, Locked: false, Icons: null,
            Patch: new MapPatch(1, 1, StartX: 127, StartY: 127, Colors: [91]), null));

        Assert.True(harness.State.Maps.TryGet(7, out MapData? map));
        Assert.Equal(90, map!.GetColor(0, 0));
        Assert.Equal(91, map.GetColor(127, 127));
    }

    [Fact]
    public async Task MapItemData_Absent_Icon_List_Leaves_Existing_Icons_Alone()
    {
        // From 1.9 the decoration list is optional and its absence means "unchanged". Replacing with an empty list on every pixel update would erase the decorations a plugin is tracking.
        var harness = new ApplierHarness(Version);
        var icon = new MapIcon(1, 2, 3, 4, DisplayName: null);

        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            5, Scale: 1, Locked: false, Icons: [icon], Patch: default, null));
        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            5, Scale: 1, Locked: false, Icons: null,
            Patch: new MapPatch(1, 1, 10, 10, [77]), null));

        Assert.True(harness.State.Maps.TryGet(5, out MapData? map));
        Assert.Single(map!.Icons);
        Assert.Equal(77, map.GetColor(10, 10));
    }

    [Fact]
    public async Task MapItemData_Malformed_Patch_Is_Dropped_Without_Throwing()
    {
        // A hostile or corrupt frame must not take the applier down: the region is validated against the 128x128 grid and against its own colour-array length before it is written.
        var harness = new ApplierHarness(Version);

        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            9, Scale: 0, Locked: false, Icons: null,
            Patch: new MapPatch(Columns: 4, Rows: 4, StartX: 126, StartY: 126, Colors: new byte[16]), null));
        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            9, Scale: 0, Locked: false, Icons: null,
            Patch: new MapPatch(Columns: 2, Rows: 2, StartX: 0, StartY: 0, Colors: [1]), null));

        Assert.True(harness.State.Maps.TryGet(9, out MapData? map));
        Assert.Equal(0, map!.GetColor(126, 126));
        Assert.Equal(0, map.GetColor(0, 0));
    }

    [Fact]
    public async Task MapItemData_Still_Publishes_MapDataReceived()
    {
        var harness = new ApplierHarness(Version);
        int received = 0;
        harness.Events.Subscribe<MapDataReceived>(e =>
        {
            if (e.MapId == 3)
                received++;

        });

        await harness.ApplyAsync(new ClientboundMapItemDataPacket(
            3, Scale: 2, Locked: false, Icons: null,
            Patch: new MapPatch(1, 1, 0, 0, [5]), null));

        Assert.Equal(1, received);
    }
}
