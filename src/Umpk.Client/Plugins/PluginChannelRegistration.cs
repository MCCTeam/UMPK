namespace Umpk.Client.Plugins;

/// <summary>A live registration for a custom plugin channel. Dispose to stop receiving on the channel and to send the <c>minecraft:unregister</c> notification.</summary>
public sealed class PluginChannelRegistration : IDisposable
{
    private readonly Action _onDispose;
    private int _disposed;

    internal PluginChannelRegistration(Identifier channel, Action onDispose)
    {
        Channel = channel;
        _onDispose = onDispose;
    }

    /// <summary>The channel this registration listens on.</summary>
    public Identifier Channel { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _onDispose();

    }
}
