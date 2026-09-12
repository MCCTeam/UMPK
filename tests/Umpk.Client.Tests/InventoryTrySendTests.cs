using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The four version-optional inventory <c>Try*</c> sends: each mirrors its throwing counterpart, capability check first, but reports FALSE instead of raising <see cref="ActionNotSupportedException"/> when the negotiated version cannot carry the send. These branches represent real protocol differences rather than error swallowing.</summary>
public sealed class InventoryTrySendTests
{
    [Theory]
    [InlineData(47)] // 1.8.9: pre-1.13, the MC|ItemName plugin-channel route.
    [InlineData(776)] // 26.2: the dedicated rename_item packet route.
    public async Task TryRenameItem_ReturnsTrue_OnEveryVersion(int protocol)
    {
        (InventoryActions actions, RecordingSink sink) = Build(VersionFor(protocol));

        bool sent = await actions.TryRenameItemAsync("T4ANVIL");

        Assert.True(sent);
        // Whichever route carried it, something actually left the client: the legacy route writes a raw frame, the modern route encodes a packet.
        Assert.True(sink.Packets.Count > 0 || sink.Frames.Count > 0);
    }

    [Theory]
    [InlineData(393)] // 1.13: registers edit_book, but as the legacy item-stack record TryEditBookAsync never builds.
    [InlineData(753)] // 1.16.3: same band.
    public async Task TryEditBook_ReturnsFalse_WhereTheIdentifierExistsButTheSendDoesNot(int protocol)
    {
        (InventoryActions actions, RecordingSink sink) = Build(VersionFor(protocol));

        bool sent = await actions.TryEditBookAsync(0, ["page one"], "T4BOOK");

        Assert.False(sent);
        Assert.Empty(sink.Packets);
    }

    [Fact]
    public async Task TryEditBook_ReturnsTrue_From1_17()
    {
        (InventoryActions actions, RecordingSink sink) = Build(JavaVersions.V1_17);

        bool sent = await actions.TryEditBookAsync(0, ["page one"], "T4BOOK");

        Assert.True(sent);
        Assert.IsType<ServerboundEditBookPacket>(Assert.Single(sink.Packets));
    }

    [Fact]
    public async Task TryPlaceRecipe_ReportsWhetherAnythingLeftTheClient()
    {
        // 1.20.1: the by-name era. The network-id send is refused (nothing left the client)...
        (InventoryActions byNameEra, RecordingSink byNameSink) = Build(JavaVersions.V1_20_1);
        Assert.False(await byNameEra.TryPlaceRecipeAsync(1));
        Assert.Empty(byNameSink.Packets);

        // ...and the by-name send on the very same version actually goes out.
        Assert.True(await byNameEra.TryPlaceRecipeByNameAsync(Identifier.Minecraft("torch")));
        Assert.IsType<ServerboundPlaceRecipeByNamePacket>(Assert.Single(byNameSink.Packets));

        // 26.2: the network-id era. The by-name send is refused...
        (InventoryActions networkIdEra, RecordingSink networkIdSink) = Build(JavaVersions.V26_2);
        Assert.False(await networkIdEra.TryPlaceRecipeByNameAsync(Identifier.Minecraft("torch")));
        Assert.Empty(networkIdSink.Packets);

        // ...and the network-id send on that same version actually goes out.
        Assert.True(await networkIdEra.TryPlaceRecipeAsync(1));
        Assert.IsType<ServerboundPlaceRecipePacket>(Assert.Single(networkIdSink.Packets));
    }

    private static (InventoryActions Actions, RecordingSink Sink) Build(JavaVersion version)
    {
        var recorder = new RecordingSink();
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
        return (new InventoryActions(recorder, services), recorder);
    }

    private static JavaVersion VersionFor(int protocol) => protocol switch
    {
        47 => JavaVersions.V1_8_9,
        393 => JavaVersions.V1_13,
        753 => JavaVersions.V1_16_3,
        776 => JavaVersions.V26_2,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
}
