using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies the login/join, keep-alive, difficulty, and server-data packets. JoinGame seeds session state and builds the world; keep-alive is auto-answered.</summary>
internal sealed class ConnectionApplier : IApplier
{
    private ChunkBatchSizeCalculator? _chunkBatches;

    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        if (packet is ClientboundSetTimePacket tick)
        {
            // Observed, not claimed: the world applier still owns world time, and it is gated on the Terrain feature plus a built world. The tick-rate estimate must survive both, so it is recorded here (this applier always runs, first, and before join) and the packet is passed on.
            context.State.Server.RecordGameTime(tick.GameTime, context.Time.GetTimestamp(), context.Time);
            return false;
        }

        switch (packet)
        {
            case ClientboundLoginPacket join:
                await ApplyJoinAsync(join, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundPlayKeepAlivePacket keepAlive:
                await ApplyKeepAliveAsync(keepAlive, context, ct).ConfigureAwait(false);
                return true;

            // minecraft:ping, the play-phase one (1.17+). Vanilla's client answers unconditionally from ping behavior, and vanilla's SERVER does nothing at all with the reply (pong behavior is an empty method), which is the whole point: the packet exists for proxies and plugins, and they are the ones that can be left waiting. The configuration-phase twin has always been answered (JavaClientLogin's RespondConfigPong); this half was decoded, bound on both directions, and answered by nobody.
            case ClientboundPlayPingPacket ping:
                await context.Sink.SendAsync(new ServerboundPlayPongPacket(ping.Id), ct).ConfigureAwait(false);
                return true;

            case ClientboundStartConfigurationPacket:
                await ApplyStartConfigurationAsync(context, ct).ConfigureAwait(false);
                return true;

            // The chunk-batch handshake is answered here rather than in the world applier because it is a protocol-flow obligation, not a terrain concern: the server stalls its chunk sender without the acknowledgement, and a stalled sender also stops entities being broadcast. It must therefore work with the Terrain feature off.
            case ClientboundChunkBatchStartPacket:
                (_chunkBatches ??= new ChunkBatchSizeCalculator(context.Time)).OnBatchStart();
                return true;

            case ClientboundChunkBatchFinishedPacket batch:
                await ApplyChunkBatchFinishedAsync(batch, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundChangeDifficultyPacket difficulty:
                await context.PublishAsync(new DifficultyChanged(difficulty.Difficulty, difficulty.Locked)).ConfigureAwait(false);
                return true;

            case ClientboundConfigCustomPayloadPacket payload:
                // 1.20.2+ announce the brand here and nowhere else, so that arm stays exactly where it is and keeps claiming the brand for ClientState.
                if (payload.Channel == ServerBrandPayload.Channel
                    && ServerBrandPayload.TryReadBody(payload.Data, out string brand))
                    context.State.Server.Brand = brand;

                // Configuration traffic also reaches handlers registered for that channel, and a minecraft:register announcement is folded into the server-announced set. Routing is unconditional rather than an else-arm of the brand: the brand is still captured into ClientState above, and "the engine reads it too" is a less surprising rule for a consumer than one channel silently not being deliverable. The return value is ignored because this applier claims the packet either way; an unclaimed configuration payload has nowhere else to go in the chain.
                context.DispatchConfigurationChannel(payload.Channel, payload.Data);
                return true;

            // 1.20.2+ registry data. Three registries are kept, and only three, because they are the three this client acts on: minecraft:dimension_type decides where every decoded chunk column lands, minecraft:chat_type decides how a 1.19+ chat body is composed into the line a consumer sees, and minecraft:enchantment is the only thing that can name the numeric holder id a 1.21+ enchantment component carries. From protocol 767 there is no built-in table to fall back on and an uninstalled one decodes every enchantment to an unbound handle). Every other registry in this packet is deliberately dropped rather than half-modelled; decoding one with no consumer would only move the drop one layer up. Claiming the packet either way keeps it from falling through the chain as unhandled.
            case ClientboundConfigRegistryDataPacket registry:
                if (registry.Registry == RegistryIds.DimensionType)
                    InstallServerRegistry(
                        DimensionTypes.FromPackedEntries(registry.Entries, context.Version.Version.Protocol), context,
                        required: true);

                else if (registry.Registry == RegistryIds.ChatType)
                    InstallServerRegistry(
                        ChatTypes.FromPackedEntries(registry.Entries, context.Version.Features.ComponentEra), context,
                        required: true);

                else if (registry.Registry == RegistryIds.Enchantment)
                    InstallServerRegistry(Enchantments.FromPackedEntries(registry.Entries), context, required: true);

                return true;

            // 1.20.2/1.20.4 (764/765) send the same identifier as ONE NBT blob covering every registry (the combined registry payload) rather than a packet per registry; 1.20.5 split it. Both carry the dimension types and the chat types, and both are needed, or the band between the JoinGame blob and the packed entries would silently keep guessing bounds from the world's name and composing every chat line with the default decoration.
            case ClientboundConfigRegistryBlobPacket blob:
                InstallServerRegistry(
                    DimensionTypes.FromJoinGameRegistry(blob.Registries, context.Version.Version.Protocol), context);
                InstallServerRegistry(
                    ChatTypes.FromJoinGameRegistry(blob.Registries, context.Version.Features.ComponentEra), context);
                return true;

            case ClientboundServerDataPacket data:
                context.State.Self.EnforcesSecureChat |= false;
                await context.PublishAsync(new ServerDataReceived(data.Motd, data.IconBytes is not null)).ConfigureAwait(false);
                return true;

            case ClientboundRespawnPacket respawn:
                await ApplyRespawnAsync(respawn, context, ct).ConfigureAwait(false);
                return true;

            // A kick. Vanilla writes this frame and then closes the socket, so the reason has to be recorded here: the receive loop's ConnectionClosedException lands moments later and the session keeps whichever reason was recorded first. Both phases carry the same wire (it is one common packet class in vanilla from 1.20.2), and both end the session, so both record.
            case ClientboundDisconnectPacket kick:
                RecordKick(kick.Reason, context);
                return true;

            case ClientboundConfigDisconnectPacket configKick:
                RecordKick(configKick.Reason, context);
                return true;

            // The pre-1.17 window-transaction echo. Answered here rather than in the inventory applier because it is a protocol-flow obligation, not an inventory concern: vanilla's server sets the menu un-synched when it rejects a click and then DROPS every later click on that menu until this echo arrives, so it has to work with the Inventory feature off.
            case ClientboundTransactionPacket transaction:
                await ApplyTransactionAsync(transaction, context, ct).ConfigureAwait(false);
                return true;

            // Cookies and transfer (1.20.5+). The same common packets appear in both phases; each pair is handled identically because the wires are identical.
            case ClientboundCookieRequestPacket request:
                await AnswerCookieAsync(request.Key, Umpk.Protocol.Java.ProtocolPhase.Play, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundConfigCookieRequestPacket configRequest:
                await AnswerCookieAsync(configRequest.Key, Umpk.Protocol.Java.ProtocolPhase.Configuration, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundStoreCookiePacket store:
                context.Cookies.Set(store.Key, store.Payload);
                return true;

            case ClientboundConfigStoreCookiePacket configStore:
                context.Cookies.Set(configStore.Key, configStore.Payload);
                return true;

            case ClientboundTransferPacket transfer:
                await ApplyTransferAsync(transfer.Host, transfer.Port, context).ConfigureAwait(false);
                return true;

            case ClientboundConfigTransferPacket configTransfer:
                await ApplyTransferAsync(configTransfer.Host, configTransfer.Port, context).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Answers <c>minecraft:start_configuration</c> (1.20.2+): acknowledge, tear down the play-phase state, and hand the connection to the configuration driver until the server ends the phase.</summary>
    /// <remarks>
    /// The order is load-bearing and matches the game <c>configuration-phase transition</c>, which sends <c>ServerboundConfigurationAcknowledgedPacket</c> and only then switches its outbound protocol to configuration. <c>configuration_acknowledged</c> is a PLAY packet, so it has to go out while the connection is still in play; <see cref="ApplierContext.EnterConfiguration"/> owns the hop that follows. Nothing can arrive in between: the descriptor marks this packet terminal (<c>ProtocolGates</c>), so the connection read loop is parked at this frame boundary until the phase is acknowledged.
    /// <para>Without the acknowledgement the server waits for the configuration switch and accepts only the configuration acknowledgement packet, so the keep-alive it keeps sending is never answered and it closes the connection on the 15-second timeout. That is the whole observed failure: the codec existed and decoded, and nothing acted on the result.</para>
    /// </remarks>
    private async ValueTask ApplyStartConfigurationAsync(ApplierContext context, CancellationToken ct)
    {
        await context.Sink.SendAsync(new ServerboundConfigurationAcknowledgedPacket(), ct).ConfigureAwait(false);

        context.State.ResetForConfigurationReentry();

        // Vanilla holds the chunk-batch estimator on the play listener (chunk batch size calculator behavior), so the re-entry starts the negotiation over at the initial estimate rather than carrying the previous world's rate into the new one.
        _chunkBatches = null;

        await context.PublishAsync(new PhaseChanged(Umpk.Protocol.Java.ProtocolPhase.Configuration))
            .ConfigureAwait(false);

        await context.EnterConfiguration(ct).ConfigureAwait(false);
    }

    /// <summary>Records a server-initiated kick. <see cref="CloseReason.DisconnectMessage"/> is what tells a reconnect policy that the peer ended the session deliberately rather than the transport failing.</summary>
    private static void RecordKick(Umpk.Text.Component reason, ApplierContext context) =>
        context.RecordDisconnect(new DisconnectInfo
        {
            Reason = CloseReason.DisconnectMessage,
            Message = reason,
        });

    /// <summary>Matches the game client: echo the transaction back with <c>accepted = true</c> only when the server rejected it. An accepted transaction needs no reply (vanilla's server records an expected uid only on the rejection path, so an unsolicited echo would be discarded anyway).</summary>
    private static async ValueTask ApplyTransactionAsync(
        ClientboundTransactionPacket transaction, ApplierContext context, CancellationToken ct)
    {
        if (transaction.Accepted)
            return;

        await context.Sink.SendAsync(
            new ServerboundTransactionPacket(transaction.ContainerId, transaction.ActionNumber, Accepted: true), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a cookie request from the session store, giving <see cref="CookieStore.Resolver"/> the first refusal so a key nothing has stored yet can still be minted on demand. A key nobody can produce is answered with an absent payload: vanilla's client always replies, and a server that blocks on the reply (proxies use this for their forwarding handshake) stalls forever without one.
    /// <para>A resolver that throws falls back to the stored value rather than propagating, for the same reason: the reply has to go out. The fault is logged, not swallowed silently.</para>
    /// </summary>
    private static async ValueTask AnswerCookieAsync(
        Identifier key, Umpk.Protocol.Java.ProtocolPhase phase, ApplierContext context, CancellationToken ct)
    {
        byte[]? payload;
        try
        {
            payload = await context.Cookies.ResolveAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Logger.LogError(ex, "The cookie resolver for {Key} threw; answering from the store.", key);
            payload = context.Cookies.Get(key);
        }

        if (payload is { Length: > 5120 })
        {
            context.Logger.LogWarning(
                "Cookie {Key} contains {Length} bytes; answering absent because the protocol limit is 5120.",
                key,
                payload.Length);
            payload = null;
        }

        object response = phase == Umpk.Protocol.Java.ProtocolPhase.Configuration
            ? new ServerboundConfigCookieResponsePacket(key, payload)
            : new ServerboundCookieResponsePacket(key, payload);
        await context.Sink.SendAsync(response, ct).ConfigureAwait(false);
    }

    private static async ValueTask ApplyTransferAsync(string host, int port, ApplierContext context) =>
        await context.PublishAsync(new ServerTransferRequested(host, port)).ConfigureAwait(false);

    /// <summary>Answers the keep-alive and records the exchange. The response must carry the request's id verbatim: the server compares it against its stored challenge and disconnects on a mismatch.</summary>
    private static async ValueTask ApplyKeepAliveAsync(
        ClientboundPlayKeepAlivePacket keepAlive, ApplierContext context, CancellationToken ct)
    {
        ServerState server = context.State.Server;
        server.RecordKeepAliveReceived(keepAlive.Id, context.Time.GetTimestamp(), context.Time);
        await context.Sink.SendAsync(new ServerboundPlayKeepAlivePacket(keepAlive.Id), ct).ConfigureAwait(false);
        server.RecordKeepAliveAnswered(context.Time.GetTimestamp(), context.Time);
    }

    /// <summary>Acknowledges a finished chunk batch. Until this reply lands the server's chunk sender is capped at one unacknowledged batch, so it sends about nine chunks and then stops; the reply both releases that cap (vanilla raises it to ten on first acknowledgement) and tells the server the rate to send at. A batch that arrives before <c>chunk_batch_start</c> was seen still gets answered, using the starting estimate, because leaving it unanswered is what stalls the stream.</summary>
    private async ValueTask ApplyChunkBatchFinishedAsync(
        ClientboundChunkBatchFinishedPacket batch, ApplierContext context, CancellationToken ct)
    {
        ChunkBatchSizeCalculator calculator = _chunkBatches ??= new ChunkBatchSizeCalculator(context.Time);
        calculator.OnBatchFinished(batch.BatchSize);
        await context.Sink.SendAsync(
            new ServerboundChunkBatchReceivedPacket(calculator.DesiredChunksPerTick), ct).ConfigureAwait(false);
    }

    /// <summary>Swaps one server-sent registry into the session's <see cref="RegistryAccess"/>, leaving every other registry in the snapshot untouched. A missing optional/unknown registry is a no-op; an explicitly supplied supported registry that could not be decoded fails atomically.</summary>
    private static void InstallServerRegistry(IRegistry? decoded, ApplierContext context, bool required = false)
    {
        if (decoded is null)
        {
            if (required)
                throw new InvalidDataException("A supported server registry could not be decoded atomically.");

            return;
        }

        if (context.State.Registries is not { } current)
            return;

        context.State.Registries = RegistryAccess.FromSnapshot(current.Snapshot.With(decoded));
    }

    private static async ValueTask ApplyJoinAsync(ClientboundLoginPacket join, ApplierContext context, CancellationToken ct)
    {
        // 735-763 ship the whole registry set inside this packet and nowhere else, so it has to be taken here, BEFORE the world is built from it. The chat types ride in the same blob from 759 (1.19, where chat decoration became the client's job) and are taken with it.
        InstallServerRegistry(
            DimensionTypes.FromJoinGameRegistry(join.JoinGameRegistry, context.Version.Version.Protocol), context);
        InstallServerRegistry(
            ChatTypes.FromJoinGameRegistry(join.JoinGameRegistry, context.Version.Features.ComponentEra), context);

        context.WorldSetup(new CommonWorldSetup(join.SpawnInfo.Dimension, join.SpawnInfo.DimensionTypeId)
        {
            DimensionTypeName = join.SpawnInfo.DimensionTypeName,
            InlineType = join.DimensionTypeNbt,
        });

        // World construction is the required, failure-prone part of a join. Complete it before making the player observable as spawned or resetting join-owned state, so an unusable required custom dimension terminates the session without leaving a half-applied player/world transition.
        SelfState self = context.State.Self;
        self.EntityId = join.PlayerId;
        self.ViewDistance = join.ViewDistance;
        self.SimulationDistance = join.SimulationDistance;
        self.EnforcesSecureChat = join.EnforcesSecureChat;
        self.GameMode = (Game.Players.GameMode)join.SpawnInfo.GameType;

        self.HasSpawned = true;
        ClearSprintLatch(self);

        // Vanilla resets the global chat index HERE and only here (login behavior: this.nextChatIndex = 0), so it spans respawns and dimension changes and restarts on a re-join.
        context.State.Chat.ResetForJoin(
            tracksGlobalIndex: context.Version.Version.Protocol >= ChatApplier.FirstGlobalChatIndexProtocol);

        // Vanilla's placement, and the only safe one: handleLogin is where the client learns the server has switched its channel protocol to PLAY, so this is the earliest moment a play-phase packet may go out at all. Announcing at connect time instead raced the server's own switch and killed the session on 761-763; see ApplierContext.AnnounceChatSession.
        if (context.AnnounceChatSession is { } announceChatSession)
            await announceChatSession(ct).ConfigureAwait(false);

        // After the registries are installed, so the seed resolves against this session's own attribute table rather than an empty one.
        SeedSelfAttributes(context);
        context.PhysicsConditionsDirty();

        await context.PublishAsync(new JoinedGame(new SessionInfo
        {
            Profile = new GameProfile(self.Uuid, self.Username),
            Endpoint = new ServerEndpoint("unknown", 0),
            Version = context.Version,
            Phase = Umpk.Protocol.Java.ProtocolPhase.Play,
        })).ConfigureAwait(false);
    }

    /// <summary>Resets the announced-sprint latch when the player state is rebuilt.</summary>
    /// <remarks>
    /// On login and on a respawn that does not keep attributes, the rebuilt player starts with an empty input and an announced-sprint value of false. Only the keep-attributes branch carries the old value across.
    /// <para>UMPK does not model <c>shouldKeep</c>, and it does not need to: the two outcomes are not symmetric. Clearing a latch that vanilla would have kept costs one redundant START_SPRINTING on the next tick, which the server treats as idempotent. Failing to clear one vanilla would have reset means the client believes it already announced a sprint the server no longer records, and the edge never fires again, so the bot runs at sprint speed while the server thinks it is walking for the rest of the session.</para>
    /// </remarks>
    private static void ClearSprintLatch(SelfState self)
    {
        self.Sprinting = false;
        self.SprintRequested = false;
        self.Sneaking = false;
    }

    private static async ValueTask ApplyRespawnAsync(
        ClientboundRespawnPacket respawn, ApplierContext context, CancellationToken ct)
    {
        Game.Players.GameMode? gameMode = null;
        if (respawn.SpawnInfo is { } spawn)
        {
            context.WorldSetup(new CommonWorldSetup(spawn.Dimension, spawn.DimensionTypeId)
            {
                DimensionTypeName = spawn.DimensionTypeName,
                InlineType = respawn.DimensionTypeNbt,
            });
            gameMode = (Game.Players.GameMode)spawn.GameType;
        }
        else if (respawn.Legacy is { } legacy)
        {
            // 47-404 name the dimension with a signed int rather than a resource key, and the respawn packet is the ONLY notice a client gets that it moved between them. Without this the world instance keeps the previous dimension's height bounds and skylight after a portal, which silently mis-sizes every chunk column decoded afterwards.
            context.WorldSetup(new CommonWorldSetup(LegacyDimensionName(legacy.Dimension), legacy.Dimension));
            gameMode = (Game.Players.GameMode)legacy.GameMode;
        }

        // As on join, resolve/install the required world first. An invalid replacement must leave the previous player and world coherent while the required packet fault ends the connection.
        ClearSprintLatch(context.State.Self);
        ClearEffectsIfThePlayerEntityWasDestroyed(respawn, context);
        SeedSelfAttributes(context);
        if (gameMode is { } resolvedGameMode)
            context.State.Self.GameMode = resolvedGameMode;

        context.PhysicsConditionsDirty();
        await context.PublishAsync(new Respawned()).ConfigureAwait(false);
    }

    /// <summary>Drops the local player's status effects when this respawn destroyed the player entity that had them, which is the only transition after which the effect table is stale and NO packet says so.</summary>
    /// <remarks>
    /// <para>The discriminator is the data-to-keep mask being zero:</para>
    /// <list type="bullet">
    /// <item>
    /// A death respawn uses a zero data-to-keep mask.
    /// </item>
    /// <item>
    /// A non-zero mask is exactly the case where the server preserves the old player's active effects.
    /// </item>
    /// <item>
    /// A dimension change uses <c>KEEP_ALL_DATA</c>, preserving the same entity and its effects.
    /// </item>
    /// <item>
    /// Nothing sends <c>remove_mob_effect</c> for the effects a death took away. The server only resends effects the new player has, which after a death is nothing. So a client that keeps its table keeps a dead player's potions - and an INFINITE one it would keep forever.
    /// </item>
    /// </list>
    /// <para>47-404 carry no mask on the wire at all, so <see cref="ClientboundRespawnPacket.DataToKeep"/> decodes as zero there and every legacy respawn clears. That is lossless as well as safe: legacy dimension transfers resend every active effect after the respawn frame. Protocols 735-758 map their single <c>keepAllPlayerData</c> bool onto 1/0, which lands on the same side of this test as the mask it became.</para>
    /// </remarks>
    private static void ClearEffectsIfThePlayerEntityWasDestroyed(
        ClientboundRespawnPacket respawn, ApplierContext context)
    {
        if (respawn.DataToKeep == 0)
            context.State.Self.ClearEffects();

    }

    /// <summary>Drops the local player's attribute map and re-seeds the standard player defaults on login and every respawn.</summary>
    /// <remarks>
    /// <para>The respawn reset is unconditional, unlike <see cref="ClearEffectsIfThePlayerEntityWasDestroyed"/>, which gates on <c>DataToKeep == 0</c>. The keep flags govern what the server restores and sends; client attributes return to player defaults on both respawn branches.</para>
    /// <para>The two failure directions are not symmetric, which is the same argument <see cref="ClearSprintLatch"/> makes. Failing to clear leaves a dead player's soul-speed or powder-snow modifier on the map forever, and nothing on the wire ever contradicts it. Clearing when vanilla would have kept costs one stale tick until the server's next dirty broadcast.</para>
    /// <para>This does NOT depend on <c>DataToKeep</c> being zero by accident on the legacy codecs, and that matters: 47-404 have no such field on the wire and <c>WorldStateCodecs</c> writes the literal <c>DataToKeep: 0</c> for them, while 735-758 map their single <c>keepAllPlayerData</c> bool onto 1/0. An unconditional reset is correct on all three shapes without reading the field at all.</para>
    /// <para><c>Known</c> is answered STRUCTURALLY, exactly as <c>CapabilityCapture</c> answers <c>EffectsKnown</c>: the entity applier is the only writer of self attributes and <c>ClientState</c> creates the entity store only when the feature is on, so a null store is precisely "this session can never observe an attribute". A protocol with no <c>minecraft:attribute</c> registry is the same claim for a different reason, and gets the same answer.</para>
    /// </remarks>
    private static void SeedSelfAttributes(ApplierContext context)
    {
        Game.Registries.RegistryAccess? registries = context.State.Registries;
        bool known = context.State.EntitiesOrNull is not null && registries is { Attributes.Count: > 0 };
        context.State.Self.Attributes.SeedPlayerDefaults(registries, known);
    }

    /// <summary>The pre-1.16 numeric dimension ids, which are a closed set of exactly three values.</summary>
    private static string LegacyDimensionName(int dimension) => dimension switch
    {
        -1 => "minecraft:the_nether",
        1 => "minecraft:the_end",
        _ => "minecraft:overworld",
    };
}
