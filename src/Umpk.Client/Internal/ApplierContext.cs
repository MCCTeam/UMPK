using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Protocol.Java;

namespace Umpk.Client.Internal;

/// <summary>The capabilities an applier uses to mutate state and publish events on the session loop. Also carries the session services an applier may need (world builder, registries, physics push).</summary>
internal sealed class ApplierContext
{
    public required ClientState State { get; init; }

    public required EventBus Events { get; init; }

    public required IPacketSink Sink { get; init; }

    public required ILogger Logger { get; init; }

    public required JavaVersion Version { get; init; }

    public required ClientFeatures Features { get; init; }

    public required ClientPolicies Policies { get; init; }

    public required WireIndex Wire { get; init; }

    /// <summary>The clock appliers measure with (tick-rate estimation, keep-alive timing). Defaults to the system clock; tests substitute a controllable provider so timing-derived state is deterministic.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Resolves entity-type registry entries for spawn packets.</summary>
    public required EntityTypeResolver EntityTypes { get; init; }

    /// <summary>Resolves a semantic <c>EntityMetadataKeys</c> field to the tier-1 index it occupies on the bound version. Handed to every tracked entity at spawn: an entity built without one answers false to every typed metadata read, so the custom name, pose and health stay empty however complete the raw index-keyed store is.</summary>
    public required Umpk.Game.Entities.IMetadataKeySource MetadataKeys { get; init; }

    /// <summary>Invoked by appliers to (re)create the world when a dimension is known (login/respawn).</summary>
    public required Action<CommonWorldSetup> WorldSetup { get; init; }

    /// <summary>Invoked after self abilities/effects/gamemode change so physics conditions get repushed.</summary>
    public required Action PhysicsConditionsDirty { get; init; }

    /// <summary>Invoked after a server teleport has been resolved into an absolute self position, so the physics engine is re-seeded there. Without it the engine keeps stepping from the position it was created at and the next tick writes that back over the teleport, which is what made the client report 0/0/0 for its own position on every session that had physics enabled.</summary>
    public required Action PhysicsPositionDirty { get; init; }

    /// <summary>Invoked after a server-applied VELOCITY has been written to self state (<c>set_entity_motion</c> addressed at the local player's own entity id), so the physics engine picks it up on the next tick. Distinct from <see cref="PhysicsPositionDirty"/> on purpose: that one resets the engine the way a spawn or a teleport does, clearing fall distance, ground state, pose and collision flags, while a server velocity update changes only delta movement. Without this seam the velocity lives in a field the engine never reads and the next tick overwrites it, which is why a knockback moved the client not at all.</summary>
    public required Action PhysicsVelocityDirty { get; init; }

    /// <summary>The server tab-complete correlator; resolves pending completion requests.</summary>
    public required Umpk.Client.Commands.CommandCompletionService CommandCompletions { get; init; }

    /// <summary>The block-action sequence tracker; the block-change-ack applier completes dig/place waits.</summary>
    public required SequenceTracker Sequences { get; init; }

    /// <summary>Records the reason the session is ending. The server sends its disconnect packet and then closes the socket, so the applier has to win the race against the receive loop's transport-close record; the session keeps the FIRST reason recorded, which is why this is called from the applier rather than left to the loop.</summary>
    public required Action<DisconnectInfo> RecordDisconnect { get; init; }

    /// <summary>The per-session cookie bag answering <c>cookie_request</c> and holding <c>store_cookie</c>.</summary>
    public required Umpk.Protocol.Java.CookieStore Cookies { get; init; }

    /// <summary>
    /// Routes a decoded CONFIGURATION-phase custom payload to the plugin-channel handlers registered for its channel, and folds a <c>minecraft:register</c> announcement into the server-announced set. Returns true when either claimed it.
    /// <para>Required rather than optional so the live client cannot accept configuration payloads without a routing path that tests happen to supply only in their harness.</para>
    /// </summary>
    public required Func<Identifier, ReadOnlyMemory<byte>, bool> DispatchConfigurationChannel { get; init; }

    /// <summary>
    /// Takes the connection into the configuration phase and drives it until the server ends it, at which point the connection is back in play. Invoked by the <c>minecraft:start_configuration</c> applier AFTER it has sent the acknowledgement, because the acknowledgement is a PLAY packet and has to be encoded while the connection is still in the play phase.
    /// <para>Required rather than optional so the live client cannot acknowledge configuration without also driving the phase transition that the test harness verifies.</para>
    /// </summary>
    public required Func<CancellationToken, ValueTask> EnterConfiguration { get; init; }

