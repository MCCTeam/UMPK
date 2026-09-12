using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Dialogs;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Every version-optional send reports failure on a version that cannot carry it, and succeeds exactly where <see cref="ClientActionCapabilities"/> says it will.</summary>
/// <remarks>
/// <para>A version-optional send must return failure when it emits no packet. Logging a warning and returning a completed task would let callers report success for an operation that never left the client.</para>
/// <para>The sweep covers every supported protocol to catch a capability that is true where encoding throws or an action window that drifts from its binding table. Each send is exercised through the real registrar and, where the capability says it should work, the emitted packet is put through the production outbound table so a true answer is proved to be encodable and not merely registered.</para>
/// </remarks>
public sealed class VersionOptionalSendTests
{
    public static TheoryData<int> AllProtocols()
    {
        var data = new TheoryData<int>();
        foreach (JavaVersion version in JavaVersions.All)
            data.Add(version.Version.Protocol);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public async Task EverySend_MatchesItsCapability_OnEveryProtocol(int protocol)
    {
        foreach (SendCase send in Cases)
        {
            var harness = new Harness(protocol);
            bool capable = send.Capable(harness.Services.Capabilities);

            if (!capable)
            {
                ActionNotSupportedException failure = await Assert.ThrowsAsync<ActionNotSupportedException>(
                    () => send.Invoke(harness));
                Assert.Equal(protocol, failure.Protocol);
                Assert.Empty(harness.Sink.Packets);
                Assert.Empty(harness.Sink.Frames);
                continue;
            }

            await send.Invoke(harness);

            // A capability answer must reflect what can ACTUALLY be sent. Putting the emitted record through the production outbound table is what separates "registered" from "encodable": edit_book on 393-753 passes an identifier check and throws here.
            foreach (object packet in harness.Sink.Packets)
                BoundDescriptorCodec.EncodeServerbound(protocol, (IPacket)packet);

            Assert.True(
                harness.Sink.Packets.Count + harness.Sink.Frames.Count > 0,
                $"{send.Name} reported capable on protocol {protocol} but sent nothing.");
        }
    }

    /// <summary>The traps an identifier lookup gets wrong. Both of these protocols DO carry the identifier.</summary>
    [Theory]
    // edit_book is registered across 393-753 but bound to the legacy item-stack record, which EditBookAsync never constructs, so the encode throws where an identifier check passes.
    [InlineData(393, "edit_book")]
    [InlineData(477, "edit_book")]
    [InlineData(753, "edit_book")]
    // sign_update is a deliberate marker on 47 (its lines are JSON components there).
    [InlineData(47, "sign_update")]
    public void IdentifierIsPresent_ButTheCapabilityIsFalse(int protocol, string identifier)
    {
        var harness = new Harness(protocol);
        Assert.True(
            harness.Wire.ServerboundPlay(Identifier.Minecraft(identifier)) >= 0,
            $"minecraft:{identifier} should be registered at protocol {protocol} for this test to mean anything");

        bool capable = identifier switch
        {
            "edit_book" => harness.Services.Capabilities.CanEditBook,
            "sign_update" => harness.Services.Capabilities.CanUpdateSign,
            _ => throw new ArgumentOutOfRangeException(nameof(identifier)),
        };

        Assert.False(capable);
    }

    /// <summary>Every send that CAN be unavailable is unavailable somewhere, so the sweep's failure branch is exercised rather than dead.</summary>
    /// <remarks>
    /// The exempt names are sends whose packets every supported protocol carries in a sendable form: <c>container_button_click</c> is bound from 1.8 and registered on every dataset; <c>custom_payload</c> and <c>teleport_to_entity</c> likewise (the latter is bound from 1.8 and also reachable through the <c>spectate</c> alias); and place/use route across the 1.9 boundary so at least one of <c>use_item_on</c>/<c>use_item</c>/<c>block_place</c> is always live. They are listed by name so that a version which later loses one turns this into a failure instead of a silent exemption.
    /// <para><c>RenameItemAsync</c> belongs to that set. It routes across the 1.13 boundary the way place/use routes across 1.9, and the partition is read from the protocol boundary: <c>MC|ItemName</c> is the legacy route through 1.12.2, while nothing binds serverbound <c>rename_item</c> below 393. The supported protocol list steps from 340 to 393, so every supported protocol is on exactly one side of that boundary. <see cref="AnvilRenameTransportTests.EveryShippedProtocol_HasExactlyOneRenameRoute"/> asserts that partition directly, so this exemption is earned rather than declared.</para>
    /// </remarks>
    [Fact]
    public void EverySend_IsUnavailableOnAtLeastOneShippedProtocol()
    {
        string[] alwaysAvailable =
        [
            "RenameItemAsync",
            "ClickContainerButtonAsync",
            "PlaceBlockAsync",
            "UseItemAsync",
            "SendPluginMessageAsync",
            "SpectatorTeleportAsync",
        ];

        foreach (SendCase send in Cases)
        {
            bool anyFalse = false;
            foreach (JavaVersion version in JavaVersions.All)
            {
                var harness = new Harness(version.Version.Protocol);
                if (!send.Capable(harness.Services.Capabilities))
                {
                    anyFalse = true;
                    break;
                }
            }

            bool exempt = Array.IndexOf(alwaysAvailable, send.Name) >= 0;
            Assert.True(
                anyFalse == !exempt,
                exempt
                    ? $"{send.Name} is listed as always available but is unavailable on some protocol."
                    : $"{send.Name} is available everywhere; its failure path is untested.");
        }
    }

    /// <summary>Each send is also available somewhere, so the sweep is not asserting a dead surface.</summary>
    [Fact]
    public void EverySend_IsAvailableOnAtLeastOneShippedProtocol()
    {
        foreach (SendCase send in Cases)
        {
            bool anyTrue = false;
            foreach (JavaVersion version in JavaVersions.All)
            {
                var harness = new Harness(version.Version.Protocol);
                if (send.Capable(harness.Services.Capabilities))
                {
                    anyTrue = true;
                    break;
                }
            }

            Assert.True(anyTrue, $"{send.Name} is available nowhere.");
        }
    }

    /// <summary><c>PlaceRecipeByNameAsync</c> is available across the whole band the by-name record is bound for, across the complete band in its binding table. That table covers 1.13-1.21.1, including protocols 393-404 and 764-767.</summary>
    [Theory]
    [InlineData(393, true)]   // 1.13: bound by-name, refused by the old >= 477 gate
    [InlineData(477, true)]
    [InlineData(763, true)]
    [InlineData(764, true)]   // 1.20.2: bound by-name, refused by the old < 764 gate
    [InlineData(767, true)]
    [InlineData(768, false)]  // 1.21.2 moved to the network-id form
    [InlineData(340, false)]  // 1.12 sends a numeric recipe id the by-name record cannot represent
    [InlineData(47, false)]
    public void PlaceRecipeByName_FollowsTheBindingTable(int protocol, bool expected)
    {
        var harness = new Harness(protocol);
        Assert.Equal(expected, harness.Services.Capabilities.CanPlaceRecipeByName);
    }

    /// <summary>The network-id form is the mirror: 1.21.2+ only.</summary>
    [Theory]
    [InlineData(767, false)]
    [InlineData(768, true)]
    [InlineData(776, true)]
    public void PlaceRecipe_IsTheNetworkIdFormOnly(int protocol, bool expected)
    {
        var harness = new Harness(protocol);
        Assert.Equal(expected, harness.Services.Capabilities.CanPlaceRecipe);
    }

    private sealed record SendCase(string Name, Func<ClientActionCapabilities, bool> Capable, Func<Harness, Task> Invoke);

    private static readonly SendCase[] Cases =
    [
        new("ClickContainerButtonAsync",
            c => c.CanClickContainerButton,
            h => h.Inventory.ClickContainerButtonAsync(1)),
        new("RenameItemAsync",
            c => c.CanRenameItem,
            h => h.Inventory.RenameItemAsync("T4ANVIL")),
        new("EditBookAsync",
            c => c.CanEditBook,
            h => h.Inventory.EditBookAsync(0, ["page one"], "title")),
        new("PlaceRecipeAsync",
            c => c.CanPlaceRecipe,
            h => h.Inventory.PlaceRecipeAsync(3)),
        new("PlaceRecipeByNameAsync",
            c => c.CanPlaceRecipeByName,
            h => h.Inventory.PlaceRecipeByNameAsync(Identifier.Minecraft("stick"))),
        new("UpdateSignAsync",
            c => c.CanUpdateSign,
            h => h.Interaction.UpdateSignAsync(new BlockPos(1, 2, 3), ["a", "b", "c", "d"])),
        new("SpectatorTeleportAsync",
            c => c.CanSpectatorTeleport,
            h => h.Interaction.SpectatorTeleportAsync(Guid.NewGuid())),
        new("UpdateCommandBlockAsync",
            c => c.CanUpdateCommandBlock,
            h => h.Interaction.UpdateCommandBlockAsync(new BlockPos(1, 2, 3), "/say hi")),
        new("PlaceBlockAsync",
            c => c.CanPlaceBlock,
            h => h.Interaction.PlaceBlockAsync(new BlockPos(1, 2, 3), Direction.Up, new Vec3d(0.5, 0.5, 0.5))),
        new("UseItemAsync",
            c => c.CanUseItem,
            h => h.Interaction.UseItemAsync()),
        new("SubmitDialogAsync",
            c => c.CanSubmitDialog,
            h => h.Dialog.SubmitActionAsync(Identifier.Parse("example:claim"))),
        new("SendPluginMessageAsync",
            c => c.CanSendPluginMessage,
            h => h.Channels.SendAsync(Identifier.Minecraft("brand"), new byte[] { 1, 2, 3 }, CancellationToken.None).AsTask()),
    ];

    private sealed class Harness
    {
        // One scheduler for the whole sweep: none of these sends touch it, and a fresh one per harness would start a drain task per (protocol, case) pair.
        private static readonly Umpk.Hosting.ChannelSessionScheduler SharedScheduler = new();

        public Harness(int protocol)
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
            JavaVersion resolved = version!;
            Wire = new WireIndex(resolved);
            var state = new ClientState(new ClientFeatures().Normalized());
            Services = new ClientSessionServices
            {
                Version = resolved,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = state,
                Wire = Wire,
                Logger = NullLogger.Instance,
                Scheduler = SharedScheduler,
            };

            Inventory = new InventoryActions(Sink, Services);
            Interaction = new InteractionActions(Sink, Services, new SequenceTracker());
            Dialog = new DialogActions(Sink, Services, TestChatActions.Build(Sink, Services));
            Channels = new PluginChannelManager(Sink, Wire, protocol, NullLogger.Instance);

            // A dialog answer needs something showing, so the payload builder has inputs to write.
            var notice = new NbtCompound();
            notice.PutString("type", "minecraft:notice");
            notice.PutString("title", "u30");
            state.Dialogs.Show(DialogNbt.Read(notice), null);
        }

        public RecordingSink Sink { get; } = new();

        public WireIndex Wire { get; }

        public ClientSessionServices Services { get; }

        public InventoryActions Inventory { get; }

        public InteractionActions Interaction { get; }

        public DialogActions Dialog { get; }

        public PluginChannelManager Channels { get; }
    }
}
