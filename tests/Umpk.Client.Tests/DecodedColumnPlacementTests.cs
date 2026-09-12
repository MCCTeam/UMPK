using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A decoded chunk column must use its dimension's absolute Y range. The nether spans 0 through 255, while the modern overworld starts at -64.</summary>
/// <remarks>
/// <para>The frames place a marker in the section covering absolute Y 112-127. The tests assert it at the dimension-relative location and assert its absence 64 blocks lower.</para>
/// <para>The frames also carry the zero padding sent by a 1.21.5 server. The client must read the fixed number of sections defined by the dimension instead of treating the padded buffer length as more sections.</para>
/// </remarks>
public sealed class DecodedColumnPlacementTests
{
    private const int Protocol = 770;
    private const int MarkerState = 1234;

    /// <summary>The section index covering absolute Y 112-127 in a dimension whose floor is 0.</summary>
    private const int MarkerSection = 7;

    [Theory]
    // dimension, its floor, its section count, and the absolute Y the marker section's floor sits at.
    [InlineData("minecraft:the_nether", 0, 16, 112)]
    [InlineData("minecraft:the_end", 0, 16, 112)]
    public async Task DecodedColumn_LandsAtTheDimensionsAbsoluteY(
        string dimension, int expectedMinY, int expectedSections, int markerBaseY)
    {
        ApplierHarness harness = await JoinedHarnessAsync(dimension, expectedSections);

        await harness.ApplyAsync(DecodeFrame(expectedSections, MarkerSection));

        Game.World.World world = harness.State.World;
        Assert.Equal(expectedMinY, world.Dimension.MinY);

        Game.World.ChunkColumn column = world.GetColumn(new ChunkPos(0, 0))!;
        Assert.Equal(expectedMinY, column.MinY);
        Assert.Equal(expectedSections, column.SectionCount);
        Assert.Same(world.Dimension, column.Dimension);

        for (int y = markerBaseY; y < markerBaseY + 16; y++)
            Assert.Equal(MarkerState, world.GetBlockStateId(new BlockPos(0, y, 0)));

        // The exact wrong answer the broken build gave: the same blocks 64 lower.
        for (int y = markerBaseY - 64; y < markerBaseY - 48; y++)
            Assert.Equal(0, world.GetBlockStateId(new BlockPos(0, y, 0)));

    }

