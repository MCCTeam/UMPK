namespace Umpk.Protocol.Java;

/// <summary>What the configuration-phase driver needs, independent of how the phase was entered. The phase is reached twice in a session's life on 1.20.2+: once out of login, and again whenever the server sends <c>minecraft:start_configuration</c> during play. Both entries use the same client behavior, so this driver takes one options record and <see cref="JavaClientLogin.RunConfigurationPhaseAsync(JavaConnection, JavaVersion, JavaConfigurationOptions, CancellationToken)"/> is the single implementation. A second copy for re-entry could drift from the login one, and the two only differ in whether the client re-announces its information.</summary>
public sealed record JavaConfigurationOptions
{
    /// <summary>
    /// Invoked, in frame order and awaited, for each configuration-phase packet the driver decodes but does not itself act on, so a host can track configuration-phase state instead of losing it.
    /// <para>The driver only decodes what it needs: the 1.21.6+ dialog pair (<c>minecraft:show_dialog</c> and <c>minecraft:clear_dialog</c>), the brand-bearing <c>minecraft:custom_payload</c>, and <c>minecraft:registry_data</c>. Other configuration traffic stays frame-level.</para>
    /// </summary>
    public Func<ObservedConfigurationPacket, CancellationToken, ValueTask>? PacketObserver { get; init; }

    /// <summary>
    /// The client-information announce to send on entering the phase, or <see langword="null"/> to send none.
    /// <para>Set at login: the client sends its information immediately after <c>login_acknowledged</c>, and the server has no client information before it. Null on a play-to-configuration re-entry, where the client sends only the acknowledgement and the server carries the existing information forward.</para>
    /// </summary>
    public ClientInformationOptions? AnnounceClientInformation { get; init; }

    /// <summary>
    /// The client brand to announce on entering the phase, or <see langword="null"/> to announce none.
    /// <para>The client sends it on the <c>minecraft:brand</c> custom payload immediately after <c>login_acknowledged</c> and immediately BEFORE the client information, in that order: the acknowledgement, then the brand payload, then <c>ServerboundClientInformationPacket</c>. The order is reproduced here because it is observable: a client is identified as much by what it says and when as by what it says it is.</para>
    /// <para>Null on a play-to-configuration re-entry, for the same reason the client information is: the client sends only the acknowledgement, and the server already has the brand from the first pass.</para>
    /// </summary>
    public string? AnnounceBrand { get; init; }

    /// <summary>The static registries to install into the codec context at the transition into the Play phase, before the first play packet is decoded. When <see langword="null"/> the codec context keeps whatever it already had.</summary>
    public Umpk.Game.Registries.RegistryAccess? PlayRegistries { get; init; }

    /// <summary>The per-session cookie store used by configuration request/store obligations.</summary>
    public CookieStore? Cookies { get; init; }

    /// <summary>Awaited at the terminal configuration frame while transport reads remain gated. The returned registry view is installed before the first play packet can decode.</summary>
    public Func<CancellationToken, ValueTask<Umpk.Game.Registries.RegistryAccess?>>? BeforePlay { get; init; }
}
