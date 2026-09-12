using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Seeded byte-level round-trip tests for the "Login" family codecs (cookie, custom query, config common packets). Each asserts frame-exactness via <see cref="CodecRoundTrip.Cycle{T}"/> and covers optional-present/absent and empty-collection edge cases.</summary>
public class LoginFamilyCodecTests
{
    private static Identifier Id(string path) => Identifier.Minecraft(path);

    [Fact]
    public void LoginCookieRequest_RoundTrips()
    {
        var decoded = CodecRoundTrip.Cycle(LoginChannelCodecs.LoginCookieRequest,
            new ClientboundLoginCookieRequestPacket(Id("session")));
        Assert.Equal(Id("session"), decoded.Key);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoginCookieResponse_OptionalPayload_RoundTrips(bool present)
    {
        byte[]? payload = present ? [1, 2, 3, 4] : null;
        var decoded = CodecRoundTrip.Cycle(LoginChannelCodecs.LoginCookieResponse,
            new ServerboundLoginCookieResponsePacket(Id("session"), payload));
        Assert.Equal(payload, decoded.Payload);
    }

    [Fact]
    public void ConfigCookieRoundTrips()
    {
        var req = CodecRoundTrip.Cycle(ConfigurationCodecs.ConfigCookieRequest,
            new ClientboundConfigCookieRequestPacket(Id("k")));
        Assert.Equal(Id("k"), req.Key);

        var resp = CodecRoundTrip.Cycle(ConfigurationCodecs.ConfigCookieResponse,
            new ServerboundConfigCookieResponsePacket(Id("k"), [9, 9]));
        Assert.Equal(new byte[] { 9, 9 }, resp.Payload);

        var store = CodecRoundTrip.Cycle(ConfigurationCodecs.StoreCookie,
            new ClientboundConfigStoreCookiePacket(Id("k"), [7]));
        Assert.Equal(new byte[] { 7 }, store.Payload);
    }

    [Fact]
    public void CustomQuery_RoundTrips_WithData()
    {
        Identifier channel = Identifier.Parse("velocity:player_info");
        var decoded = CodecRoundTrip.Cycle(LoginChannelCodecs.CustomQuery,
            new ClientboundLoginCustomQueryPacket(42, channel, [0xDE, 0xAD, 0xBE, 0xEF]));
        Assert.Equal(42, decoded.TransactionId);
        Assert.Equal(channel, decoded.Channel);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, decoded.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomQueryAnswer_OptionalData_RoundTrips(bool understood)
    {
        byte[]? data = understood ? [1, 2, 3] : null;
        var decoded = CodecRoundTrip.Cycle(LoginChannelCodecs.CustomQueryAnswer,
            new ServerboundLoginCustomQueryAnswerPacket(7, data));
        Assert.Equal(7, decoded.TransactionId);
        Assert.Equal(data, decoded.Data);
    }

    [Fact]
    public void ConfigCustomPayload_BothDirections_RoundTrip()
    {
        var client = CodecRoundTrip.Cycle(ConfigurationCodecs.ConfigCustomPayloadClient,
            new ClientboundConfigCustomPayloadPacket(Id("brand"), [0, 1, 2]));
        Assert.Equal(new byte[] { 0, 1, 2 }, client.Data);

        var server = CodecRoundTrip.Cycle(ConfigurationCodecs.ConfigCustomPayloadServer,
            new ServerboundConfigCustomPayloadPacket(Id("brand"), []));
        Assert.Empty(server.Data);
    }

    [Fact]
    public void PingPong_RoundTrip()
    {
        Assert.Equal(1234, CodecRoundTrip.Cycle(ConfigurationCodecs.Ping, new ClientboundConfigPingPacket(1234)).Id);
        Assert.Equal(-9, CodecRoundTrip.Cycle(ConfigurationCodecs.Pong, new ServerboundConfigPongPacket(-9)).Id);
    }

    [Fact]
    public void Transfer_RoundTrips()
    {
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.Transfer,
            new ClientboundConfigTransferPacket("play.example.net", 25565));
        Assert.Equal("play.example.net", decoded.Host);
        Assert.Equal(25565, decoded.Port);
    }

    [Fact]
    public void ResetChat_RoundTrips_Empty()
    {
        byte[] bytes = CodecRoundTrip.Encode(ConfigurationCodecs.ResetChat, new ClientboundConfigResetChatPacket());
        Assert.Empty(bytes);
        _ = CodecRoundTrip.Cycle(ConfigurationCodecs.ResetChat, new ClientboundConfigResetChatPacket());
    }

    [Fact]
    public void Disconnect_RoundTrips_InEveryComponentWireLayout()
    {
        foreach (PacketCodec<ClientboundConfigDisconnectPacket> codec in new[]
        {
            ConfigurationCodecs.DisconnectV1_20_2,
            ConfigurationCodecs.DisconnectV1_20_3,
            ConfigurationCodecs.DisconnectV1_21_5,
        })
        {
            var decoded = CodecRoundTrip.Cycle(codec, new ClientboundConfigDisconnectPacket(Component.Text("bye")));
            Assert.NotNull(decoded.Reason);
        }
    }

    [Fact]
    public void UpdateEnabledFeatures_RoundTrips_IncludingEmpty()
    {
        var full = CodecRoundTrip.Cycle(ConfigurationCodecs.UpdateEnabledFeatures,
            new ClientboundConfigUpdateEnabledFeaturesPacket([Id("vanilla"), Id("bundle")]));
        Assert.Equal(2, full.Features.Count);

        var empty = CodecRoundTrip.Cycle(ConfigurationCodecs.UpdateEnabledFeatures,
            new ClientboundConfigUpdateEnabledFeaturesPacket([]));
        Assert.Empty(empty.Features);
    }

    [Fact]
    public void RegistryData_RoundTrips_WithAndWithoutData()
    {
        var compound = new NbtCompound();
        compound.PutInt("k", 5);
        NbtTag tag = compound;
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.RegistryData,
            new ClientboundConfigRegistryDataPacket(Id("worldgen/biome"),
            [
                new PackedRegistryEntry(Id("plains"), tag),
                new PackedRegistryEntry(Id("desert"), null),
            ]));
        Assert.Equal(2, decoded.Entries.Count);
        Assert.NotNull(decoded.Entries[0].Data);
        Assert.Null(decoded.Entries[1].Data);
    }

