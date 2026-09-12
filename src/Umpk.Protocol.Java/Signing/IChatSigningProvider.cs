namespace Umpk.Protocol.Java.Signing;

/// <summary>The host-provided source of chat-signing certificates. Supplied through <c>UmpkClientBuilder.UseChatSigning(IChatSigningProvider)</c> (in <c>Umpk.Client</c>, which depends on this package rather than the other way around) to enable signed chat on signing-era versions. The client owns constructing the per-session <see cref="ChatSigningSession"/> and deriving the <see cref="ChatSignatureEra"/> from the negotiated version; the provider only yields the player key material.</summary>
/// <remarks>
/// <para>This interface lives in <c>Umpk.Protocol.Java.Signing</c> because its return type (<see cref="ChatSignatureEra"/>) and its session type (<see cref="ChatSigningSession"/>) are already Protocol.Java concepts, and its only implementing caller, <c>UmpkClient</c>'s internal chat-signing coordinator, already depends on this package. Adding a dependency from <c>Umpk.Auth</c> to <c>Umpk.Client</c> just to reach this interface would drag the whole game/physics/pathfinding surface into auth and risk a cycle the moment <c>Umpk.Client</c> wants <c>Umpk.Auth</c>. The CALLING CADENCE described below, though, is still <c>Umpk.Client</c>'s: it is the one client implementation in this codebase, and the only one this contract has been proven against.</para>
/// <para><b>Calling cadence.</b> Implementations should size their caching against this contract:</para>
/// <list type="bullet">
/// <item><description>
/// Once at session start, on every signing-era version. On 1.19 and 1.19.1 that call is made before the login hello, because the profile key rides the hello and nothing later can carry a replacement.
/// </description></item>
/// <item><description>
/// From 1.19.3, at most once an hour from the session tick while the held key is past its <see cref="PlayerCertificates.RefreshedAfter"/> instant, or while no key is held at all. The floor is one hour and it is armed before the call, so a provider that fails, hangs or returns null is not retried sooner for either reason.
/// </description></item>
/// <item><description>
/// From 1.19.3, when a send discovers the held certificates have EXPIRED: ONCE immediately, jumping the floor above, and then subject to that SAME hourly floor again until something usable is installed. The one immediate attempt is deliberate - an expired key is still signed with, and a server drops those messages silently from this side, so the very next message gets one chance to recover without waiting out an interval - but it is one attempt per EPISODE, not one per send: a certificate source that stays broken is not hammered at message rate. "Something usable" means non-null AND not itself already expired; an implementation that keeps returning the same dead answer, or any other answer that is already expired on arrival, is treated exactly like no answer at all and is asked again on the same floor, not treated as having recovered.
/// </description></item>
/// </list>
/// <para>Returning null means "no certificates available", and the session falls back to the unsigned send path exactly as an offline session does. Throwing is safe: every exception this method can produce is caught and treated the same as returning null, so a failing implementation degrades the send rather than faulting it. A call may also be ABANDONED: the client stops waiting after 30 seconds and proceeds without an answer, and a result produced after that point is discarded and never installed. The supplied <see cref="CancellationToken"/> is cancelled at that moment, so an implementation that observes it can release its resources, but observing it is not required for the client to make progress.</para>
/// <para><b>An implementation must not send chat or run a command from inside <see cref="GetCertificatesAsync"/>, and must not block on anything that does.</b> The client holds the chat-signing lock across the whole of resolve, sign and send, so that a profile-key rotation cannot announce itself between a message's signature and its frame; a provider that re-enters the send path from within this call would wait on a lock its own caller holds. Every other client API is safe to call. This constraint is stated here because the lock ownership is not visible to an implementation.</para>
/// </remarks>
public interface IChatSigningProvider
{
    /// <summary>Returns the current player certificates, or null when none are available (unsigned fallback). See the interface remarks for exactly when this is called, which is not only at session start: an implementation should cache a successful answer, because the expired-key path asks once immediately and then once an hour, not once per send, until it gets an answer that is both non-null and not itself already expired.</summary>
    ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken);
}
