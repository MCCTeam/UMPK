using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ConfigurationEnabledFeaturesSessionRuntimeTests
{
    public static IEnumerable<object[]> InitialCases() => new[] { 761, 762, 763, 764, 765, 766, 768, 777 }.SelectMany(protocol => new[] { false, true }.Select(enabled => new object[] { protocol, enabled }));

    [Theory]
    [MemberData(nameof(InitialCases))]
    public async Task RealFeatureFrameUpdatesItsConnectionOwnerBeforePublicUse(int protocol, bool enabled)
    {
        await using var session = await BlockUseSession.Create(protocol, "stone", featureFlags: enabled);
        var frame = Assert.Single(session.FeatureFrames);
        Assert.Equal(protocol >= 764 ? ProtocolPhase.Configuration : ProtocolPhase.Play, frame.Phase);
        Assert.True(frame.WireId >= 0);
        Assert.True(frame.PayloadLength > 0);
        var packet = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(frame.Packet);
        Assert.Equal(enabled ? 4 : 1, packet.Features.Count);
        Assert.True(Assert.Single(session.VanillaMembershipAtFeatureEvent));
        string gated = protocol is 761 or 762 ? "bamboo_door" : protocol is 765 or 766 ? "copper_door" : protocol == 768 ? "pale_oak_door" : "oak_door";
        bool expected = enabled || protocol is 763 or 764 or 777;
        Assert.Equal(expected, session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft(gated), protocol));
        await session.Hold(gated);
        var sent = await session.Use(Hand.Main);
        Assert.Equal(expected ? 1 : 0, sent.Count);
        if (expected)
            Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
    }

    public static IEnumerable<object[]> ConfigurationProtocols() => new[] { 764, 765, 766, 768, 777 }.Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(ConfigurationProtocols))]
    public async Task ReentryReplacesFeatureSetInFrameOrderAndOmissionRetainsIt(int protocol)
    {
        await using var session = await BlockUseSession.Create(protocol, "stone");
        Assert.Single(session.FeatureFrames);
        await session.Reconfigure([]);
        Assert.Equal(2, session.FeatureFrames.Count);
        Assert.Empty(Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(session.FeatureFrames[1].Packet).Features);
        Assert.False(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        Assert.Equal(new bool?[] { true, false }, session.VanillaMembershipAtFeatureEvent);
        await session.Reconfigure(null);
        Assert.Equal(2, session.FeatureFrames.Count);
        Assert.False(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        await session.Reconfigure([Identifier.Minecraft("vanilla")]);
        Assert.Equal(3, session.FeatureFrames.Count);
        Assert.True(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        Assert.Equal(new bool?[] { true, false, true }, session.VanillaMembershipAtFeatureEvent);
    }

    [Theory]
    [MemberData(nameof(ConfigurationProtocols))]
    public async Task WorldReplacementRetainsTheConnectionFeatureSet(int protocol)
    {
        await using var session = await BlockUseSession.Create(protocol, "stone");
        Assert.Single(session.FeatureFrames);
        var original = session.Client.State.World;
        await session.Respawn();
        Assert.NotSame(original, session.Client.State.World);
        Assert.Equal(Identifier.Minecraft("the_nether"), session.Client.State.World.Dimension.DimensionName);
        Assert.True(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        if (protocol is 765 or 766)
            Assert.True(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("copper_door"), protocol));
        if (protocol == 768)
            Assert.True(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("pale_oak_door"), protocol));
        Assert.Single(session.FeatureFrames);
    }

    [Theory]
    [MemberData(nameof(ConfigurationProtocols))]
    public async Task SessionEndClearsTheAdvertisedSetAndPeerSeesEof(int protocol)
    {
        await using var session = await BlockUseSession.Create(protocol, "stone");
        Assert.Single(session.FeatureFrames);
        await session.Reconfigure([]);
        Assert.False(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        await session.Client.DisconnectAsync(session.Token);
        Assert.True(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("stone"), protocol));
        if (protocol is 765 or 766)
            Assert.False(session.Client.State.EnabledFeatures.IsBlockEnabled(Identifier.Minecraft("copper_door"), protocol));
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
    }
}
