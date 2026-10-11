using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockJoinCodecTests
{
    [Fact]
    public void ResourcePacksInfoWithNoPacksUsesExactWireForm()
    {
        var packet = new ResourcePacksInfoPacket(false, false, false, false, Guid.Empty, string.Empty, []);
        byte[] expected = Convert.FromHexString("00000000000000000000000000000000000000000000");

        Assert.Equal(expected, BedrockJoinCodecs.Encode(packet));
        Assert.Equal(packet, BedrockJoinCodecs.DecodeResourcePacksInfo(expected));
    }

    [Fact]
    public void ResourcePacksInfoRoundTripsOnePack()
    {
        var pack = new BedrockTexturePackInfo(
            Guid.Parse("12345678-1234-1234-1234-1234567890ab"),
            "1.0.0",
            1024UL,
            "key",
            string.Empty,
            "identity",
            false,
            false,
            false,
            string.Empty);
        var packet = new ResourcePacksInfoPacket(true, false, true, false, Guid.Empty, string.Empty, [pack]);

        BedrockJoinCodecs.DecodeResourcePacksInfo(BedrockJoinCodecs.Encode(packet)).Packs
            .AssertSinglePack(pack);
    }

    [Fact]
    public void ResourcePackStackWithNoPacksUsesExactWireForm()
    {
        var packet = new ResourcePackStackPacket(false, [], string.Empty, [], false, false);
        byte[] expected = Convert.FromHexString("000000000000000000");

        Assert.Equal(expected, BedrockJoinCodecs.Encode(packet));
        Assert.Equal(packet, BedrockJoinCodecs.DecodeResourcePackStack(expected));
    }

    [Fact]
    public void ResourcePackStackRoundTripsPacksAndExperiments()
    {
        var packet = new ResourcePackStackPacket(
            true,
            [new BedrockResourcePackIdVersion("pack-id", "1.2.3", string.Empty)],
            "1.26.45",
            [new BedrockExperiment("data_driven_jigsaw_structures", true)],
            false,
            true);

        Assert.Equal(packet, BedrockJoinCodecs.DecodeResourcePackStack(BedrockJoinCodecs.Encode(packet)));
    }

    [Theory]
    [InlineData(0u, "000663616e63656c")]
    [InlineData(1u, "010b646f776e6c6f6164696e67")]
    [InlineData(2u, "0213646f776e6c6f6164696e6766696e6973686564")]
    [InlineData(3u, "03197265736f757263657061636b737461636b66696e6973686564")]
    public void ResourcePackClientResponseUsesStatusAndNamePair(uint statusValue, string expectedHex)
    {
        var status = (ResourcePackResponseStatus)statusValue;
        var packet = new ResourcePackClientResponsePacket(status, status == ResourcePackResponseStatus.SendPacks ? ["pack-a"] : []);
        byte[] encoded = BedrockJoinCodecs.Encode(packet);
        byte[] expected = status == ResourcePackResponseStatus.SendPacks
            ? [.. Convert.FromHexString(expectedHex), 0x01, 0x06, .. "pack-a"u8.ToArray()]
            : Convert.FromHexString(expectedHex);

        Assert.Equal(expected, encoded);
        Assert.Equal(packet, BedrockJoinCodecs.DecodeResourcePackClientResponse(encoded));
    }

    [Fact]
    public void ResourcePackClientResponseRejectsPackIdsOutsideSendPacks()
    {
        var packet = new ResourcePackClientResponsePacket(ResourcePackResponseStatus.Completed, ["pack-a"]);

        Assert.Throws<BedrockCodecException>(() => BedrockJoinCodecs.Encode(packet));
    }

    [Fact]
    public void ResourcePackClientResponseRejectsMismatchedStatusName()
    {
        byte[] encoded = BedrockJoinCodecs.Encode(new ResourcePackClientResponsePacket(ResourcePackResponseStatus.Completed, []));
        encoded[1] = (byte)'X';

        Assert.Throws<BedrockCodecException>(() => BedrockJoinCodecs.DecodeResourcePackClientResponse(encoded));
    }

    [Fact]
    public void ResourcePackClientResponseRejectsUnknownStatus()
    {
        byte[] encoded = Convert.FromHexString("0407756e6b6e6f776e");

        Assert.Throws<BedrockCodecException>(() => BedrockJoinCodecs.DecodeResourcePackClientResponse(encoded));
    }

    [Fact]
    public void StartGameKeepsLocalIdentityAndBoundedRemainder()
    {
        var packet = new StartGamePacket(1L, 2UL, [0xAA, 0xBB]);
        byte[] expected = [0x02, 0x02, 0xAA, 0xBB];

        Assert.Equal(expected, BedrockJoinCodecs.Encode(packet));
        Assert.Equal(packet, BedrockJoinCodecs.DecodeStartGame(expected));
    }

    [Fact]
    public void StartGameRejectsMissingIdentityPrefix()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockJoinCodecs.DecodeStartGame([0x02]));
    }

    [Fact]
    public void BiomeDefinitionListKeepsDefinitionCountAndBoundedRemainder()
    {
        var packet = new BiomeDefinitionListPacket(3U, [0x01, 0x02]);
        byte[] expected = [0x03, 0x01, 0x02];

        Assert.Equal(expected, BedrockJoinCodecs.Encode(packet));
        Assert.Equal(packet, BedrockJoinCodecs.DecodeBiomeDefinitionList(expected));
    }

    [Fact]
    public void AvailableEntityIdentifiersRoundTripsIdentifierList()
    {
        var pig = new NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var cow = new NbtCompound();
        cow.PutString("id", "minecraft:cow");
        var list = new NbtList(NbtTagType.Compound) { pig, cow };
        var root = new NbtCompound();
        root.Put("idlist", list);
        var packet = new AvailableEntityIdentifiersPacket(root);

        AvailableEntityIdentifiersPacket decoded = BedrockJoinCodecs.DecodeAvailableEntityIdentifiers(BedrockJoinCodecs.Encode(packet));

        Assert.Equal(["minecraft:pig", "minecraft:cow"], BedrockJoinCodecs.ReadEntityIdentifiers(decoded));
        Assert.Equal(
            BedrockJoinCodecs.Encode(packet),
            BedrockJoinCodecs.Encode(decoded));
    }

    [Fact]
    public void AvailableEntityIdentifiersRejectsMissingIdentifierList()
    {
        var packet = new AvailableEntityIdentifiersPacket(new NbtCompound());

        Assert.Throws<BedrockCodecException>(() => BedrockJoinCodecs.ReadEntityIdentifiers(packet));
    }

    [Fact]
    public void PacketViolationWarningPreservesSeverityAndReason()
    {
        var packet = new BedrockPacketViolationWarningPacket(
            BedrockViolationType.Malformed,
            BedrockViolationSeverity.Terminating,
            1,
            "Connection Request Invalid.");

        Assert.Equal(packet, BedrockJoinCodecs.DecodePacketViolationWarning(BedrockJoinCodecs.Encode(packet)));
    }
}

internal static class BedrockJoinCodecTestExtensions
{
    internal static void AssertSinglePack(this IReadOnlyList<BedrockTexturePackInfo> packs, BedrockTexturePackInfo expected)
    {
        Assert.Single(packs);
        Assert.Equal(expected, packs[0]);
    }
}
