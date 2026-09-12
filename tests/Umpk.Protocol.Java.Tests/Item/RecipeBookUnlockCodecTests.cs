using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>
/// Byte-anchored tests for the pre-1.21.2 recipe-book unlock packet (<c>minecraft:recipe</c>) on protocols 335-767. The expected bytes below are hand-built field by field from the wire contract, never by running the encoder, so an encoder that merely agrees with itself cannot satisfy them. Three era forms:
/// <list type="bullet">
/// <item>1.12 - 1.12.2 (335 - 340): two book bools and numeric crafting-manager recipe ids.</item>
/// <item>1.13 - 1.16.1 (393 - 736): four book bools (crafting + furnace), resource-location recipes
/// encoded as a resource-location string.</item>
/// <item>1.16.2 - 1.21.1 (751 - 767): the eight-bool <c>RecipeBookSettings</c> block (CRAFTING,
/// FURNACE, BLAST_FURNACE, SMOKER, each open then filtering).</item>
/// </list>
/// The trailing highlight list is present only when the state is INIT, on every era.
/// </summary>
public class RecipeBookUnlockCodecTests
{
    private static readonly Identifier Torch = Identifier.Minecraft("torch");
    private static readonly Identifier Chest = Identifier.Minecraft("chest");

    // "minecraft:torch" / "minecraft:chest" as a length-prefixed UTF-8 string.
    private static readonly byte[] TorchUtf8 =
        [0x0F, .. "minecraft:torch"u8.ToArray()];

    private static readonly byte[] ChestUtf8 =
        [0x0F, .. "minecraft:chest"u8.ToArray()];

