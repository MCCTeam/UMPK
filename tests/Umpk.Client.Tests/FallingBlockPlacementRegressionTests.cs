using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class FallingBlockPlacementRegressionTests
{
    [Fact]
    public async Task InitialQueuedFallersUseNativeHashSetGrowthAndTenEntryReentry()
    {
        await using var harness = new MotionHarness();
        int[] ids = [1, 17, 2, 18, 3, 19, 4, 20, 5, 21, 6, 22, 7];
        int[] firstPhase = [1, 17, 2, 18, 3, 19, 4, 5, 6, 7];
        foreach (int id in ids)
            await harness.Spawn(id, 101);
        await harness.Column();
        await harness.Tick();
        foreach (int id in ids)
            Assert.Equal(firstPhase.Contains(id), harness.State.EntityMembership.IsWorldQueryMember(harness.State.Entities.Get(id)!));
        await harness.Tick();
        foreach (int id in ids)
        {
            Entity actor = harness.State.Entities.Get(id)!;
            Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.Equal(NativeY[2][firstPhase.Contains(id) ? 1 : 0], actor.Position.Y);
        }
    }

    [Fact]
    public async Task ExplicitObservedPositionDoesNotLeaveAStaleFallingBox()
    {
        await using var harness = new MotionHarness();
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity actor = harness.State.Entities.Get(42)!;
        actor.Position = new Vec3d(4.5, 101, 1.5);
        Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(107), out Aabb box, out _, 107));
        Assert.Equal(4.5 - .98F / 2F, box.MinX);
        Assert.Equal(101, box.MinY);
    }

    // Original 1.9 listener, World traversal, FallingBlock tick and inherited move; native observations SHA-256 b297f3d92ec64464fe3ec1fb0c5c9de13fd72b3ef503643570a9ca8f5cf9e07d.
    private static readonly double[][] NativeY =
    [
        [101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0],
        [30.009999990463257, 29.969999991357327, 29.890799992364645, 29.773183992735266, 29.6179203117492, 29.4257619023155, 29.197446658299416, 28.933697715702955, 28.635223747821883, 28.30271925449956, 27.936864845595736, 27.538327518785938, 27.107760931804908],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.89079999236465, 100.89079999236465, 100.89079999236465, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187],
        [101.00999999046326, 101.00999999046326, 101.00999999046326, 101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 29.969999991357327, 29.890799992364645, 29.773183992735266, 29.6179203117492, 29.4257619023155, 29.197446658299416, 28.933697715702955, 28.635223747821883, 28.30271925449956, 27.936864845595736],
        [-64.49000000953674, -64.53000000864267, -64.60920000763535, -64.72681600726473, -64.8820796882508, -65.0742380976845, -65.30255334170059, -65.56630228429705, -65.86477625217813, -66.19728074550045, -66.56313515440428, -66.96167248121408, -67.39223906819511],
        [320.50999999046326, 320.4699999913573, 320.39079999236463, 320.27318399273526, 320.1179203117492, 319.92576190231546, 319.6974466582994, 319.4336977157029, 319.13522374782184, 318.8027192544995, 318.43686484559566, 318.0383275187859, 317.6077609318049],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
        [101.00999999046326, 100.96999999135733, 100.89079999236465, 100.77318399273527, 100.6179203117492, 100.4257619023155, 100.19744665829941, 99.93369771570295, 99.63522374782187, 99.30271925449955, 98.93686484559572, 98.53832751878592, 98.10776093180489],
    ];
    private static readonly double[][] NativeVelocityY =
    [
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.07761600052344796, -0.07761600052344796, -0.07761600052344796, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911],
        [0.0, 0.0, 0.0, 0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
        [0.0, -0.03919999988675116, -0.07761600052344796, -0.11526368188013789, -0.15215841032776656, -0.18831524491015375, -0.2237489434905301, -0.25847396877514306, -0.2925044942163911, -0.3258544097978949, -0.35853732770386776, -0.3905665878750984, -0.42195526345381407],
    ];
    private static readonly bool[][] NativeClear =
    [
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [false, false, false, false, false, false, false, true, true, true, true, true, true],
        [false, false, false, false, false, false, false, true, true, true, true, true, true],
        [false, false, false, false, false, false, false, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [false, false, false, true, true, true, false, false, false, false, false, true, true],
        [true, true, true, false, false, false, false, false, false, false, true, true, true],
        [false, false, false, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [false, false, false, false, false, false, false, true, true, true, true, true, true],
        [false, false, false, true, true, true, true, true, true, true, true, true, true],
        [false, false, true, true, true, true, true, true, true, true, true, true, true],
        [false, false, true, true, true, true, true, true, true, true, true, true, true],
        [false, false, false, false, false, false, false, true, true, true, true, true, true],
    ];

    private static readonly bool[][] NativeMember =
    [
        [false, false, false, false, false, false, false, false, false, false, false, false, false],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, false, false, false, true, true, true, true, true, true, true],
        [false, false, false, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, false, false, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
        [true, true, true, true, true, true, true, true, true, true, true, true, true],
    ];

    private static readonly double[][] NativeX =
    [
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 17.5, 17.5, 17.5, 17.5, 17.5, 17.5, 17.5, 17.5, 17.5, 17.5],
        [1.5, 2.0, 2.490000009536743, 2.97020002822876, 3.440796055706025, 3.901980171609651, 4.353940613991594, 4.79686185614636, 5.230924681906082, 5.656306259429701, 6.073180213516357, 6.48171669647252, 6.882082457561775],
        [1.5, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568],
        [1.5, 1.375, 1.2524999976158142, 1.13244999294281, 1.0148009860734937, 0.899504957097587, 0.7865148465021014, 0.6757845359634099, 0.5672688295234793, 0.4609234351425745, 0.35670494662091035, 0.2545708258818695, 0.15447938560955587],
    ];

    private static readonly double[][] NativeZ =
    [
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5],
        [1.5, 2.25, 2.9850000143051147, 3.70530004234314, 4.411194083559037, 5.102970257414476, 5.780910920987391, 6.445292784219539, 7.096387022859123, 7.734459389144551, 8.359770320274535, 8.97257504470878, 9.573123686342662],
        [1.5, 2.25, 2.9850000143051147, 3.70530004234314, 4.411194083559037, 5.102970257414476, 5.780910920987391, 6.445292784219539, 7.096387022859123, 7.734459389144551, 8.359770320274535, 8.97257504470878, 9.573123686342662],
        [1.5, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568, 1.5099999904632568],
    ];

    private static readonly double[][] NativeVelocityX =
    [
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.49000000953674316, 0.4802000186920168, 0.4705960274772649, 0.4611841159036265, 0.4519604423819429, 0.44292124215476536, 0.4340628257597223, 0.42538157752361927, 0.41687395408665656, 0.40853648295616307, 0.4003657610892548, 0.3923584535038406],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, -0.12250000238418579, -0.1200500046730042, -0.11764900686931623, -0.11529602897590663, -0.11299011059548572, -0.11073031053869134, -0.10851570643993058, -0.10634539438090482, -0.10421848852166414, -0.10213412073904077, -0.1000914402723137, -0.09808961337596014],
    ];

    private static readonly double[][] NativeVelocityZ =
    [
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        [0.0, 0.7350000143051147, 0.7203000280380252, 0.7058940412158974, 0.6917761738554398, 0.6779406635729144, 0.6643818632321481, 0.6510942386395835, 0.6380723662854288, 0.6253109311299848, 0.6128047244342446, 0.6005486416338822, 0.5885376802557609],
        [0.0, 0.7350000143051147, 0.7203000280380252, 0.7058940412158974, 0.6917761738554398, 0.6779406635729144, 0.6643818632321481, 0.6510942386395835, 0.6380723662854288, 0.6253109311299848, 0.6128047244342446, 0.6005486416338822, 0.5885376802557609],
        [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
    ];

    private static ClientboundLevelChunkPacket ColumnPacket(ApplierHarness applier, bool neighbor = false)
    {
        using var native = LegacyPlayerMembershipRuntimeTests.ReadNative();
        int index = (int)LegacyPlayerMembershipRuntimeTests.Protocols().Single(row => (int)row[1] == 107)[2];
        var binding = native.RootElement.GetProperty("cases")[index];
        var selected = binding.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == (neighbor ? 7 : 1));
        var captured = neighbor ? selected.GetProperty("packets").EnumerateArray().Single(row => row.GetProperty("action").GetString() == "crossing_full") : selected.GetProperty("packets")[0];
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(LegacyPlayerMembershipRuntimeTests.DecodeNative(applier, 107, binding, captured));
        Assert.True(packet.FullChunk);
        Assert.Equal(new ChunkPos(neighbor ? 1 : 0, 0), packet.Column.Position);
        return packet;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task SessionTicksFollowOriginalFallingBlockMotionAndMembership(int scene)
    {
        await using var harness = new MotionHarness();
        if (scene is not (0 or 6))
            await harness.Column();
        if (scene == 3)
            await harness.Block(new BlockPos(1, 99, 1), "stone");
        if (scene == 10)
            await harness.Block(new BlockPos(1, 101, 1), "sand");
        if (scene == 13)
            for (int y = 100; y <= 105; y++)
                for (int z = 0; z <= 4; z++)
                    await harness.Block(new BlockPos(2, y, z), "stone");
        if (scene == 14)
            for (int y = 100; y <= 105; y++)
                for (int x = 0; x <= 3; x++)
                    await harness.Block(new BlockPos(x, y, 2), "stone");
        double startY = scene switch { 4 => 30, 8 => -64.5, 9 => 320.5, _ => 101 };
        await harness.Spawn(42, startY);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.Equal(Vec3d.Zero, actor.Velocity);
        Assert.Equal(NativeY[scene][0], actor.Position.Y);
        Assert.Equal(NativeMember[scene][0], harness.State.EntityMembership.IsWorldQueryMember(actor));
        for (int step = 0; step < 12; step++)
        {
            if (scene >= 12 && step == 0)
                await harness.Motion(42, (short)(scene == 14 ? -1000 : 4000), 0, (short)(scene == 14 ? 4000 : 6000));
            if (scene == 5 && step == 2)
                await harness.Forget();
            if (scene == 5 && step == 5)
                await harness.Column();
            if (scene == 6 && step == 2)
                await harness.Column();
            if (scene == 7 && step == 2)
            {
                await harness.Spawn(42, 30);
                actor = harness.State.Entities.Get(42)!;
            }
            if (scene == 11 && step == 2)
                await harness.Teleport(42, actor.Position.Y, 17.5);
            if (scene == 11 && step == 4)
                await harness.Column(neighbor: true);
            await harness.Tick();
            Assert.Equal(NativeMember[scene][step + 1], harness.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.Equal(NativeX[scene][step + 1], actor.Position.X);
            Assert.Equal(NativeZ[scene][step + 1], actor.Position.Z);
            Assert.Equal(NativeVelocityX[scene][step + 1], actor.Velocity.X);
            Assert.Equal(NativeVelocityZ[scene][step + 1], actor.Velocity.Z);
            if (scene == 10)
                Assert.True(harness.State.World.GetBlock(new BlockPos(1, 101, 1)).IsAir);
            double expectedY = NativeY[scene][step + 1];
            double expectedVelocity = NativeVelocityY[scene][step + 1];
            Assert.Equal(expectedY, actor.Position.Y);
            Assert.Equal(expectedVelocity, actor.Velocity.Y);
            Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(107), out Aabb box, out bool blocksBuilding, 107));
            Assert.True(blocksBuilding);
            Assert.Equal(actor.Position.Y, box.MinY);
            bool nativeClear = NativeClear[scene][step + 1];
            bool intersects = harness.State.EntityMembership.IsWorldQueryMember(actor) == true && VanillaPlacementCollision.Intersects(new BlockPos(1, 101, 1), box, BlockInteractionProfile.ForProtocol(107));
            Assert.Equal(nativeClear, !intersects);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task UnclassifiedMovementContextRemainsUnknownAfterLaterPackets(int scene)
    {
        await using var harness = new MotionHarness();
        await harness.Column();
        await harness.Spawn(42, scene == 5 ? 3_000_000_000 : 101);
        Entity actor = harness.State.Entities.Get(42)!;
        if (scene == 0)
            await harness.Block(new BlockPos(1, 100, 1), "web");
        if (scene is 1 or 2 or 3)
            await harness.Object(43, scene == 2 ? 127 : 1, 1.5, 101, 1.5);
        if (scene == 3)
            await harness.Passengers(43, [42]);
        if (scene == 4)
            harness.State.World.SetBorder(harness.State.World.Border.WithSize(4).WithCenter(1.5, 1.5));

        await harness.Tick();

        Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(107), out _, out _, 107));
        await harness.Block(new BlockPos(1, 100, 1), "air");
        if (scene is 1 or 2 or 3)
            await harness.Remove(43);
        harness.State.World.SetBorder(Umpk.Game.World.WorldBorderState.Default);
        await harness.Teleport(42, 30);
        await harness.Motion(42, 0);
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(107), out _, out _, 107));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RemovalAndLevelLifetimeDiscardOwnedFallingState(int scene)
    {
        await using var harness = new MotionHarness();
        await harness.Column();
        await harness.Spawn(42, 101);
        await harness.Tick();
        Entity old = harness.State.Entities.Get(42)!;
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(old, out _));
        if (scene == 0)
            await harness.Remove(42);
        else if (scene == 1)
            harness.State.ResetForRespawn(harness.State.World);
        else if (scene == 2)
            harness.State.ResetForConfigurationReentry();
        else if (scene == 3)
            harness.State.ResetForSessionEnd();
        else
            await harness.Spawn(42, 30);
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(old, out _));
        if (scene is 1 or 2 or 3)
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), 107);
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity current = harness.State.Entities.Get(42)!;
        Assert.NotSame(old, current);
        await harness.Tick();
        Assert.Equal(NativeY[2][1], current.Position.Y);
        Assert.Equal(NativeVelocityY[2][1], current.Velocity.Y);
    }

    private sealed class MotionHarness : IAsyncDisposable
    {
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(20));
        public ClientState State => _applier.State;

        public MotionHarness()
        {
            Assert.True(JavaVersions.TryGetByProtocol(107, out var version));
            _applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(107);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), 107);
        }

        public Task Column(bool neighbor = false)
        {
            ClientboundLevelChunkPacket packet = ColumnPacket(_applier, neighbor);
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _lifetime.Token);
        }

        public Task Forget() => Apply("forget_level_chunk", new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        public Task Spawn(int id, double y) => Apply("add_entity", new ClientboundAddEntityPacket(id, new Guid(id, 0, 0, new byte[8]), 70, 1.5, y, 1.5, 0, 0, 0, 12, 8000, 16000, -8000, null));
        public Task Object(int id, int type, double x, double y, double z) => Apply("add_entity", new ClientboundAddEntityPacket(id, new Guid(id, 0, 0, new byte[8]), type, x, y, z, 0, 0, 0, 0, 0, 0, 0, null));
        public Task Remove(int id) => Apply("remove_entities", new ClientboundRemoveEntitiesPacket([id]));
        public Task Passengers(int vehicle, int[] passengers) => Apply("set_passengers", new ClientboundSetPassengersPacket(vehicle, passengers));
        public Task Motion(int id, short y) => Motion(id, 0, y, 0);
        public Task Motion(int id, short x, short y, short z) => Apply("set_entity_motion", new ClientboundSetEntityMotionPacket(id, x, y, z, null));
        public Task Teleport(int id, double y, double x = 1.5) => Apply("teleport_entity", new ClientboundTeleportEntityPacket(id, x, y, 1.5, 0, 0, false, null, 0));
        public Task Block(BlockPos cell, string name)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out var block));
            return Apply("block_update", new ClientboundBlockUpdatePacket(cell, block.DefaultStateId));
        }

        public Task Tick() => _scheduler.InvokeAsync(() =>
        {
            State.TickEntities(107, JavaGameData.BlockShapes(107), []);
            State.EntityMembership.FinishWorldPhase();
        }, _lifetime.Token);

        private Task Apply(string name, object value)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(107, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var original = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(original);
            codec.Encode(ref writer, value, context);
            object decoded = codec.Decode(original.WrittenSpan, context);
            if (value is ClientboundRemoveEntitiesPacket expectedRemove)
                Assert.Equal(expectedRemove.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
            else if (value is ClientboundSetPassengersPacket expectedPassengers)
            {
                var actual = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                Assert.Equal(expectedPassengers.VehicleId, actual.VehicleId);
                Assert.Equal(expectedPassengers.Passengers, actual.Passengers);
            }
            else
                Assert.Equal(value, decoded);
            var repeated = new ArrayBufferWriter<byte>();
            var repeatedWriter = new PacketWriter(repeated);
            codec.Encode(ref repeatedWriter, decoded, context);
            Assert.Equal(original.WrittenSpan.ToArray(), repeated.WrittenSpan.ToArray());
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _lifetime.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await _scheduler.DisposeAsync();
            _lifetime.Dispose();
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public async Task PlacementUsesRelevantFallingBlockBounds(int scene, int expectedPackets)
    {
        const int protocol = 107;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var applier = new ApplierHarness(version, new ClientFeatures { Terrain = true, Inventory = true, Entities = true, Physics = false, Pathfinding = false });
        ClientState state = applier.State;
        state.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        await applier.ApplyAsync(ColumnPacket(applier));
        var target = new BlockPos(1, 100, 1);
        Assert.True(state.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out var stone));
        await applier.ApplyAsync(RoundTrip("block_update", new ClientboundBlockUpdatePacket(target, stone.DefaultStateId)));
        Assert.True(state.Registries.Items.TryGet(Identifier.Minecraft("stone"), out var item));
        await applier.ApplyAsync(RoundTrip("container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(item, 32))));
        if (scene != 0)
        {
            double y = scene == 2 ? 101 : 30;
            var spawn = new ClientboundAddEntityPacket(42, Guid.Parse("00000000-0000-0000-0000-000000000042"), 70, 1.5, y, 1.5, 0, 0, 0, 12, 0, 0, 0, null);
            var decoded = Assert.IsType<ClientboundAddEntityPacket>(RoundTrip("add_entity", spawn));
            Assert.Equal(spawn, decoded);
            await applier.ApplyAsync(decoded);
            Assert.Equal(Identifier.Minecraft("falling_block"), state.Entities.Get(42)!.Type.Id);
            if (scene == 3)
            {
                await applier.ApplyAsync(RoundTrip("remove_entities", new ClientboundRemoveEntitiesPacket([42])));
                Assert.Null(state.Entities.Get(42));
            }
        }
        await using var scheduler = new ChannelSessionScheduler();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sink = new RecordingSink();
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new(),
            Policies = new(),
            State = state,
            Wire = new(version),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
            CurrentSessionCancellation = () => lifetime.Token,
            ReadPhysicsState = () => new PhysicsState { BoundingBox = Aabb.OfSize(.5, 100, -1.5, .6, 1.8) },
        };
        var actions = new InteractionActions(sink, services, applier.Sequences);

        await actions.PlaceBlockAsync(target, Direction.Up, new Vec3d(.5, 1, .5), ct: lifetime.Token);

        Assert.Equal(expectedPackets, sink.Packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(sink.Packets[0]);
        Assert.Equal(target, use.Position);
        Assert.Equal((int)Direction.Up, use.Face);
        if (expectedPackets == 2)
            Assert.IsType<ServerboundSwingPacket>(sink.Packets[1]);
        Assert.Equal(32, state.Inventory.GetSlot(36).Count);
        Assert.True(state.World.GetBlock(target.Above()).IsAir);

        object RoundTrip(string name, object packet)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, name);
            var context = new PacketCodecContext(state.Registries, IConnectionCodecState.Empty);
            var original = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(original);
            codec.Encode(ref writer, packet, context);
            object decoded = codec.Decode(original.WrittenSpan, context);
            var repeated = new ArrayBufferWriter<byte>();
            var repeatedWriter = new PacketWriter(repeated);
            codec.Encode(ref repeatedWriter, decoded, context);
            Assert.Equal(original.WrittenSpan.ToArray(), repeated.WrittenSpan.ToArray());
            return decoded;
        }
    }

    private sealed class RecordingSink : IPacketSink
    {
        public List<object> Packets { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(107, Assert.IsAssignableFrom<IPacket>(packet)));
            Packets.Add(packet);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => throw new InvalidOperationException();
    }
}
