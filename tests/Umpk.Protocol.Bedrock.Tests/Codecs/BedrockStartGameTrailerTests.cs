using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockStartGameTrailerTests
{
    [Fact]
    public void LiveRemainderDecodesPaletteModeAndRoundTrips()
    {
        byte[] payload = Convert.FromHexString("0A0000003F9F0100470000003F0000000000000000F71A6E94A52EF3AF0000106D696E6563726166743A706C61696E73000200000200FEFF0300000000001A0000000000000000000000000101000000002712636F6D6D616E64426C6F636B4F75747075740101010F646F4461794C696768744379636C650101010D646F456E7469747944726F70730101010A646F466972655469636B0101010D72656369706573556E6C6F636B01010111646F4C696D697465644372616674696E6701010009646F4D6F624C6F6F740101010D646F4D6F62537061776E696E670101010B646F54696C6544726F70730101010E646F576561746865724379636C650101010E64726F776E696E6744616D6167650101010A66616C6C44616D6167650101010A6669726544616D6167650101010D6B656570496E76656E746F72790101000B6D6F624772696566696E67010101037076700101010F73686F77436F6F7264696E617465730101000F706C61796572576179706F696E74730102010000000A6C6F6361746F726261720101010E73686F7744617973506C61796564010100136E61747572616C526567656E65726174696F6E0101010B746E744578706C6F6465730101011373656E64436F6D6D616E64466565646261636B010101156D6178436F6D6D616E64436861696E4C656E6774680102FFFF00000A646F496E736F6D6E696101010114636F6D6D616E64426C6F636B73456E61626C65640101010F72616E646F6D5469636B537065656401020100000012646F496D6D6564696174655265737061776E0101001173686F7744656174684D657373616765730101011466756E6374696F6E436F6D6D616E644C696D69740102102700000B737061776E52616469757301020A0000000873686F77546167730101010C667265657A6544616D616765010101147265737061776E426C6F636B734578706C6F64650101011073686F77426F726465724566666563740101011273686F775265636970654D6573736167657301010119706C6179657273536C656570696E6750657263656E746167650102640000001970726F6A656374696C657343616E427265616B426C6F636B7301010115746E744578706C6F73696F6E44726F70446563617901010000000000000000010400000000000001000000000000012A1000000010000000000000000000000016756D706B2D6C6976652D33323634373833306662383616756D706B2D6C6976652D3332363437383330666238362430303030303030302D303030302D303030302D303030302D3030303030303030303030300050010D0000000000000096BDD7BF03001B3C72616B6E65743E383737612D636135372D376361312D356335360107312E32362E34350A0000439620E1811E3028000000000000000000000000000000000001010100000000000000");

        BedrockStartGameTrailer trailer = BedrockStartGameTrailerCodecs.DecodeTrailer(payload);

        Assert.Equal(0, trailer.Dimension);
        Assert.Equal("minecraft:plains", trailer.BiomeName);
        Assert.Equal(39, trailer.Gamerules.Count);
        Assert.Equal("1.26.45", trailer.Engine);
        Assert.Empty(trailer.BlockProperties);
        Assert.Equal(0x28301E81E1209643UL, trailer.BlockPaletteChecksum);
        Assert.True(trailer.BlockNetworkIdsAreHashes);
        Assert.False(trailer.ClientSideGeneration);
        Assert.NotNull(trailer.ServerJoinInfo);
        Assert.Null(trailer.ServerJoinInfo.Gathering);
        Assert.Equal(payload, BedrockStartGameTrailerCodecs.EncodeTrailer(trailer));
    }

    [Fact]
    public void CustomPropertiesAndJoinDetailsRoundTrip()
    {
        var customState = new NbtCompound();
        customState.Put("custom_state", new NbtInt(7));
        var propertyData = new NbtCompound();
        propertyData.Put("version", new NbtInt(1));
        BedrockStartGameTrailer trailer = MinimalTrailer(
            [new BedrockBlockPropertyEntry("minecraft:custom_block", customState)],
            propertyData,
            new BedrockServerJoinInformation(
                new BedrockGatheringJoinInfo(Guid.Parse("11111111-2222-3333-4444-555555555555"), "exp", null, "world", "creator", null, null, null),
                new BedrockStoreEntryPointInfo("store", "Store"),
                new BedrockPresenceInfo("exp", null, "rich")),
            false);

        byte[] encoded = BedrockStartGameTrailerCodecs.EncodeTrailer(trailer);
        BedrockStartGameTrailer decoded = BedrockStartGameTrailerCodecs.DecodeTrailer(encoded);

        Assert.Equal("minecraft:custom_block", Assert.Single(decoded.BlockProperties).Name);
        Assert.Equal(customState, Assert.Single(decoded.BlockProperties).State);
        Assert.Equal(propertyData, decoded.PropertyData);
        Assert.False(decoded.BlockNetworkIdsAreHashes);
        Assert.Equal("exp", decoded.ServerJoinInfo!.Gathering!.ExperienceName);
        Assert.Equal(encoded, BedrockStartGameTrailerCodecs.EncodeTrailer(decoded));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(9876543210L)]
    public void CurrentTickRoundTripsAsSignedLittleEndian(long currentTick)
    {
        BedrockStartGameTrailer trailer = MinimalTrailer(
            [],
            new NbtCompound(),
            null,
            true,
            currentTick);

        byte[] encoded = BedrockStartGameTrailerCodecs.EncodeTrailer(trailer);
        BedrockStartGameTrailer decoded = BedrockStartGameTrailerCodecs.DecodeTrailer(encoded);

        Assert.Equal(currentTick, decoded.CurrentTick);
        Assert.Equal(encoded, BedrockStartGameTrailerCodecs.EncodeTrailer(decoded));
    }

    [Fact]
    public void TruncatedRemainderIsRejected()
    {
        byte[] payload = Convert.FromHexString("0A0000003F9F0100470000003F0000000000000000F71A6E94A52EF3AF0000106D696E6563726166743A706C61696E73");
        Assert.Throws<BedrockCodecException>(() => BedrockStartGameTrailerCodecs.DecodeTrailer(payload));
    }

    [Fact]
    public void UnknownGameruleTypeIsRejected()
    {
        byte[] payload = Convert.FromHexString("0A0000003F9F0100470000003F0000000000000000F71A6E94A52EF3AF0000106D696E6563726166743A706C61696E73000200000200FEFF0300000000001A0000000000000000000000000101000000002712636F6D6D616E64426C6F636B4F75747075740101010F646F4461794C696768744379636C650101010D646F456E7469747944726F70730101010A646F466972655469636B0101010D72656369706573556E6C6F636B01010111646F4C696D697465644372616674696E6701010009646F4D6F624C6F6F740101010D646F4D6F62537061776E696E670101010B646F54696C6544726F70730101010E646F576561746865724379636C650101010E64726F776E696E6744616D6167650101010A66616C6C44616D6167650101010A6669726544616D6167650101010D6B656570496E76656E746F72790101000B6D6F624772696566696E67010101037076700101010F73686F77436F6F7264696E617465730101000F706C61796572576179706F696E74730102010000000A6C6F6361746F726261720101010E73686F7744617973506C61796564010100136E61747572616C526567656E65726174696F6E0101010B746E744578706C6F6465730101011373656E64436F6D6D616E64466565646261636B010101156D6178436F6D6D616E64436861696E4C656E6774680102FFFF00000A646F496E736F6D6E696101010114636F6D6D616E64426C6F636B73456E61626C65640101010F72616E646F6D5469636B537065656401020100000012646F496D6D6564696174655265737061776E0101001173686F7744656174684D657373616765730101011466756E6374696F6E436F6D6D616E644C696D69740102102700000B737061776E52616469757301020A0000000873686F77546167730101010C667265657A6544616D616765010101147265737061776E426C6F636B734578706C6F64650101011073686F77426F726465724566666563740101011273686F775265636970654D6573736167657301010119706C6179657273536C656570696E6750657263656E746167650102640000001970726F6A656374696C657343616E427265616B426C6F636B7301010115746E744578706C6F73696F6E44726F70446563617901010000000000000000010400000000000001000000000000012A1000000010000000000000000000000016756D706B2D6C6976652D33323634373833306662383616756D706B2D6C6976652D3332363437383330666238362430303030303030302D303030302D303030302D303030302D3030303030303030303030300050010D0000000000000096BDD7BF03001B3C72616B6E65743E383737612D636135372D376361312D356335360107312E32362E34350A0000439620E1811E3028000000000000000000000000000000000001010100000000000000");
        payload[102] = 0x09;

        Assert.Throws<BedrockCodecException>(() => BedrockStartGameTrailerCodecs.DecodeTrailer(payload));
    }

    private static BedrockStartGameTrailer MinimalTrailer(
        IReadOnlyList<BedrockBlockPropertyEntry> properties,
        NbtTag propertyData,
        BedrockServerJoinInformation? joinInfo,
        bool hashes,
        long currentTick = 0L) =>
        new(
            1,
            new BedrockVector3(0.5f, 70f, 0.5f),
            0f,
            0f,
            99UL,
            0,
            "minecraft:plains",
            0,
            1,
            0,
            false,
            1,
            new BedrockStartGamePosition(0, 70, 0),
            false,
            0,
            false,
            false,
            0,
            0U,
            false,
            string.Empty,
            0f,
            0f,
            false,
            true,
            false,
            0U,
            0U,
            false,
            false,
            [new BedrockGameRule("flag", true, BedrockGameRuleKind.Bool, true, 0, 0f), new BedrockGameRule("count", true, BedrockGameRuleKind.Int, false, 3, 0f), new BedrockGameRule("level", true, BedrockGameRuleKind.Float, false, 0, 0.5f)],
            [new BedrockExperimentEntry("exp", true)],
            false,
            false,
            false,
            1,
            4,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "*",
            16,
            16,
            false,
            string.Empty,
            string.Empty,
            false,
            0,
            false,
            0,
            false,
            "level",
            "world",
            "00000000-0000-0000-0000-000000000000",
            false,
            0,
            false,
            currentTick,
            0,
            properties,
            "correlation",
            false,
            "engine",
            propertyData,
            0UL,
            Guid.Empty,
            false,
            hashes,
            false,
            joinInfo,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}
