using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.Tests.Inventory;

public sealed class BedrockContainerLifecycleTests
{
    [Fact]
    public void OpenRetainsTypeCoordinatesAndEntityId()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        var packet = new BedrockContainerOpenPacket(1, 0, 10, 64, -20, -1L);

        windows.NoteOpen(packet, windows.Generation);

        State.Inventory.BedrockOpenContainer snapshot = windows.GetWindow(1)!.Value;
        Assert.Equal(1, snapshot.WindowId);
        Assert.Equal(0, snapshot.WindowType);
        Assert.Equal(10, snapshot.X);
        Assert.Equal(64, snapshot.Y);
        Assert.Equal(-20, snapshot.Z);
        Assert.Equal(-1L, snapshot.EntityUniqueId);
    }

    [Fact]
    public void ReopenReplacesWindowStateWithoutGrowingCount()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L), windows.Generation);
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 8, 5, 6, 7, 8L), windows.Generation);

        Assert.Equal(1, windows.WindowCount);
        Assert.Equal(8, windows.GetWindow(1)!.Value.WindowType);
        Assert.Equal(8L, windows.GetWindow(1)!.Value.EntityUniqueId);
    }

    [Fact]
    public void CloseRemovesOnlyTheAddressedWindow()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L), windows.Generation);
        windows.NoteOpen(new BedrockContainerOpenPacket(2, 8, 5, 6, 7, 8L), windows.Generation);

        Assert.True(windows.TryClose(1, windows.Generation));

        Assert.Null(windows.GetWindow(1));
        Assert.NotNull(windows.GetWindow(2));
    }

    [Fact]
    public void DuplicateAndUnknownCloseReportFalseWithoutMutation()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L), windows.Generation);

        Assert.False(windows.TryClose(9, windows.Generation));
        Assert.True(windows.TryClose(1, windows.Generation));
        Assert.False(windows.TryClose(1, windows.Generation));
        Assert.Null(windows.GetWindow(9));
        Assert.Null(windows.GetWindow(1));
    }

    [Fact]
    public void StaleGenerationLeavesReplacementWindowsUntouched()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L), 1);
        windows.Invalidate();
        windows.NoteOpen(new BedrockContainerOpenPacket(2, 8, 5, 6, 7, 8L), windows.Generation);

        windows.NoteOpen(new BedrockContainerOpenPacket(3, 0, 0, 0, 0, 0L), 1);
        Assert.False(windows.TryClose(2, 1));

        Assert.Null(windows.GetWindow(1));
        Assert.Null(windows.GetWindow(3));
        Assert.NotNull(windows.GetWindow(2));
    }

    [Fact]
    public void InvalidateClearsWindowsAndRotatesGeneration()
    {
        var windows = new State.Inventory.BedrockContainerWindows();
        windows.NoteOpen(new BedrockContainerOpenPacket(1, 0, 1, 2, 3, 4L), windows.Generation);
        int generationBefore = windows.Generation;

        windows.Invalidate();

        Assert.Equal(0, windows.WindowCount);
        Assert.True(windows.Generation > generationBefore);
        Assert.Null(windows.GetWindow(1));
    }
}