    /// <summary>Field-wise comparison. The record's generated equality compares the list fields by reference, so a decoded frame never equals the source packet even when every element matches.</summary>
    private static void AssertSameFields(ClientboundRecipePacket expected, ClientboundRecipePacket actual)
    {
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Books, actual.Books);
        Assert.Equal(expected.Recipes, actual.Recipes);
        Assert.Equal(expected.ToHighlight, actual.ToHighlight);
        Assert.Equal(expected.LegacyRecipeIds, actual.LegacyRecipeIds);
        Assert.Equal(expected.LegacyToHighlightIds, actual.LegacyToHighlightIds);
    }

    // 1.12-1.12.2: two booleans and numeric ids.

    [Fact]
    public void RecipeV1_12_Init_PinnedFrame()
    {
        // The 1.12 form reads the state, GUI-open flag, and filtering-craftable flag;
        // VarInt count plus one VarInt per recipe; INIT adds another list with the same framing.
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Init,
            [new RecipeBookSetting(true, false)],
            Recipes: [],
            ToHighlight: [],
            LegacyRecipeIds: [7, 300],
            LegacyToHighlightIds: [300]);

        byte[] expected =
        [
            0x00,                    // state = INIT
            0x01,                    // crafting book open
            0x00,                    // crafting filtering off
            0x02, 0x07, 0xAC, 0x02,  // recipes: count 2, ids 7 and 300 (VarInt)
            0x01, 0xAC, 0x02,        // highlight: count 1, id 300
        ];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_12, packet));
        AssertSameFields(packet, CodecRoundTrip.Decode(RecipeCodecs.RecipeV1_12, expected));
    }

    [Fact]
    public void RecipeV1_12_Add_OmitsHighlightList()
    {
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Add,
            [new RecipeBookSetting(false, true)],
            Recipes: [],
            ToHighlight: [],
            LegacyRecipeIds: [9],
            LegacyToHighlightIds: []);

        byte[] expected = [0x01, 0x00, 0x01, 0x01, 0x09];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_12, packet));
        AssertSameFields(packet, CodecRoundTrip.Decode(RecipeCodecs.RecipeV1_12, expected));
    }

    // 1.13-1.16.1: four booleans and identifiers.

    [Fact]
    public void RecipeV1_13_Init_PinnedFrame()
    {
        // State VarInt; four booleans for crafting and furnace open/filter settings; VarInt count plus one resource-location string per recipe; INIT adds another list with the same framing.
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Init,
            [new RecipeBookSetting(true, false), new RecipeBookSetting(false, true)],
            Recipes: [Torch, Chest],
            ToHighlight: [Chest],
            LegacyRecipeIds: [],
            LegacyToHighlightIds: []);

        byte[] expected =
        [
            0x00,                          // state = INIT
            0x01, 0x00,                    // crafting: open, not filtering
            0x00, 0x01,                    // furnace: closed, filtering
            0x02, .. TorchUtf8, .. ChestUtf8,  // recipes: count 2
            0x01, .. ChestUtf8,            // highlight: count 1
        ];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_13, packet));
        AssertSameFields(packet, CodecRoundTrip.Decode(RecipeCodecs.RecipeV1_13, expected));

        // Differential: the adjacent eras disagree on the byte count of the book block.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_16_2, packet));
    }

    [Fact]
    public void RecipeV1_13_Remove_PinnedFrame()
    {
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Remove,
            [new RecipeBookSetting(false, false), new RecipeBookSetting(false, false)],
            Recipes: [Torch],
            ToHighlight: [],
            LegacyRecipeIds: [],
            LegacyToHighlightIds: []);

        byte[] expected = [0x02, 0x00, 0x00, 0x00, 0x00, 0x01, .. TorchUtf8];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_13, packet));
        AssertSameFields(packet, CodecRoundTrip.Decode(RecipeCodecs.RecipeV1_13, expected));
    }

    // 1.16.2-1.21.1: eight recipe-book-settings booleans.

    [Fact]
    public void RecipeV1_16_2_Init_PinnedFrame()
    {
        // Wire order: state VarInt; one open-and-filtering boolean pair for CRAFTING, FURNACE, BLAST_FURNACE, and SMOKER; a list of recipe identifiers; and, for INIT, a second identifier list.
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Init,
            [
                new RecipeBookSetting(true, true),
                new RecipeBookSetting(false, false),
                new RecipeBookSetting(true, false),
                new RecipeBookSetting(false, true),
            ],
            Recipes: [Torch],
            ToHighlight: [],
            LegacyRecipeIds: [],
            LegacyToHighlightIds: []);

        byte[] expected =
        [
            0x00,                    // state = INIT
            0x01, 0x01,              // crafting
            0x00, 0x00,              // furnace
            0x01, 0x00,              // blast furnace
            0x00, 0x01,              // smoker
            0x01, .. TorchUtf8,      // recipes: count 1
            0x00,                    // highlight: count 0 (INIT still writes the list)
        ];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_16_2, packet));
        AssertSameFields(packet, CodecRoundTrip.Decode(RecipeCodecs.RecipeV1_16_2, expected));
    }

    [Fact]
    public void RecipeV1_16_2_ShortBookList_PadsClosed()
    {
        // A caller that supplies fewer books than the era carries still produces the fixed-width block, matching the 1.21.2+ recipe_book_settings codec's behaviour.
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Add, [new RecipeBookSetting(true, true)], [], [], [], []);

        byte[] expected = [0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        Assert.Equal(expected, CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_16_2, packet));
    }

    // Era resolution.

    /// <summary>The band boundaries: the packet resolves to a real codec on every protocol it exists on, and the three era forms are actually distinct on the wire (a same-shape mis-binding would be invisible in a per-codec test).</summary>
    [Theory]
    [InlineData("V1_12")]
    [InlineData("V1_12_2")]
    [InlineData("V1_13")]
    [InlineData("V1_13_2")]
    [InlineData("V1_14")]
    [InlineData("V1_15")]
    [InlineData("V1_16")]
    [InlineData("V1_16_2")]
    [InlineData("V1_17")]
    [InlineData("V1_19_4")]
    [InlineData("V1_20_3")]
    [InlineData("V1_21")]
    public void Recipe_IsImplemented_OnEveryProtocolItExistsOn(string key)
    {
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(key));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:recipe");
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound)
            .TryGetInbound(0, out BoundPacketCodec entry));
        Assert.True(entry.IsImplemented, $"minecraft:recipe resolved to a marker under key {key}");
    }

    [Fact]
    public void ThreeWireLayoutForms_ProduceDistinctFrames()
    {
        var packet = new ClientboundRecipePacket(
            RecipeBookState.Add, [new RecipeBookSetting(true, false)], [Torch], [], [11], []);

        byte[] v112 = CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_12, packet);
        byte[] v113 = CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_13, packet);
        byte[] v1162 = CodecRoundTrip.Encode(RecipeCodecs.RecipeV1_16_2, packet);

        Assert.NotEqual(v112, v113);
        Assert.NotEqual(v113, v1162);
        Assert.NotEqual(v112, v1162);
    }
}