    /// <summary>Runs the connect-time chat-signing setup: acquire the profile certificates and put <c>chat_session_update</c> on the wire. Invoked from the JOIN applier, never from the connect path, and never more than once per connection.</summary>
    /// <remarks>
    /// <para>The join packet is the earliest moment a play-phase packet may be sent on protocols 761-763. Until then the server still decodes the login packet set, so sending <c>chat_session_update</c> earlier can be interpreted under the wrong table and close the connection.</para>
    /// <para>The standard client also sends the update only after the join packet.</para>
    /// <para>Null when the session has no chat signing to set up (no provider, or a version with no signing era), which is what keeps the offline path free of it.</para>
    /// </remarks>
    public Func<CancellationToken, ValueTask>? AnnounceChatSession { get; init; }

    /// <summary>Optional per-peer signed-chat verifier resolver (signing era, protocols 759-763). Null in the offline path where no profile keys are tracked; when present, the chat applier verifies inbound <c>player_chat</c> signatures through the session-side <c>SignedChatVerifier</c>.</summary>
    public Func<Guid, Umpk.Protocol.Java.Signing.SignedChatVerifier?>? ChatVerifierResolver { get; init; }

    /// <summary>The 1.19.3+ (v3) last-seen acknowledgement tracker. When present, the chat applier records each inbound signed <c>player_chat</c> signature into it so the next outbound signed chat carries the correct acknowledgement. Null on the offline and non-v3 paths.</summary>
    public Umpk.Protocol.Java.Signing.LastSeenMessagesTracker? LastSeenTracker { get; init; }

    /// <summary>The 1.19.1/1.19.2 (v2) last-seen collector. When present, the chat applier records each inbound signed <c>player_chat</c> as a (sender, signature) pair so the next outbound signed chat or command folds the same window into its signature and repeats it on the packet. Null on the offline and non-v2 paths.</summary>
    public Umpk.Protocol.Java.Signing.LastSeenMessagesCollector? LegacyLastSeenCollector { get; init; }

    /// <summary>The session's shared, per-CONNECTION <c>MessageSignatureCache</c> (vanilla's per-connection compression cache for <c>player_chat</c> last-seen entries; see <see cref="Umpk.Protocol.Java.Signing.MessageSignatureCache"/>). One instance for the whole connection, covering every sender, not one per peer -- resolves a v3 last-seen entry the server sent as a compact cache-id reference back to the signature bytes the sender actually signed over. Null only on the offline path, where no signing era is negotiated at all. It is non-null for EVERY signing era a session negotiates (v1, v2, v3 alike), because it is created whenever <see cref="ChatVerifierResolver"/> is (see <c>UmpkClient._chatSignatureCache</c>); v1 and v2 simply never consult it, since v1 has no last-seen window at all and v2's entries are always full by construction (see <c>Umpk.Client.Internal.SignedChatVerification.RecordForCache</c>).</summary>
    public Umpk.Protocol.Java.Signing.MessageSignatureCache? SignatureCache { get; init; }

    /// <summary>Publishes an event on the loop (fire-and-await inline).</summary>
    public ValueTask PublishAsync<TEvent>(TEvent evt)
        where TEvent : IClientEvent
        => Events.PublishAsync(evt);
}

/// <summary>The dimension facts an applier hands to the world builder when creating/replacing the world.</summary>
/// <remarks>
/// Three different eras name the dimension TYPE three different ways, and every one of them is a better answer than guessing bounds from <paramref name="DimensionName"/>, which is the WORLD's name and says nothing about its height:
/// <list type="bullet">
/// <item><see cref="InlineType"/> on 751-758, the current type's own compound, straight off the wire.</item>
/// <item><see cref="DimensionTypeName"/> on 735/736 and 759-765, a resource key into the registry.</item>
/// <item><paramref name="DimensionTypeId"/> on 766+, a network id into the registry.</item>
/// </list>
/// On 47-404 there is no dimension type on the wire at all and <paramref name="DimensionTypeId"/> is the legacy signed dimension index, which is why it is only ever resolved against a registry that the server actually populated.
/// </remarks>
internal readonly record struct CommonWorldSetup(string DimensionName, int DimensionTypeId)
{
    /// <summary>The dimension-type resource key, when the era sends one (735/736, 759-765).</summary>
    public string? DimensionTypeName { get; init; }

    /// <summary>The current dimension type's own NBT compound, when the era sends one.</summary>
    public Umpk.Nbt.NbtTag? InlineType { get; init; }
}