    [Fact]
    public void UpdateTags_RoundTrips()
    {
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.UpdateTags,
            new ClientboundConfigUpdateTagsPacket(
            [
                new TagRegistry(Id("block"), [new TagEntry(Id("mineable/axe"), [1, 2, 3]), new TagEntry(Id("logs"), [])]),
                new TagRegistry(Id("item"), []),
            ]));
        Assert.Equal(2, decoded.Registries.Count);
        Assert.Equal(3, decoded.Registries[0].Tags[0].Ids.Count);
        Assert.Empty(decoded.Registries[0].Tags[1].Ids);
        Assert.Empty(decoded.Registries[1].Tags);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourcePackPop_OptionalId_RoundTrips(bool present)
    {
        Guid? id = present ? Guid.NewGuid() : null;
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.ResourcePackPop,
            new ClientboundConfigResourcePackPopPacket(id));
        Assert.Equal(id, decoded.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourcePackPush_OptionalPrompt_RoundTrips(bool prompt)
    {
        Component? p = prompt ? Component.Text("please") : null;
        Guid id = Guid.NewGuid();
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.ResourcePackPushV1_21_5,
            new ClientboundConfigResourcePackPushPacket(id, "https://packs/x.zip", "abcdef", Required: true, p));
        Assert.Equal(id, decoded.Id);
        Assert.Equal("https://packs/x.zip", decoded.Url);
        Assert.Equal("abcdef", decoded.Hash);
        Assert.True(decoded.Required);
        Assert.Equal(prompt, decoded.Prompt is not null);
    }

    [Fact]
    public void ResourcePackResponse_RoundTrips()
    {
        Guid id = Guid.NewGuid();
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.ResourcePackResponseV1_20_3,
            new ServerboundConfigResourcePackPacket(id, 3));
        Assert.Equal(id, decoded.Id);
        Assert.Equal(3, decoded.Action);
    }

    [Fact]
    public void CustomReportDetails_RoundTrips()
    {
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.CustomReportDetails,
            new ClientboundConfigCustomReportDetailsPacket(
            [
                new ReportDetail("title", "desc"),
                new ReportDetail("t2", "d2"),
            ]));
        Assert.Equal(2, decoded.Details.Count);
        Assert.Equal("title", decoded.Details[0].Title);
    }

    [Fact]
    public void ServerLinks_RoundTrips_KnownAndCustom()
    {
        var decoded = CodecRoundTrip.Cycle(ConfigurationCodecs.ServerLinksV1_21_5,
            new ClientboundConfigServerLinksPacket(
            [
                new ServerLinkEntry(KnownTypeId: 0, Label: null, "https://bugs.example"),
                new ServerLinkEntry(KnownTypeId: null, Label: Component.Text("Wiki"), "https://wiki.example"),
            ]));
        Assert.Equal(2, decoded.Links.Count);
        Assert.Equal(0, decoded.Links[0].KnownTypeId);
        Assert.Null(decoded.Links[0].Label);
        Assert.Null(decoded.Links[1].KnownTypeId);
        Assert.NotNull(decoded.Links[1].Label);
    }
}
