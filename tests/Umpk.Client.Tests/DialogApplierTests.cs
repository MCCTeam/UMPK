using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

/// <summary>The client-side dialog state (1.21.6+): show/clear application, the shape a consumer renders, the serverbound response and cancel actions, and the era gate that keeps everything inert on a version without dialog packets.</summary>
public sealed class DialogApplierTests
{
    private static JavaVersion DialogVersion => JavaVersions.V1_21_6;

    private static JavaVersion PreDialogVersion => JavaVersions.V1_21_5;

    // A representative multi-action dialog: a title, a body message, three inputs covering the kinds a consumer must render differently, and two buttons (one custom-click, one plain close) plus an exit action.
    private static NbtCompound SampleDialog()
    {
        var body = new NbtCompound();
        body.PutString("type", "minecraft:plain_message");
        body.PutString("contents", "Choose your reward.");
        var bodyList = new NbtList(NbtTagType.Compound);
        bodyList.Add(body);

        var textInput = new NbtCompound();
        textInput.PutString("type", "minecraft:text");
        textInput.PutString("key", "note");
        textInput.PutString("label", "Note");
        textInput.PutString("initial", "hello");

        var boolInput = new NbtCompound();
        boolInput.PutString("type", "minecraft:boolean");
        boolInput.PutString("key", "subscribe");
        boolInput.PutBool("initial", true);
        boolInput.PutString("on_true", "yes");
        boolInput.PutString("on_false", "no");

        var optionA = new NbtCompound();
        optionA.PutString("id", "sword");
        var optionB = new NbtCompound();
        optionB.PutString("id", "shield");
        optionB.PutBool("initial", true);
        var optionList = new NbtList(NbtTagType.Compound);
        optionList.Add(optionA);
        optionList.Add(optionB);

        var choiceInput = new NbtCompound();
        choiceInput.PutString("type", "minecraft:single_option");
        choiceInput.PutString("key", "reward");
        choiceInput.Put("options", optionList);

        var rangeInput = new NbtCompound();
        rangeInput.PutString("type", "minecraft:number_range");
        rangeInput.PutString("key", "amount");
        rangeInput.PutFloat("start", 1f);
        rangeInput.PutFloat("end", 10f);
        rangeInput.PutFloat("initial", 4f);

        var inputs = new NbtList(NbtTagType.Compound);
        inputs.Add(textInput);
        inputs.Add(boolInput);
        inputs.Add(choiceInput);
        inputs.Add(rangeInput);

        var additions = new NbtCompound();
        additions.PutString("source", "reward_menu");

        var claimAction = new NbtCompound();
        claimAction.PutString("type", "minecraft:dynamic/custom");
        claimAction.PutString("id", "example:claim");
        claimAction.Put("additions", additions);

        var claim = new NbtCompound();
        claim.PutString("label", "Claim");
        claim.PutString("tooltip", "Take the reward");
        claim.PutInt("width", 120);
        claim.Put("action", claimAction);

        var dismiss = new NbtCompound();
        dismiss.PutString("label", "Later");

        var actions = new NbtList(NbtTagType.Compound);
        actions.Add(claim);
        actions.Add(dismiss);

        var exitAction = new NbtCompound();
        exitAction.PutString("type", "minecraft:custom");
        exitAction.PutString("id", "example:dismissed");

        var exit = new NbtCompound();
        exit.PutString("label", "Close");
        exit.Put("action", exitAction);

        var root = new NbtCompound();
        root.PutString("type", "minecraft:multi_action");
        root.PutString("title", "Reward");
        root.PutString("external_title", "Rewards");
        root.PutBool("can_close_with_escape", true);
        root.Put("body", bodyList);
        root.Put("inputs", inputs);
        root.Put("actions", actions);
        root.Put("exit_action", exit);
        return root;
    }

