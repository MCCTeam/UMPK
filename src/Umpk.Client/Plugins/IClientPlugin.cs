namespace Umpk.Client.Plugins;

/// <summary>A client extension. One <see cref="Attach"/> call per session activation, on the session loop, before login completes. Everything the plugin acquires through its context is torn down automatically when the plugin detaches or the session ends.</summary>
public interface IClientPlugin
{
    /// <summary>A stable identifier for the plugin.</summary>
    string Id { get; }

    /// <summary>Called once per session to wire the plugin up through the provided context.</summary>
    void Attach(ClientPluginContext context);
}
