using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The anvil rename on BOTH sides of the 1.13 boundary.
///
/// <para>Protocols 47-340 rename through the <c>MC|ItemName</c> plugin channel. The payload is a VarInt UTF-8 byte count followed by the name bytes, so the frame body is exactly two Minecraft Strings: channel and payload. Protocol 393 introduces the serverbound <c>rename_item</c> packet at wire id 28.</para>
/// </summary>
public sealed class AnvilRenameTransportTests
{
    /// <summary>The five protocols the sweep measured as FAIL, plus the modern control.</summary>
    public static TheoryData<int> LegacyProtocols => new(47, 110, 210, 316, 340);

    private static readonly Identifier CustomPayloadId = Identifier.Minecraft("custom_payload");

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void CanRenameItem_IsTrue_OnTheWholeLegacyBand(int protocol)
    {
        ClientActionCapabilities capabilities = CapabilitiesFor(protocol);

        // This is the capability value that consumers branch on.
        Assert.True(capabilities.CanRenameItem);
        Assert.Equal(protocol, capabilities.Protocol);
    }

    [Theory]
    [InlineData(393)]
    [InlineData(767)]
    [InlineData(776)]
    public void CanRenameItem_IsTrue_FromV1_13_ByTheDedicatedPacket(int protocol)
    {
        ClientActionCapabilities capabilities = CapabilitiesFor(protocol);

        Assert.True(capabilities.CanRenameItem);
    }

    /// <summary>The legacy route must be bounded ABOVE at 340. A plain "no rename_item, so use the channel" rule would claim the channel on any future version that dropped the packet, and 1.13 proves the server side can disappear: this is the cross-era rejection the round-trip cannot see.</summary>
    [Theory]
    [InlineData(47, true)]
    [InlineData(340, true)]
    [InlineData(393, false)]
    [InlineData(776, false)]
    public void TheTwoRoutes_AreMutuallyExclusive_AndSplitAtV1_13(int protocol, bool expectLegacy)
    {
        ClientActionCapabilities capabilities = CapabilitiesFor(protocol);

        Assert.Equal(expectLegacy, capabilities.CanRenameItemLegacy);
        Assert.Equal(!expectLegacy, capabilities.CanRenameItemDirect);
    }

    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public async Task RenameItemAsync_OnTheLegacyBand_WritesTheMCItemNameFrame(int protocol)
    {
        JavaVersion version = VersionFor(protocol);
        var recorder = new RecordingSink();
        InventoryActions actions = ActionsFor(version, recorder);

        await actions.RenameItemAsync("T4ANVIL");

        // NOT a record send: the legacy route is a raw frame, so nothing must reach SendAsync.
        Assert.Empty(recorder.Packets);
        (int wireId, byte[] payload) = Assert.Single(recorder.Frames);

        // The frame goes out under the era's own serverbound play custom_payload id.
        Assert.Equal(new WireIndex(version).ServerboundPlay(CustomPayloadId), wireId);

        // Frame LENGTH, asserted explicitly, because a round trip through this build's own writer would agree with itself whatever the framing was. Two Minecraft Strings: 1 + 11 + 1 + 7 = 20.
        Assert.Equal(20, payload.Length);

        // And the bytes themselves, spelled out.
        byte[] expected =
        [
            11, .. Encoding.UTF8.GetBytes("MC|ItemName"),
            7, .. Encoding.UTF8.GetBytes("T4ANVIL"),
        ];
        Assert.Equal(expected, payload);
    }

    /// <summary>An empty name is the server's "clear the name" request. The payload must still contain the zero-length string byte; an absent payload has a different wire meaning.</summary>
    [Fact]
    public async Task RenameItemAsync_WithAnEmptyName_StillWritesTheLengthByte()
    {
        var recorder = new RecordingSink();
        InventoryActions actions = ActionsFor(JavaVersions.V1_8_9, recorder);

        await actions.RenameItemAsync(string.Empty);

        (_, byte[] payload) = Assert.Single(recorder.Frames);
        Assert.Equal(13, payload.Length);          // 1 + 11 channel, then a single 0x00 length
        Assert.Equal(0, payload[^1]);
    }

    /// <summary>The 1.13+ route must stay the dedicated packet. Routing a modern version down the channel would send a frame no 1.13+ server has a handler for, and nothing would throw.</summary>
    [Theory]
    [InlineData(393)]
    [InlineData(767)]
    public async Task RenameItemAsync_FromV1_13_SendsTheDedicatedRecord_AndNoFrame(int protocol)
    {
        var recorder = new RecordingSink();
        InventoryActions actions = ActionsFor(VersionFor(protocol), recorder);

        await actions.RenameItemAsync("T4ANVIL");

        Assert.Empty(recorder.Frames);
        ServerboundRenameItemPacket sent = Assert.IsType<ServerboundRenameItemPacket>(
            Assert.Single(recorder.Packets));
        Assert.Equal("T4ANVIL", sent.Name);
    }

    /// <summary>Across every supported protocol, each version has exactly one rename route, never two and never none. This is what earns <c>RenameItemAsync</c> its place on <c>VersionOptionalSendTests</c>'s always-available list; without it that exemption would be an assertion of faith. It also catches a future dataset that added a protocol between 340 and 393, which would land on neither side of the boundary and silently lose the rename there.</summary>
    [Fact]
    public void EveryShippedProtocol_HasExactlyOneRenameRoute()
    {
        foreach (JavaVersion version in JavaVersions.All)
        {
            int protocol = version.Version.Protocol;
            ClientActionCapabilities capabilities = new(
                new WireIndex(version), protocol, static () => ProtocolPhase.Play);

            Assert.True(
                capabilities.CanRenameItemDirect ^ capabilities.CanRenameItemLegacy,
                $"protocol {protocol} has {(capabilities.CanRenameItem ? "both" : "neither")} rename route");
            Assert.True(capabilities.CanRenameItem, $"protocol {protocol} cannot rename at all");

            // And the route is the era's, not whichever happened to answer first.
            Assert.Equal(protocol <= 340, capabilities.CanRenameItemLegacy);
        }
    }

    private static ClientActionCapabilities CapabilitiesFor(int protocol)
    {
        JavaVersion version = VersionFor(protocol);
        return new ClientActionCapabilities(
            new WireIndex(version), version.Version.Protocol, static () => ProtocolPhase.Play);
    }

    private static InventoryActions ActionsFor(JavaVersion version, RecordingSink recorder)
    {
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = new ClientState(new ClientFeatures().Normalized()),
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return new InventoryActions(recorder, services);
    }

    private static JavaVersion VersionFor(int protocol) => protocol switch
    {
        47 => JavaVersions.V1_8_9,
        110 => JavaVersions.V1_9_4,
        210 => JavaVersions.V1_10_2,
        316 => JavaVersions.V1_11_2,
        340 => JavaVersions.V1_12_2,
        393 => JavaVersions.V1_13,
        767 => JavaVersions.V1_21_1,
        776 => JavaVersions.V26_2,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
}
