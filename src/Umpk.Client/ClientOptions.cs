using Umpk.Protocol.Java;

namespace Umpk.Client;

/// <summary>Bindable settings for a client session. This POCO carries only value-typed, binder-friendly options; policy objects that hold delegates (reconnect, resource-pack, version negotiation) live on <see cref="ClientPolicies"/> and are code-configured. UMPK libraries never bind configuration themselves.</summary>
public sealed class ClientOptions
{
    /// <summary>Minimum spacing between outbound chat messages (cooldown). Zero disables the throttle.</summary>
    public TimeSpan ChatCooldown { get; set; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Everything announced to the server in client information: locale, view distance, chat visibility and colors, displayed skin parts, main hand, text filtering, server listing, and particle status. The negotiated version decides how much of it reaches the wire.</summary>
    public ClientInformationOptions ClientInformation { get; set; } = new();

    /// <summary>
    /// The client brand announced to the server on the <c>minecraft:brand</c> custom payload, or <see langword="null"/> to announce none. A vanilla client always announces one ("vanilla"), so a session that announces nothing is distinguishable from every real client by its silence alone; null is nonetheless the default here because announcing on a library's behalf is the host's decision, not the library's.
    /// <para>Where it goes depends on the era, and both are handled: 1.20.2+ sends it in the configuration phase, right after <c>login_acknowledged</c> and before the client information; 1.8 through 1.20.1 send it in play, on the era's own channel (<c>MC|Brand</c> below 1.13, <c>minecraft:brand</c> from 1.13). See <see cref="Internal.ServerBrandPayload"/>, which reads the server's own brand off the same three shapes.</para>
    /// </summary>
    public string? ClientBrand { get; set; }

    /// <summary>How long an action's protocol round trip may wait before failing.</summary>
    public TimeSpan ActionTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long <see cref="UmpkClient.ConnectAsync"/> may run before failing.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the session may go without a single inbound frame before the connection is declared lost. Zero or less switches the backstop off. The default matches the standard client's 30-second read timeout. This is safe against a healthy quiet server because the server keep-alives every 15 seconds, including during a <c>/tick freeze</c>. A timeout surfaces as <see cref="Events.Disconnected"/> carrying <see cref="Umpk.Protocol.Java.CloseReason.IdleTimeout"/>, so a reconnect policy classifies it as a transport fault, not a kick.</summary>
    public TimeSpan ReadIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Debug-only watchdog threshold. When a single session-loop work item (event handler, tick, applier) runs longer than this, a warning is logged. Zero disables the watchdog.</summary>
    public TimeSpan LoopStallThreshold { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Default bounded capacity of a per-subscription event stream channel.</summary>
    public int DefaultStreamCapacity { get; set; } = 256;

    /// <summary>Whether the client sends its position every tick automatically once physics runs.</summary>
    public bool AutoSendPosition { get; set; } = true;

    /// <summary>Whether keep-alive packets are answered automatically.</summary>
    public bool AutoKeepAlive { get; set; } = true;

    /// <summary>How long the status ping may take before <see cref="UmpkClientBuilder.BuildForAsync"/> gives up auto-detecting the server's protocol version. Only consulted by <c>BuildForAsync</c>; matches <see cref="Umpk.Protocol.Java.JavaStatusOptions"/>'s own default.</summary>
    public TimeSpan StatusPingTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
