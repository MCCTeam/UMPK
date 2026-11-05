using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class BlockTargetingTableTests
{
    [Fact]
    public void EveryGeneratedTargetingStateMatchesItsArtifactDerivedDataset()
    {
        string[] files = Directory.GetFiles(Path.Combine(FixturePaths.RepoRoot(), "data", "java"), "block-targeting.json", SearchOption.AllDirectories);
        Assert.Equal(50, files.Length);
        foreach (string file in files)
        {
            int protocol = int.Parse(Directory.GetParent(file)!.Name, System.Globalization.CultureInfo.InvariantCulture);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement data = document.RootElement;
            Aabb[][] pool = ReadShapePool(data);
            var (world, source, position) = Create(protocol, "stone");
            var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(source);
            foreach (JsonElement range in data.GetProperty("state_refs").EnumerateArray())
                for (int state = range[0].GetInt32(); state < range[0].GetInt32() + range[1].GetInt32(); state++)
                {
                    world.SetBlockStateId(position, state);
                    if (!world.BlockData.IsValidState(state))
                        continue;
                    Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape geometry));
                    Assert.Equal(pool[range[2].GetInt32()], geometry.Outline.ToArray());
                    Assert.Equal(pool[range[3].GetInt32()], geometry.Interaction.ToArray());
                    JsonElement shape = data.GetProperty("shape_pool")[range[2].GetInt32()];
                    string model = shape.TryGetProperty("clip_model", out JsonElement perShape) ? perShape.GetString()! : data.GetProperty("clip_model").GetString()!;
                    Assert.Equal(model switch
                    {
                        "aabb" => BlockRayClipModel.LegacyBox,
                        "aabb_list" => BlockRayClipModel.LegacyBoxList,
                        "voxel_forward" => BlockRayClipModel.VoxelForwardInside,
                        "voxel" => BlockRayClipModel.VoxelOppositeInside,
                        _ => throw new InvalidOperationException("Unknown canonical clip model"),
                    }, geometry.ClipModel);
                    string held = range[4].GetString()!;
                    if (held.Length != 0)
                    {
                        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Parse(held), out BlockTargetingShape holding));
                        Assert.Equal(pool[range[5].GetInt32()], holding.Outline.ToArray());
                    }
                }
        }
    }

    [Fact]
    public void EveryGeneratedDoorNeighborRangeMatchesItsNativeMeasurement()
    {
        string[] files = Directory.GetFiles(Path.Combine(FixturePaths.RepoRoot(), "data", "java"), "block-targeting.json", SearchOption.AllDirectories);
        int checkedNodes = 0;
        foreach (string file in files)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement data = document.RootElement;
            if (!data.TryGetProperty("context_nodes", out JsonElement nodes))
                continue;
            int protocol = int.Parse(Directory.GetParent(file)!.Name, System.Globalization.CultureInfo.InvariantCulture);
            Aabb[][] pool = ReadShapePool(data);
            var (world, source, position) = Create(protocol, "wooden_door");
            var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(source);
            HashSet<int> visited = [];
            foreach (JsonElement root in data.GetProperty("context_roots").EnumerateArray())
            {
                int index = root[2].GetInt32();
                if (!visited.Add(index))
                    continue;
                world.SetBlockStateId(position, root[0].GetInt32());
                JsonElement node = nodes[index], offset = node.GetProperty("offset");
                BlockPos neighbor = position.Offset(offset[0].GetInt32(), offset[1].GetInt32(), offset[2].GetInt32());
                foreach (JsonElement range in node.GetProperty("ranges").EnumerateArray())
                    for (int state = range[0].GetInt32(); state < range[0].GetInt32() + range[1].GetInt32(); state++)
                    {
                        world.SetBlockStateId(neighbor, state);
                        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape geometry));
                        Assert.Equal(pool[range[2].GetInt32()], geometry.Outline.ToArray());
                    }
                world.SetBlockStateId(neighbor, 0);
                checkedNodes++;
            }
            Assert.Equal(nodes.GetArrayLength(), visited.Count);
        }
        Assert.True(checkedNodes > 0);
    }

    private static Aabb[][] ReadShapePool(JsonElement data)
        => [.. data.GetProperty("shape_pool").EnumerateArray().Select(shape => shape.GetProperty("boxes").EnumerateArray().Select(box => new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble())).ToArray())];

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    public void LegacyTorchMetadataZeroUsesTheNativeAliasAndFloatSelectionBounds(int protocol)
    {
        var (world, shapes, position) = Create(protocol, "torch");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, -1), new Vec3d(0.5, 64.5, 2), false, shapes);

        Assert.True(hit.Hit);
        Assert.Equal(position, hit.BlockPos);
        Assert.Equal(Direction.North, hit.Face);
        Assert.Equal(0.4000000059604645, hit.Point.Z);
    }

    [Theory]
    [InlineData(47, 1033, 0.8125)]
    [InlineData(47, 17, 0.8125)]
    [InlineData(47, 1145, 0.8125)]
    [InlineData(107, 1033, 0.8125)]
    [InlineData(107, 17, 0.0)]
    [InlineData(107, 1145, 0.0)]
    [InlineData(340, 1033, 0.8125)]
    [InlineData(340, 17, 0.0)]
    [InlineData(340, 1145, 0.0)]
    public void LegacyOpenDoorReadsItsHingeFromTheNativeNeighborRule(int protocol, int aboveState, double minZ)
    {
        var (world, shapes, position) = Create(protocol, "wooden_door");
        world.SetBlockStateId(position, 1028);
        world.SetBlockStateId(position.Offset(0, 1, 0), aboveState);
        var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);

        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape geometry));

        Aabb outline = Assert.Single(geometry.Outline.ToArray());
        Assert.Equal(minZ, outline.MinZ);
        Assert.Equal(minZ + 0.1875, outline.MaxZ);
    }

    [Theory]
    [InlineData(47, 0.5)]
    [InlineData(107, 1.0)]
    [InlineData(340, 1.0)]
    public void LegacyStairInsideHitRetainsItsEraBoxDecomposition(int protocol, double x)
    {
        var (world, shapes, _) = Create(protocol, "oak_stairs");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.25, 64.25, 0.25), new Vec3d(2, 64.25, 0.25), false, shapes);

        Assert.True(hit.Hit);
        Assert.Equal(x, hit.Point.X);
        Assert.Equal(Direction.East, hit.Face);
        Assert.False(hit.InsideBlock);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    public void LegacyStairListDoesNotSelectAHitExactlyAtTheEndpoint(int protocol)
    {
        var (world, shapes, _) = Create(protocol, "oak_stairs");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.25, 64.25, -1), new Vec3d(0.25, 64.25, 0), false, shapes);

        Assert.False(hit.Hit);
    }

    [Theory]
    [InlineData(393, Direction.South)]
    [InlineData(401, Direction.South)]
    [InlineData(404, Direction.South)]
    [InlineData(477, Direction.North)]
    [InlineData(498, Direction.North)]
    [InlineData(770, Direction.North)]
    [InlineData(777, Direction.North)]
    public void OriginInsideUsesTheNativeProbePointAndEraDirection(int protocol, Direction face)
    {
        var (world, shapes, position) = Create(protocol, "stone");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, 0.5), new Vec3d(0.5, 64.5, 2), false, shapes);

        Assert.True(hit.Hit);
        Assert.Equal(position, hit.BlockPos);
        Assert.Equal(new Vec3d(0.5, 64.5, 0.5015), hit.Point);
        Assert.Equal(face, hit.Face);
        Assert.True(hit.InsideBlock);
    }

    [Fact]
    public void InsideDirectionUsesVanillaFloatDotProductsAndDirectionTieOrder()
    {
        var (world, shapes, _) = Create(777, "stone");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, 0.5), new Vec3d(2.500000001, 66.5, 0.5), false, shapes);

        Assert.True(hit.InsideBlock);
        Assert.Equal(Direction.Down, hit.Face);
    }

    [Fact]
    public void VoxelShapesRejectSegmentsBelowTheNativeSquaredLengthThreshold()
    {
        var (world, shapes, _) = Create(777, "stone");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, 0.5), new Vec3d(0.5001, 64.5, 0.5), false, shapes);

        Assert.False(hit.Hit);
    }

    [Fact]
    public void AProbeThatAlreadyLeftTheShapeDoesNotInventAnInsideHit()
    {
        var (world, shapes, _) = Create(777, "stone");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, 1 - 1E-9), new Vec3d(0.5, 64.5, 1.001), false, shapes);

        Assert.False(hit.Hit);
    }

    [Fact]
    public void VoxelEntryPlanesExcludeAHitExactlyAtTheSegmentEndpoint()
    {
        var (world, shapes, _) = Create(777, "stone");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, -1), new Vec3d(0.5, 64.5, 0), false, shapes);

        Assert.False(hit.Hit);
    }

    // Literal vectors were measured with the matching vanilla getOffset methods, including the overflowing int multiplication near the world border.
    [Theory]
    [InlineData(498, 0, 0, 0, -0.25, -0.25)]
    [InlineData(498, 17, 63, -31, 0.0833333432674408, -0.14999999850988388)]
    [InlineData(498, -30000000, -64, 29999999, -0.25, 0.15000000596046448)]
    [InlineData(777, 0, 0, 0, -0.25, -0.25)]
    [InlineData(777, 17, 63, -31, 0.0833333432674408, -0.14999999850988388)]
    [InlineData(777, -30000000, -64, 29999999, -0.25, 0.15000000596046448)]
    public void FlowerOffsetsMatchNativeCoordinateWitnesses(int protocol, int x, int y, int z, double dx, double dz)
    {
        var (world, shapes, original) = Create(protocol, "poppy");
        var position = new BlockPos(x, y, z);
        world.SetBlockStateId(position, world.GetBlockStateId(original));
        var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);

        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape geometry));

        Assert.Equal(new Vec3d(dx, 0, dz), geometry.OutlineOffset);
    }

    [Theory]
    [InlineData(498, "grass")]
    [InlineData(777, "short_grass")]
    public void TallGrassOutlineIgnoresItsNativeRenderingOffset(int protocol, string name)
    {
        var (world, shapes, position) = Create(protocol, name);
        var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);

        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape geometry));

        Assert.Equal(Vec3d.Zero, geometry.OutlineOffset);
    }

    [Fact]
    public void CloserInteractionShapeOverridesTheFaceAndRetainsTheOutlinePoint()
    {
        var (world, shapes, position) = Create(777, "scaffolding");

        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.1, -1), new Vec3d(0.5, 65.1, 1), false, shapes);

        Assert.True(hit.Hit);
        Assert.Equal(position, hit.BlockPos);
        Assert.Equal(64.875, hit.Point.Y);
        Assert.Equal(0.55, hit.Point.Z, precision: 12);
        Assert.Equal(Direction.North, hit.Face);
    }

    [Theory]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public void TorchOutlineIsTargetableWithoutInventingCollision(int protocol)
    {
        var (world, shapes, position) = Create(protocol, "torch");

        Assert.Empty(shapes.GetCollisionShapes(world.GetBlock(position)).ToArray());
        Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);
        BlockHitResult hit = Raycast.CastBlock(world, new Vec3d(0.5, 64.5, -1), new Vec3d(0.5, 64.5, 2), false, shapes);

        Assert.True(hit.Hit);
        Assert.Equal(position, hit.BlockPos);
        Assert.Equal(Direction.North, hit.Face);
        Assert.Equal(0.375, hit.Point.Z);
    }

    [Fact]
    public void ScaffoldingHeldItemChangesOutlineAndRetainsSeparateInteractionShape()
    {
        var (world, shapes, position) = Create(777, "scaffolding");
        var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);

        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape ordinary));
        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("scaffolding"), out BlockTargetingShape held));
        Assert.True(ordinary.Outline.Length > 1);
        Assert.Equal(new Aabb(0, 0, 0, 1, 1, 1), Assert.Single(held.Outline.ToArray()));
        Assert.Equal(new Aabb(0, 0, 0, 1, 1, 1), Assert.Single(ordinary.Interaction.ToArray()));
    }

    [Fact]
    public void LightOutlineDependsOnTheHeldLightItem()
    {
        var (world, shapes, position) = Create(777, "light");
        var targeting = Assert.IsAssignableFrom<IBlockTargetingSource>(shapes);

        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("air"), out BlockTargetingShape ordinary));
        Assert.True(targeting.TryGetTargetingShape(world, position, Identifier.Minecraft("light"), out BlockTargetingShape held));
        Assert.True(ordinary.Outline.IsEmpty);
        Assert.Single(held.Outline.ToArray());
    }

    private static (World, IBlockShapeSource, BlockPos) Create(int protocol, string name)
    {
        var registries = JavaGameData.Registries(protocol);
        var dimensions = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType)
            .Add(0, Identifier.Minecraft("overworld"), new DimensionTypeDefinition(-64, 384, true)).Build();
        var world = new World(new DimensionState(dimensions[0], Identifier.Minecraft("overworld")),
            new RegistryBlockDataSource(registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
        var position = new BlockPos(0, 64, 0);
        world.SetBlockStateId(position, registries.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId);
        return (world, JavaGameData.BlockShapes(protocol), position);
    }
}