    /// <summary>The overworld control. Its floor really is -64 on this protocol, so the marker section (index 7 from the floor) covers 48-63 there, not 112-127. Without this leg a fix that simply moved the hardcoded floor from -64 to 0 would look correct.</summary>
    [Fact]
    public async Task DecodedColumn_InTheOverworld_KeepsTheDeepFloor()
    {
        ApplierHarness harness = await JoinedHarnessAsync("minecraft:overworld", sectionCount: 24);

        await harness.ApplyAsync(DecodeFrame(sectionCount: 24, markerSection: MarkerSection));

        Game.World.World world = harness.State.World;
        Assert.Equal(-64, world.Dimension.MinY);
        Assert.Equal(24, world.GetColumn(new ChunkPos(0, 0))!.SectionCount);
        Assert.Equal(MarkerState, world.GetBlockStateId(new BlockPos(0, -64 + (MarkerSection * 16), 0)));
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(0, MarkerSection * 16, 0)));
    }

    /// <summary>The end-to-end path: the server's own <c>dimension_type</c> registry arrives in the configuration phase, the join names its dimension type by NETWORK ID (1.20.5+), and the decoded column lands on the bounds that id resolved to. The registry here is deliberately NOT vanilla's ordering and carries a datapack dimension with bounds no name-keyed table could produce, so the assertion can only pass if the server's table was actually used.</summary>
    [Fact]
    public async Task ServerRegistryDimensionType_DrivesTheColumnBounds()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5)
        {
            State = { Registries = Umpk.Data.Java.JavaGameData.Registries(Protocol) },
        };

        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(
            RegistryIds.DimensionType,
            [
                new PackedRegistryEntry(Identifier.Minecraft("the_nether"), Element(0, 256, skylight: false)),
                new PackedRegistryEntry(new Identifier("mypack", "skyblock"), Element(48, 128, skylight: true)),
            ]));

        Registry<DimensionTypeDefinition> types = harness.State.Registries!.DimensionTypes;
        Assert.Equal(2, types.Count);

        // Network id 1 is the datapack dimension: floor 48, 8 sections.
        await JoinAsync(harness, "mypack:skyblock", dimensionTypeId: 1);
        await harness.ApplyAsync(DecodeFrame(sectionCount: 8, markerSection: 0));

        Game.World.World world = harness.State.World;
        Assert.Equal(48, world.Dimension.MinY);
        Assert.Equal(128, world.Dimension.Height);
        Assert.True(world.Dimension.HasSkylight);

        Game.World.ChunkColumn column = world.GetColumn(new ChunkPos(0, 0))!;
        Assert.Equal(48, column.MinY);
        Assert.Equal(8, column.SectionCount);
        Assert.Equal(MarkerState, world.GetBlockStateId(new BlockPos(0, 48, 0)));
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(0, 0, 0)));
    }

    /// <summary>The same end-to-end path on 1.20.2/1.20.4 (764/765), where the identical configuration-phase identifier carries ONE NBT blob for every registry instead of a packet per registry, and the join names its dimension type by RESOURCE KEY rather than by id. Live on 1.20.4 this is what turned an empty dimension-type registry into the server's own four entries, <c>minecraft:overworld_caves</c> among them, which no name-keyed table has.</summary>
    [Fact]
    public async Task ServerRegistryBlob_DrivesTheColumnBounds()
    {
        var harness = new ApplierHarness(JavaVersions.V1_20_4)
        {
            State = { Registries = Umpk.Data.Java.JavaGameData.Registries(765) },
        };

        await harness.ApplyAsync(new ClientboundConfigRegistryBlobPacket(
            Blob(("minecraft:overworld", 0, Element(-64, 384, skylight: true)),
                 ("mypack:attic", 1, Element(96, 64, skylight: false)))));

        Assert.Equal(2, harness.State.Registries!.DimensionTypes.Count);

        await JoinAsync(harness, "mypack:loft", dimensionTypeId: 0, dimensionTypeName: "mypack:attic");
        await harness.ApplyAsync(DecodeFrame(sectionCount: 4, markerSection: 0));

        Game.World.World world = harness.State.World;
        Assert.Equal(96, world.Dimension.MinY);
        Assert.Equal(64, world.Dimension.Height);
        Assert.Equal(96, world.GetColumn(new ChunkPos(0, 0))!.MinY);
        Assert.Equal(MarkerState, world.GetBlockStateId(new BlockPos(0, 96, 0)));
    }

    private static NbtCompound Blob(params (string Name, int Id, NbtCompound Element)[] entries)
    {
        var values = new NbtList(NbtTagType.Compound);
        foreach ((string name, int id, NbtCompound element) in entries)
        {
            var record = new NbtCompound();
            record.PutString("name", name);
            record.PutInt("id", id);
            record.Put("element", element);
            values.Add(record);
        }

        var section = new NbtCompound();
        section.PutString("type", "minecraft:dimension_type");
        section.Put("value", values);

        var root = new NbtCompound();
        root.Put("minecraft:dimension_type", section);
        return root;
    }

    private static async Task<ApplierHarness> JoinedHarnessAsync(string dimension, int sectionCount)
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        await JoinAsync(harness, dimension, dimensionTypeId: 0);
        Assert.Equal(sectionCount, harness.State.World.Dimension.SectionCount);
        return harness;
    }

    private static async Task JoinAsync(
        ApplierHarness harness, string dimension, int dimensionTypeId, string? dimensionTypeName = null)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: dimensionTypeId, Dimension: dimension, Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63)
        {
            DimensionTypeName = dimensionTypeName,
        };
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [dimension], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));
    }

    private static NbtCompound Element(int minY, int height, bool skylight)
    {
        var element = new NbtCompound();
        element.PutInt("min_y", minY);
        element.PutInt("height", height);
        element.PutBool("has_skylight", skylight);
        return element;
    }

    /// <summary>Decodes a 1.21.5 chunk frame of <paramref name="sectionCount"/> single-value sections (the marker state in one of them, air in the rest) followed by 39 zero bytes of server pad.</summary>
    private static ClientboundLevelChunkPacket DecodeFrame(int sectionCount, int markerSection)
    {
        var sections = new ArrayBufferWriter<byte>();
        var sw = new PacketWriter(sections);
        for (int i = 0; i < sectionCount; i++)
        {
            bool marked = i == markerSection;
            sw.WriteShort(marked ? (short)4096 : (short)0);
            sw.WriteByte(0);                                   // block container: single value
            sw.WriteVarInt(marked ? MarkerState : 0);
            sw.WriteByte(0);                                   // biome container: single value
            sw.WriteVarInt(0);
        }

        // The over-allocated zero tail a real 1.21.5 server sends (39 bytes on the captured nether column: one VarInt length per container that getSerializedSize still counts).
        sw.WriteBytes(new byte[39]);

        var frame = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(frame);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteVarInt(0);                                      // no heightmaps
        w.WriteVarInt(sections.WrittenCount);
        w.WriteBytes(sections.WrittenSpan);
        w.WriteBytes([0x00]);                                  // empty block-entity list

        byte[] bytes = frame.WrittenSpan.ToArray();
        var reader = new PacketReader(bytes);
        return ChunkCodecs.V1_21_5.Decode(ref reader, PacketCodecContext.Registryless);
    }
}
