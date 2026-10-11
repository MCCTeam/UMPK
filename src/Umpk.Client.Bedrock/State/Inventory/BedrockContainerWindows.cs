using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Carries the lifecycle identity of one open window from its protocol-2169 open packet: its type, block position, and container entity.</summary>
/// <remarks>The open packet does not supply a FullContainerName; content addressing belongs to the inventory store and the session window map.</remarks>
internal readonly record struct BedrockOpenContainer(int WindowId, sbyte WindowType, int X, int Y, int Z, long EntityUniqueId);

/// <summary>Tracks which container windows the server has opened in the current generation so close and invalidation can retire them exactly once.</summary>
internal sealed class BedrockContainerWindows
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, BedrockOpenContainer> _windows = new();
    private int _generation = 1;

    /// <summary>Gets the current window generation, advanced by every invalidation so stale open and close packets can be recognized.</summary>
    internal int Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    /// <summary>Gets how many container windows are currently open.</summary>
    internal int WindowCount
    {
        get
        {
            lock (_gate)
                return _windows.Count;
        }
    }

    /// <summary>Records one protocol-2169 container-open packet under the current generation and ignores opens from a superseded generation.</summary>
    internal void NoteOpen(BedrockContainerOpenPacket packet, int generation)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (generation != _generation)
                return;
            _windows[packet.WindowId] = new BedrockOpenContainer(packet.WindowId, packet.WindowType, packet.X, packet.Y, packet.Z, packet.EntityUniqueId);
        }
    }

    /// <summary>Closes one window under the current generation and reports whether it was open.</summary>
    internal bool TryClose(int windowId, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return false;
            return _windows.Remove(windowId);
        }
    }

    /// <summary>Reads one open window by identifier, or null when that window is not open.</summary>
    internal BedrockOpenContainer? GetWindow(int windowId)
    {
        lock (_gate)
            return _windows.TryGetValue(windowId, out BedrockOpenContainer open) ? open : null;
    }

    /// <summary>Drops every open window and advances the generation after a lifecycle boundary.</summary>
    internal void Invalidate()
    {
        lock (_gate)
        {
            _windows.Clear();
            _generation++;
        }
    }
}
