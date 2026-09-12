using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Dialogs;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>A dialog shown during the CONFIGURATION phase must reach the same client surface as a play-phase one: the same <c>ClientState.Dialogs</c>, the same events, and an answerable response. The wire forms differ (configuration show_dialog is a bare NBT body, play show_dialog is a holder), so this pins that the difference stops at the codec and never reaches the state model.</summary>
public sealed class ConfigurationDialogApplierTests
{
    private static JavaVersion DialogVersion => JavaVersions.V1_21_6;

    // A notice dialog with a custom-click button, so both the parsed shape and the response id are non-trivial. An empty body would leave nothing to compare between the two phases.
    private static NbtCompound SampleDialog()
    {
        var action = new NbtCompound();
        action.PutString("type", "minecraft:custom");
        action.PutString("id", "example:accept");
        var additions = new NbtCompound();
        additions.PutString("source", "config_notice");
        action.Put("additions", additions);

        var button = new NbtCompound();
        button.PutString("label", "Accept");
        button.Put("action", action);

        var root = new NbtCompound();
        root.PutString("type", "minecraft:notice");
        root.PutString("title", "Server rules");
        root.Put("action", button);
        return root;
    }

    // The two phases must land identical state from the same body.
    [Fact]
    public async Task ConfigShowDialog_LandsSameStateAsPlayShowDialog()
    {
        var configuration = new ApplierHarness(DialogVersion);
        var play = new ApplierHarness(DialogVersion);

        DialogShown? configEvent = null;
        configuration.Events.Subscribe<DialogShown>(e => configEvent = e);

        await configuration.ApplyAsync(new ClientboundConfigShowDialogPacket(SampleDialog()));
        await play.ApplyAsync(new ClientboundShowDialogPacket(null, SampleDialog()));

        Assert.True(configuration.State.Dialogs.IsShowing);
        Assert.Null(configuration.State.Dialogs.CurrentRegistryId);

        Dialog fromConfig = Assert.IsType<Dialog>(configuration.State.Dialogs.Current);
        Dialog fromPlay = Assert.IsType<Dialog>(play.State.Dialogs.Current);

        Assert.Equal(Identifier.Minecraft("notice"), fromConfig.Type);
        Assert.Equal("Server rules", fromConfig.Title.ToPlainText());
        Assert.Equal(fromPlay.Type, fromConfig.Type);
        Assert.Equal(fromPlay.Title.ToPlainText(), fromConfig.Title.ToPlainText());
        Assert.Equal(
            fromPlay.Buttons.Select(b => b.Action?.Id),
            fromConfig.Buttons.Select(b => b.Action?.Id));

        // The same event a play-phase dialog raises, with no registry id (configuration cannot express one).
        Assert.NotNull(configEvent);
        Assert.Null(configEvent!.RegistryId);
    }

    [Fact]
    public async Task ConfigClearDialog_ClearsStateAndPublishes()
    {
        var harness = new ApplierHarness(DialogVersion);
        bool cleared = false;
        harness.Events.Subscribe<DialogCleared>(_ => cleared = true);

        await harness.ApplyAsync(new ClientboundConfigShowDialogPacket(SampleDialog()));
        Assert.True(harness.State.Dialogs.IsShowing);

        await harness.ApplyAsync(new ClientboundConfigClearDialogPacket());

        Assert.False(harness.State.Dialogs.IsShowing);
        Assert.Null(harness.State.Dialogs.Current);
        Assert.True(cleared);
    }

    // A play-phase clear must close a configuration-phase dialog and vice versa: one surface, so the pair cannot be phase-scoped.
    [Fact]
    public async Task ClearIsPhaseAgnostic()
    {
        var a = new ApplierHarness(DialogVersion);
        await a.ApplyAsync(new ClientboundConfigShowDialogPacket(SampleDialog()));
        await a.ApplyAsync(new ClientboundClearDialogPacket());
        Assert.False(a.State.Dialogs.IsShowing);

        var b = new ApplierHarness(DialogVersion);
        await b.ApplyAsync(new ClientboundShowDialogPacket(null, SampleDialog()));
        await b.ApplyAsync(new ClientboundConfigClearDialogPacket());
        Assert.False(b.State.Dialogs.IsShowing);
    }

    // The response has to be sent in the phase the connection is in: custom_click_action is registered in both phases under separate identities, and the wrong one has no wire id in the live phase.
    [Theory]
    [InlineData(ProtocolPhase.Configuration)]
    [InlineData(ProtocolPhase.Play)]
    public async Task SubmitSendsThePacketForTheLivePhase(ProtocolPhase phase)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var recorder = new RecordingSink();
        var services = new ClientSessionServices
        {
            Version = DialogVersion,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(DialogVersion),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
            CurrentPhase = () => phase,
        };
        var actions = new DialogActions(recorder, services, TestChatActions.Build(recorder, services));

        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await actions.SubmitAsync(dialog.Buttons[0]);

        object sent = Assert.Single(recorder.Packets);
        if (phase == ProtocolPhase.Configuration)
        {
            var config = Assert.IsType<ServerboundConfigCustomClickActionPacket>(sent);
            Assert.Equal(Identifier.Parse("example:accept"), config.Id);
            Assert.Equal("config_notice", Assert.IsType<NbtCompound>(config.Payload).GetString("source"));
            Assert.Equal(ProtocolPhase.Configuration, config.Type.Phase);
        }
        else
        {
            var play = Assert.IsType<ServerboundCustomClickActionPacket>(sent);
            Assert.Equal(Identifier.Parse("example:accept"), play.Id);
            Assert.Equal("config_notice", Assert.IsType<NbtCompound>(play.Payload).GetString("source"));
            Assert.Equal(ProtocolPhase.Play, play.Type.Phase);
        }

        Assert.False(state.Dialogs.IsShowing);
    }
}