    private static (DialogActions Actions, RecordingSink Sink, ClientState State, ListLogger Logger) BuildActions(JavaVersion version)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var recorder = new RecordingSink();
        var logger = new ListLogger();
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = logger,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        return (new DialogActions(recorder, services, TestChatActions.Build(recorder, services)), recorder, state, logger);
    }

    [Fact]
    public async Task ShowDialog_Inline_PopulatesStateAndPublishes()
    {
        var harness = new ApplierHarness(DialogVersion);
        DialogShown? shown = null;
        harness.Events.Subscribe<DialogShown>(e => shown = e);

        await harness.ApplyAsync(new ClientboundShowDialogPacket(null, SampleDialog()));

        Assert.True(harness.State.Dialogs.IsShowing);
        Assert.NotNull(harness.State.Dialogs.Current);
        Assert.Null(harness.State.Dialogs.CurrentRegistryId);
        Assert.NotNull(shown);
        Assert.Null(shown!.RegistryId);
    }

    [Fact]
    public async Task ClearDialog_EmptiesStateAndPublishes()
    {
        var harness = new ApplierHarness(DialogVersion);
        bool cleared = false;
        harness.Events.Subscribe<DialogCleared>(_ => cleared = true);

        await harness.ApplyAsync(new ClientboundShowDialogPacket(null, SampleDialog()));
        Assert.True(harness.State.Dialogs.IsShowing);

        await harness.ApplyAsync(new ClientboundClearDialogPacket());

        Assert.False(harness.State.Dialogs.IsShowing);
        Assert.Null(harness.State.Dialogs.Current);
        Assert.Null(harness.State.Dialogs.CurrentRegistryId);
        Assert.True(cleared);
    }

    // The registry form carries only an index, and the dialog registry is not modelled: the state must report "a dialog is open that I cannot render" rather than silently reporting no dialog.
    [Fact]
    public async Task ShowDialog_RegistryReference_RecordsIdWithoutBody()
    {
        var harness = new ApplierHarness(DialogVersion);
        await harness.ApplyAsync(new ClientboundShowDialogPacket(7, null));

        Assert.True(harness.State.Dialogs.IsShowing);
        Assert.Null(harness.State.Dialogs.Current);
        Assert.Equal(7, harness.State.Dialogs.CurrentRegistryId);
    }

    [Fact]
    public async Task ShowDialog_StateShape_InputsAndButtons()
    {
        var harness = new ApplierHarness(DialogVersion);
        await harness.ApplyAsync(new ClientboundShowDialogPacket(null, SampleDialog()));

        Dialog dialog = Assert.IsType<Dialog>(harness.State.Dialogs.Current);

        Assert.Equal(Identifier.Minecraft("multi_action"), dialog.Type);
        Assert.Equal("Reward", dialog.Title.ToPlainText());
        Assert.Equal("Rewards", dialog.ExternalTitle?.ToPlainText());
        Assert.True(dialog.CanCloseWithEscape);

        DialogBodyElement element = Assert.Single(dialog.Body);
        Assert.Equal(Identifier.Minecraft("plain_message"), element.Type);
        Assert.Equal("Choose your reward.", element.Contents?.ToPlainText());
        Assert.Equal(200, element.Width); // vanilla default when the server omits it

        Assert.Equal(4, dialog.Inputs.Count);

        Assert.Equal("note", dialog.Inputs[0].Key);
        Assert.Equal(DialogInputKind.Text, dialog.Inputs[0].Kind);
        Assert.Equal("Note", dialog.Inputs[0].Label?.ToPlainText());
        Assert.Equal("hello", dialog.Inputs[0].InitialValue);

        // A boolean submits its on_true/on_false strings, not "true"/"false".
        Assert.Equal(DialogInputKind.Boolean, dialog.Inputs[1].Kind);
        Assert.Equal("yes", dialog.Inputs[1].InitialValue);

        Assert.Equal(DialogInputKind.SingleOption, dialog.Inputs[2].Kind);
        Assert.Equal(["sword", "shield"], dialog.Inputs[2].Options.Select(o => o.Id));
        Assert.Equal("shield", dialog.Inputs[2].InitialValue); // the option flagged initial

        Assert.Equal(DialogInputKind.NumberRange, dialog.Inputs[3].Kind);
        Assert.Equal(new DialogNumberRange(1f, 10f, null, 4f), dialog.Inputs[3].Range);
        Assert.Equal("4", dialog.Inputs[3].InitialValue);

        Assert.Equal(2, dialog.Buttons.Count);
        Assert.Equal("Claim", dialog.Buttons[0].Label.ToPlainText());
        Assert.Equal("Take the reward", dialog.Buttons[0].Tooltip?.ToPlainText());
        Assert.Equal(120, dialog.Buttons[0].Width);
        Assert.Equal(DialogActionKind.DynamicCustom, dialog.Buttons[0].Action?.Kind);
        Assert.Equal(Identifier.Parse("example:claim"), dialog.Buttons[0].Action?.Id);

        Assert.Equal("Later", dialog.Buttons[1].Label.ToPlainText());
        Assert.Equal(150, dialog.Buttons[1].Width); // vanilla default button width
        Assert.Null(dialog.Buttons[1].Action);

        Assert.Equal(DialogActionKind.Custom, dialog.ExitAction?.Action?.Kind);
        Assert.Equal(Identifier.Parse("example:dismissed"), dialog.ExitAction?.Action?.Id);
    }

    // A notice and a confirmation put their buttons under different keys; both normalise into Buttons.
    [Fact]
    public void DialogNbt_NoticeAndConfirmation_NormaliseIntoButtons()
    {
        var ok = new NbtCompound();
        ok.PutString("label", "OK");
        var notice = new NbtCompound();
        notice.PutString("type", "minecraft:notice");
        notice.PutString("title", "Heads up");
        notice.Put("action", ok);

        Dialog noticeDialog = Assert.IsType<Dialog>(DialogNbt.Read(notice));
        Assert.Equal("OK", Assert.Single(noticeDialog.Buttons).Label.ToPlainText());

        var yes = new NbtCompound();
        yes.PutString("label", "Yes");
        var no = new NbtCompound();
        no.PutString("label", "No");
        var confirmation = new NbtCompound();
        confirmation.PutString("type", "minecraft:confirmation");
        confirmation.PutString("title", "Sure?");
        confirmation.Put("yes", yes);
        confirmation.Put("no", no);

        Dialog confirmDialog = Assert.IsType<Dialog>(DialogNbt.Read(confirmation));
        Assert.Equal(["Yes", "No"], confirmDialog.Buttons.Select(b => b.Label.ToPlainText()));
    }

    [Fact]
    public async Task SubmitAsync_SendsCustomClickWithAdditionsAndInputDefaults()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(DialogVersion);
        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await actions.SubmitAsync(dialog.Buttons[0]);

        var sent = Assert.IsType<ServerboundCustomClickActionPacket>(Assert.Single(sink.Packets));
        Assert.Equal(Identifier.Parse("example:claim"), sent.Id);

        var payload = Assert.IsType<NbtCompound>(sent.Payload);
        Assert.Equal("reward_menu", payload.GetString("source")); // the action's fixed additions
        Assert.Equal("hello", payload.GetString("note"));
        Assert.Equal("yes", payload.GetString("subscribe"));
        Assert.Equal("shield", payload.GetString("reward"));
        Assert.Equal("4", payload.GetString("amount"));

        // Pressing a button closes the dialog client-side.
        Assert.False(state.Dialogs.IsShowing);
    }

    [Fact]
    public async Task SubmitAsync_SuppliedValuesOverrideDefaults()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(DialogVersion);
        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await actions.SubmitAsync(dialog.Buttons[0], new Dictionary<string, string>
        {
            ["note"] = "typed by the user",
            ["reward"] = "sword",
        });

        var sent = Assert.IsType<ServerboundCustomClickActionPacket>(Assert.Single(sink.Packets));
        var payload = Assert.IsType<NbtCompound>(sent.Payload);
        Assert.Equal("typed by the user", payload.GetString("note"));
        Assert.Equal("sword", payload.GetString("reward"));
        Assert.Equal("yes", payload.GetString("subscribe")); // untouched input keeps its default
    }

    [Fact]
    public async Task SubmitAsync_ButtonWithoutCustomAction_OnlyCloses()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(DialogVersion);
        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await actions.SubmitAsync(dialog.Buttons[1]);

        Assert.Empty(sink.Packets);
        Assert.False(state.Dialogs.IsShowing);
    }

    [Fact]
    public async Task CancelAsync_TriggersExitActionThenClears()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(DialogVersion);
        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await actions.CancelAsync();

        var sent = Assert.IsType<ServerboundCustomClickActionPacket>(Assert.Single(sink.Packets));
        Assert.Equal(Identifier.Parse("example:dismissed"), sent.Id);
        Assert.False(state.Dialogs.IsShowing);
    }

    [Fact]
    public async Task CancelAsync_WithoutExitAction_SendsNothingAndClears()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(DialogVersion);

        var plain = new NbtCompound();
        plain.PutString("type", "minecraft:notice");
        plain.PutString("title", "Just a notice");
        state.Dialogs.Show(DialogNbt.Read(plain), null);

        await actions.CancelAsync();

        Assert.Empty(sink.Packets);
        Assert.False(state.Dialogs.IsShowing);
    }

    /// <summary>On a version without dialogs, all three answer paths report failure instead of completing.</summary>
    /// <remarks>An answer that never leaves the client is indistinguishable from one the server ignored. A configuration-phase dialog can prevent configuration from completing.</remarks>
    [Fact]
    public async Task PreDialogVersion_ActionsReportFailure()
    {
        (DialogActions actions, RecordingSink sink, ClientState state, _) = BuildActions(PreDialogVersion);

        // The state can only be populated directly here: on this version no dialog packet ever arrives.
        Dialog dialog = Assert.IsType<Dialog>(DialogNbt.Read(SampleDialog()));
        state.Dialogs.Show(dialog, null);

        await Assert.ThrowsAsync<ActionNotSupportedException>(() => actions.SubmitAsync(dialog.Buttons[0]));
        await Assert.ThrowsAsync<ActionNotSupportedException>(() => actions.CancelAsync());
        await Assert.ThrowsAsync<ActionNotSupportedException>(
            () => actions.SubmitActionAsync(Identifier.Parse("example:claim")));

        Assert.Empty(sink.Packets);
    }

    // On a version without dialogs nothing ever writes the state, so it stays empty.
    [Fact]
    public async Task PreDialogVersion_StateStaysEmpty()
    {
        var harness = new ApplierHarness(PreDialogVersion);
        await harness.ApplyAsync(new ClientboundClearDialogPacket());
        Assert.False(harness.State.Dialogs.IsShowing);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));

        }
    }
}
