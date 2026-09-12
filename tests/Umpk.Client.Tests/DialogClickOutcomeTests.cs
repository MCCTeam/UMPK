using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Snapshots;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Dialogs;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="DialogClickOutcome"/>, <see cref="DialogActions.ClickAsync"/> and <see cref="DialogSnapshot"/> report what pressing a dialog button did. A bare success flag cannot distinguish a press that reached the server from one that only closed the dialog locally.</summary>
public sealed class DialogClickOutcomeTests
{
    private static JavaVersion DialogVersion => JavaVersions.V1_21_6;

    [Fact]
    public async Task Click_RunCommand_IsPerformedAndReachesTheServer()
    {
        (DialogActions actions, RecordingSink sink, ClientState state) = Build();
        state.Dialogs.Show(OneButtonDialog(Action(DialogActionKind.RunCommand, "/me T4RUNCLICK")), null);

        DialogClickOutcome outcome = await actions.ClickAsync(0);

        // The server never saw this command from the dialog packet: run_command is a client-side action a vanilla client performs itself by sending it as an ordinary command. Performing it is what makes the report true.
        Assert.Equal(DialogClickOutcome.CommandSent, outcome);
        var sent = Assert.IsType<ServerboundChatCommandPacket>(Assert.Single(sink.Packets));
        Assert.Equal("me T4RUNCLICK", sent.Command);
    }

    [Fact]
    public async Task Click_RunCommandWithNoCommand_IsNotPerformed()
    {
        (DialogActions actions, RecordingSink sink, ClientState state) = Build();
        state.Dialogs.Show(OneButtonDialog(Action(DialogActionKind.RunCommand, "   ")), null);

        DialogClickOutcome outcome = await actions.ClickAsync(0);

        Assert.Equal(DialogClickOutcome.ActionNotPerformed, outcome);
        Assert.Empty(sink.Packets);
    }

    [Fact]
    public async Task Click_ButtonWithNoAction_OnlyCloses()
    {
        (DialogActions actions, RecordingSink sink, ClientState state) = Build();
        state.Dialogs.Show(OneButtonDialog(action: null), null);

        DialogClickOutcome outcome = await actions.ClickAsync(0);

        Assert.Equal(DialogClickOutcome.ClosedOnly, outcome);
        Assert.Empty(sink.Packets);
        Assert.False(state.Dialogs.IsShowing);
    }

    [Theory]
    [InlineData(DialogActionKind.OpenUrl)]
    [InlineData(DialogActionKind.SuggestCommand)]
    [InlineData(DialogActionKind.CopyToClipboard)]
    [InlineData(DialogActionKind.ShowDialog)]
    public async Task Click_UnperformableClientActions_SaySo(DialogActionKind kind)
    {
        (DialogActions actions, RecordingSink sink, ClientState state) = Build();
        state.Dialogs.Show(OneButtonDialog(Action(kind, "value")), null);

        DialogClickOutcome outcome = await actions.ClickAsync(0);

        Assert.Equal(DialogClickOutcome.ActionNotPerformed, outcome);
        Assert.Empty(sink.Packets);
    }

    /// <summary>The point of the whole enum: ActionSent and CommandSent are the only outcomes that put anything on the wire; ClosedOnly and ActionNotPerformed close the dialog and send nothing.</summary>
    [Fact]
    public async Task Click_OnlyASendIsReportedAsAPress()
    {
        await AssertSendAsync(Action(DialogActionKind.Custom, value: null, id: Identifier.Minecraft("claim")), DialogClickOutcome.ActionSent, sent: true);
        await AssertSendAsync(Action(DialogActionKind.RunCommand, "/say hi"), DialogClickOutcome.CommandSent, sent: true);
        await AssertSendAsync(action: null, DialogClickOutcome.ClosedOnly, sent: false);
        await AssertSendAsync(Action(DialogActionKind.OpenUrl, "https://example.invalid"), DialogClickOutcome.ActionNotPerformed, sent: false);

        static async Task AssertSendAsync(DialogAction? action, DialogClickOutcome expected, bool sent)
        {
            (DialogActions actions, RecordingSink sink, ClientState state) = Build();
            state.Dialogs.Show(OneButtonDialog(action), null);

            DialogClickOutcome outcome = await actions.ClickAsync(0);

            Assert.Equal(expected, outcome);
            Assert.Equal(sent, sink.Packets.Count > 0);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Click_OutOfRange_ReportsNoButton(int buttonIndex)
    {
        (DialogActions actions, RecordingSink sink, ClientState state) = Build();
        state.Dialogs.Show(OneButtonDialog(Action(DialogActionKind.RunCommand, "/say hi")), null);

        DialogClickOutcome outcome = await actions.ClickAsync(buttonIndex);

        Assert.Equal(DialogClickOutcome.NoButton, outcome);
        Assert.Empty(sink.Packets);
        // Nothing was closed either: an out-of-range index touched no button at all.
        Assert.True(state.Dialogs.IsShowing);
    }

    [Fact]
    public async Task Click_WithNoDialogOpen_ReportsNoButton()
    {
        (DialogActions actions, RecordingSink sink, ClientState _) = Build();

        DialogClickOutcome outcome = await actions.ClickAsync(0);

        Assert.Equal(DialogClickOutcome.NoButton, outcome);
        Assert.Empty(sink.Packets);
    }

    // DialogSnapshot.Project

    [Fact]
    public void DialogSnapshot_ReportsRegistryReference_AsOpenButUnresolvable()
    {
        var state = new DialogState();
        state.Show(null, 42);

        DialogSnapshot? snapshot = DialogSnapshot.Project(state);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.Dialog);
        Assert.Equal(42, snapshot.RegistryId);
    }

    [Fact]
    public void DialogSnapshot_IsNull_WhenNothingIsShowing()
    {
        var state = new DialogState();

        Assert.Null(DialogSnapshot.Project(state));
    }

    // Fixtures

    private static (DialogActions Actions, RecordingSink Sink, ClientState State) Build()
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
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        ChatActions chat = TestChatActions.Build(recorder, services);
        return (new DialogActions(recorder, services, chat), recorder, state);
    }

    private static Dialog OneButtonDialog(DialogAction? action) =>
        new(
            Identifier.Minecraft("notice"),
            Component.Text("Title"),
            ExternalTitle: null,
            CanCloseWithEscape: true,
            Body: [],
            Inputs: [],
            Buttons: [new DialogButton(Component.Text("T4RUNBTN"), Tooltip: null, Width: 150, action)],
            ExitAction: null);

    private static DialogAction Action(DialogActionKind kind, string? value, Identifier? id = null) =>
        new(kind, Identifier.Minecraft(kind.ToString().ToLowerInvariant()), value, id, Additions: null);
}
