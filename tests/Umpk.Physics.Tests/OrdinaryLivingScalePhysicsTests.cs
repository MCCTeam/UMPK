using System.IO.Compression;
using System.Text.Json;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class OrdinaryLivingScalePhysicsTests
{
    public static IEnumerable<object[]> NativeLocalPhases()
    {
        using JsonDocument document = Native();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int row = 0; row < 16; row++)
                yield return [release.GetProperty("protocol").GetInt32(), row];
    }

    [Theory]
    [MemberData(nameof(NativeLocalPhases))]
    public void CurrentScaleDrivesProspectivePoseFitWhileTheCachedScaleWaitsForItsPhase(int protocol, int rowIndex)
    {
        using JsonDocument document = Native();
        JsonElement row = document.RootElement.GetProperty("releases").EnumerateArray().First(release => release.GetProperty("protocol").GetInt32() == protocol).GetProperty("rows")[rowIndex];
        string context = row.GetProperty("context").GetString()!;
        var world = new ScaleWorld(protocol, context == "empty" ? null : int.Parse(context[4..], System.Globalization.CultureInfo.InvariantCulture));
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.Reset(new Vec3d(.5, 65, .5), 0, 0);
        float scale = (float)row.GetProperty("scale").GetDouble();
        engine.SetConditions(PhysicsConditions.Default with { Scale = scale, RefreshScaleDimensions = true });
        AssertBody(engine.State, row.GetProperty("after_packet"));
        Assert.Equal(row.GetProperty("after_packet").GetProperty("eye_cached_double").GetDouble(), engine.State.EyeHeight);
        Assert.Equal(1F, engine.State.DimensionsScale);
        var input = new MovementInput { Sneak = row.GetProperty("crouch_input").GetBoolean() };
        PhysicsState state = engine.Step(input).State;
        AssertBody(state, row.GetProperty("after_dirty_phase"));
        Assert.Equal(row.GetProperty("after_dirty_phase").GetProperty("eye_cached_double").GetDouble(), state.EyeHeight);
        Assert.Equal(scale, state.DimensionsScale);
        Assert.False(engine.Conditions.RefreshScaleDimensions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionPushesCannotLoseAnUnconsumedScaleRefresh(bool laterRequestsRefresh)
    {
        var world = new ScaleWorld(770, null);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(770));
        engine.Reset(new Vec3d(.5, 65, .5), 0, 0);
        engine.SetConditions(PhysicsConditions.Default with { Scale = 2F, RefreshScaleDimensions = true });
        engine.SetConditions(PhysicsConditions.Default with { Scale = .5F, RefreshScaleDimensions = laterRequestsRefresh });
        Assert.Equal(1F, engine.State.DimensionsScale);
        PhysicsState state = engine.Step(MovementInput.None).State;
        Assert.Equal(.5F, state.DimensionsScale);
        Assert.Equal((double)(1.62F * .5F), state.EyeHeight);
        Assert.False(engine.Conditions.RefreshScaleDimensions);
        engine.SetConditions(engine.Conditions with { Scale = 2F });
        Assert.Equal(.5F, engine.Step(MovementInput.None).State.DimensionsScale);
    }

    [Theory]
    [InlineData(EntityPose.Standing)]
    [InlineData(EntityPose.Crouching)]
    [InlineData(EntityPose.Swimming)]
    [InlineData(EntityPose.FallFlying)]
    [InlineData(EntityPose.SpinAttack)]
    [InlineData(EntityPose.Sleeping)]
    [InlineData(EntityPose.Dying)]
    public void ForwardSnapshotRestoresCachedBodyAndEyeIndependentlyOfTheProspectiveScale(EntityPose pose)
    {
        var world = new ScaleWorld(770, null);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(770));
        engine.SetConditions(PhysicsConditions.Default with { Scale = .5F });
        engine.LoadState(new PhysicsState { Position = new Vec3d(1.5, 64, 1.5), Pose = pose, DimensionsScale = 2F });
        PhysicsState before = engine.State;
        Assert.Equal(2F, before.DimensionsScale);
        Assert.Equal(pose is EntityPose.Sleeping or EntityPose.Dying ? .2F : 1.2F, before.BoundingBox.XSize);
        Assert.Equal(pose switch { EntityPose.Sleeping => .2F, EntityPose.Dying => 1.62F, _ => (float)PhysicsProfile.EyeHeightForPose(pose, PoseDimensionsEra.ModernCrawl) * 2F }, before.EyeHeight);
        var copy = new PlayerPhysics(world, PhysicsProfile.ForProtocol(770));
        copy.SetConditions(engine.Conditions);
        copy.LoadState(before);
        Assert.Equal(before, copy.State);
    }

    [Fact]
    public void DefaultConditionsPreserveUnscaledSnapshotsAndPoseTiming()
    {
        var engine = new PlayerPhysics(new ScaleWorld(770, null), PhysicsProfile.ForProtocol(770));
        engine.Reset(new Vec3d(.5, 65, .5), 0, 0);
        engine.SetConditions(PhysicsConditions.Default);
        PhysicsState state = engine.Step(new MovementInput { Sneak = true }).State;
        Assert.Null(state.DimensionsScale);
        Assert.Null(engine.Conditions.Scale);
        Assert.Equal(EntityPose.Crouching, state.Pose);
        Assert.Equal((double)1.27F, state.EyeHeight);
    }

    private static void AssertBody(PhysicsState state, JsonElement expected)
    {
        JsonElement box = expected.GetProperty("body");
        Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), state.BoundingBox);
    }

    private static JsonDocument Native()
    {
        using Stream source = typeof(OrdinaryLivingScalePhysicsTests).Assembly.GetManifestResourceStream("Umpk.Physics.Tests.Fixtures.LocalScalePhase.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class ScaleWorld(int protocol, int? roof) : IPhysicsWorldView
    {
        private readonly FixtureBlockData _blocks = new();
        private static readonly Aabb[] Stone = [new Aabb(0, 0, 0, 1, 1, 1)];
        public BlockCollisionQueryEra CollisionQueryEra => PhysicsProfile.BlockCollisionQueryForProtocol(protocol);
        public BlockState GetBlock(BlockPos pos) => new(_blocks, pos.Y == roof && pos.X is >= -1 and <= 1 && pos.Z is >= -1 and <= 1 ? (int)BlockKind.Stone : (int)BlockKind.Air);
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => state.IsAir ? [] : Stone;
        public bool IsChunkLoaded(BlockPos pos) => true;
    }
}
