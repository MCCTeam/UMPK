using Umpk;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

/// <summary>Data-driven full-matrix live validation (nightly-only, see <see cref="NightlyTheoryAttribute"/>). One representative jar per wire protocol (49 protocols, <see cref="LiveMatrix"/>) is booted sequentially on port 25599 (the <see cref="LiveServerCollection"/> disables parallelism), and each leg drives the high-level <see cref="UmpkClient"/> through login to Play and asserts on real decoded <see cref="ClientState"/>: keep-alive survival, world/terrain decode, chat receive AND send, entity tracking/metadata, a client-driven entity attack, inventory item decode, held-slot selection with a client block place and dig, a chest open/content/click round-trip, potion effects, the command tree, and a clean disconnect - every feature honestly n/a-gated per version by the codec registry. The JVM is version-selected (<see cref="JavaRuntimes"/>) and always torn down; each leg gets one infrastructure-fault reboot-and-retry.</summary>
[Collection(LiveServerCollection.Name)]
public sealed class LiveServerTests
{
    private const string Username = "UmpkITest";

    private readonly ITestOutputHelper _out;

    public LiveServerTests(ITestOutputHelper output) => _out = output;

    [NightlyTheory]
    [MemberData(nameof(LiveMatrix.Legs), MemberType = typeof(LiveMatrix))]
    public async Task FullFeatureLeg(string versionName, int protocol)
    {
        string serverDir = IntegrationConfig.ServerDir(versionName);
        Assert.True(
            Directory.Exists(serverDir),
            $"Server directory for {versionName} not provisioned ({serverDir}). Set UMPK_SERVER_ROOT.");
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) && version is not null,
            $"No JavaVersion for protocol {protocol}.");

        // Bounded reboot-and-retry for INFRASTRUCTURE faults only. An assertion failure (XunitException) is a real feature verdict and fails immediately; any other fault (boot timeout, connect exhaustion, a wedged server socket) tears the whole leg down (server + client are per-attempt disposables) and reruns it once from scratch.
        for (int attempt = 0; ; attempt++)
            try
            {
                await RunLegAsync(versionName, protocol, version!);
                return;
            }
            catch (Exception ex) when (attempt == 0 && ex is not Xunit.Sdk.XunitException)
            {
                _out.WriteLine(
                    $"[RETRY] {versionName}: infrastructure fault on the first leg attempt, rebooting once: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }

    }

    private async Task RunLegAsync(string versionName, int protocol, JavaVersion version)
    {
        string serverDir = IntegrationConfig.ServerDir(versionName);
        string jre = JavaRuntimes.ForProtocol(protocol);
        var report = new LegReport(versionName, protocol, JavaRuntimes.TierLabel(protocol));

        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(360));
        await using LocalServer server = await LocalServer.StartAsync(
            serverDir, jre, TimeSpan.FromSeconds(180), ct.Token);

        var signals = new LegSignals();
        var logs = new CapturingLoggerFactory();
        await using UmpkClient client = await ConnectWithRetryAsync(version, protocol, signals, logs, ct.Token);

        try
        {
            // login -> Play
            report.Login = client.Status == ClientStatus.Playing;
            Assert.True(report.Login, $"{versionName}: expected Playing after connect, got {client.Status}.");

            // wait for join (position + world)
            await WaitUntilAsync(
                () => client.State.Self.HasSpawned && (client.State.HasWorld || signals.ChunkLoads > 0),
                TimeSpan.FromSeconds(30), ct.Token);

            // HasSpawned is set at join, but the initial player_position packet arrives separately; wait for it so the summon/dig coordinates use the real spawn, not the (0,0,0) pre-position default.
            await WaitUntilAsync(() => !client.State.Self.Position.Equals(default(Vec3d)), TimeSpan.FromSeconds(15), ct.Token);
            Vec3d spawnPos = await client.InvokeAsync(c => c.State.Self.Position, ct.Token);
            report.SpawnPosition = spawnPos;

            // provision the account for privileged/creative actions. All provisioning runs through the server CONSOLE, which is always op, so the account itself does not need to be op: creative, give, summon, effect, setblock and time all apply to the player regardless of its permission level. We deliberately do NOT `op` the account: opping raises the player's permission level, which makes the server RE-SEND the full (expanded) command tree, and that larger tree trips a pre-existing receive-path decode limitation on the mid-modern eras (1.16-1.20.1 fall back to the 1.21.5 argument-type registry, whose parser-id table misaligns on some older argument nodes - see the WALL note). The initial (non-op) command tree still decodes and validates the commands feature; the expanded-tree decode is a separate receive-path issue outside this run's send-side mandate.
            await server.SendConsoleAsync($"gamemode creative {Username}", ct.Token);
            // Disable mob/tile drops so the client attack-kill and block dig do not spawn item entities: a dropped item's metadata carries an ItemStack (pre-1.13 that decode throws and drops the session, documented), and cow loot landing in the hotbar would displace the given block the held-slot selection targets. Stabilize time so the command-effect assert is deterministic. 1.21.11 (protocol 774) renamed the whole gamerule space to snake_case (verified vs 1.21.10 doMobLoot vs 1.21.11 entity_drops/block_drops/advance_time).
            if (protocol >= 774)
            {
                await server.SendConsoleAsync("gamerule entity_drops false", ct.Token);
                await server.SendConsoleAsync("gamerule block_drops false", ct.Token);
                await server.SendConsoleAsync("gamerule advance_time false", ct.Token);
            }
            else
            {
                await server.SendConsoleAsync("gamerule doMobLoot false", ct.Token);
                await server.SendConsoleAsync("gamerule doTileDrops false", ct.Token);
                await server.SendConsoleAsync("gamerule doDaylightCycle false", ct.Token);
            }
            // Peaceful difficulty: keeps ambient hostile mobs from spawning (and despawns any), so the spawn stream stays quiet and the summoned cow's attribution/attack cannot race a stray mob.
            await server.SendConsoleAsync("difficulty peaceful", ct.Token);
            // Confirm creative actually applied (the server sends a game_event the client tracks) so the block-break dig breaks instantly on the START action rather than needing a mining sequence.
            bool creative = await WaitUntilAsync(
                () => client.State.Self.GameMode == Umpk.Game.Players.GameMode.Creative, TimeSpan.FromSeconds(8), ct.Token);
            report.Provisioned = creative;

            // Client-send capability is derived from the codec registry per version, never assumed from a protocol range. The movement/dig/place serverbound codecs are implemented across the whole current matrix (the historical mid-era send-marker bands were closed by per-protocol codec selection, e.g. the pre-1.19 dig without the sequence VarInt and the 1.9-1.12.2 use_item_on i8/f32 cursor forms); the dynamic gates stay so a future registry change re-gates honestly. use_item_on remains unbound only at 1.8 (protocol 47, which uses the legacy block-place wire instead), so canPlace is false there.
            bool canMove = LiveMatrix.SendImplemented(version, "move_player_pos");
            bool canDig = LiveMatrix.SendImplemented(version, "player_action");
            bool canPlace = LiveMatrix.SendImplemented(version, "use_item_on");
            bool canChatSend = LiveMatrix.SendImplemented(version, "chat");

            // Receive-path capability is registry-derived per version too. The clientbound Play surface decodes across the whole current matrix except the honest per-era markers gated below: the commands tree on 1.16-1.18.2 (protocols 735-758). open_screen decodes across protocols 107-404, and container_set_content decodes across the supported range. The gates stay dynamic so future registry changes are reflected automatically.
            bool rxEntity = LiveMatrix.ReceiveImplemented(version, "add_entity");
            bool rxTime = LiveMatrix.ReceiveImplemented(version, "set_time");
            bool rxBlock = LiveMatrix.ReceiveImplemented(version, "block_update");
            bool rxInv = LiveMatrix.ReceiveImplemented(version, "container_set_content");
            bool rxChat = LiveMatrix.ReceiveImplemented(version, "system_chat")
                || LiveMatrix.ReceiveImplemented(version, "chat")
                || LiveMatrix.ReceiveImplemented(version, "player_chat");
            bool rxEffect = LiveMatrix.ReceiveImplemented(version, "update_mob_effect");
            bool rxOpenScreen = LiveMatrix.ReceiveImplemented(version, "open_screen");
            bool rxMetadata = LiveMatrix.ReceiveImplemented(version, "set_entity_data");
            // The player-chat echo path: the console `say` broadcast arrives as system_chat/legacy chat everywhere, but a PLAYER message is broadcast as player_chat (or legacy chat pre-1.19), so the send echo is only client-observable where one of those decodes. From 1.20.2 the player_chat receive is a marker, so the send assertion there is the server-log round-trip.
            bool rxPlayerEcho = LiveMatrix.ReceiveImplemented(version, "chat")
                || LiveMatrix.ReceiveImplemented(version, "player_chat");

            // Send our position/rotation up front (where drivable) so the server agrees on where we are.
            if (canMove)
                try
                {
                    await client.Actions.Movement.SetRotationAsync(90f, 0f, ct.Token);
                    await client.Actions.Movement.SendPositionAsync(ct.Token);
                    report.Movement = !spawnPos.Equals(default(Vec3d)) && client.Status == ClientStatus.Playing;
                }
                catch (Exception ex)
                {
                    report.MovementError = ex.GetType().Name + ": " + ex.Message;
                }

            else
                report.MovementNote = "n/a-send-marker";

            // Summon, track, and remove a cow where the clientbound add_entity codec is implemented.
            if (rxEntity)
            {
                // Count spawns relative to our summon: the cumulative counter may already be non-zero from ambient mobs at join, so wait for an increment attributable to the summoned cow (and give it a brief settle so the entity is tracked before the effect targets it). Clear every pre-existing cow first (long-lived test worlds accumulate them around spawn), so exactly ONE cow exists afterwards and the console can target it with nearest-selectors during the attack loop without ambiguity. Console lines process in order, so the clear always lands before the summon.
                await server.SendConsoleAsync(KillCommand(protocol), ct.Token);
                int spawnsBefore = signals.EntitySpawns;
                await server.SendConsoleAsync(SummonCommand(protocol, spawnPos), ct.Token);
                await WaitUntilAsync(() => signals.EntitySpawns > spawnsBefore, TimeSpan.FromSeconds(10), ct.Token);
                await Task.Delay(500, ct.Token);

                // effects: apply a potion effect to the LOCAL PLAYER and confirm the client decodes update_mob_effect into an EntityEffectApplied event + a queryable self-effect. Vanilla only sends ClientboundUpdateMobEffectPacket for the affected player itself (a standalone mob's effect is reflected only in its metadata particle colour, never as this packet), so the effect must target the player to exercise the receive codec. The client tracks self effects in SelfState (Self is not in the shared entity store).
                if (rxEffect)
                {
                    int effectsBefore = signals.EffectsApplied;
                    for (int attempt = 0; attempt < 5 && signals.EffectsApplied == effectsBefore; attempt++)
                    {
                        await server.SendConsoleAsync(EffectCommand(protocol), ct.Token);
                        if (await WaitUntilAsync(() => signals.EffectsApplied > effectsBefore, TimeSpan.FromSeconds(2), ct.Token))
                            break;

                    }

                    bool selfEffectTracked = await client.InvokeAsync(c => c.State.Self.ActiveEffects.Count > 0, ct.Token);
                    report.Effects = signals.EffectsApplied > effectsBefore && selfEffectTracked;
                }
                else
                    report.EffectsNote = "n/a-rx-marker";

                // entity interaction: resolve the summoned cow's id from the spawn stream and drive the client attack wire (serverbound interact action=attack, or the dedicated attack packet from 26.1). The cow is summoned with NoAI so it stays at the player's feet, inside the server's interaction-reach check. Confirmation is EITHER removal by repeated attacks (a cow has 10 HP and a creative fist deals 1 per damaging hit) OR an observed health decrease through the cow's set_entity_data after a hit. The console kill below stays as cleanup so the removal round-trip is asserted even when the attack leaves the cow alive.
                int? cowId = signals.AttributeSummon(spawnsBefore, "cow", spawnPos, radius: 4.0);
                bool canAttack = LiveMatrix.SendImplemented(version, "interact")
                    || LiveMatrix.SendImplemented(version, "attack");
                if (canAttack && cowId is int cow)
                {
                    int removalsBefore = signals.EntityRemovals;
                    bool removedByAttack = false;
                    bool healthDecreased = false;
                    int attacksSent = 0;
                    float lastHealth = float.NaN;
                    // A creative fist deals 1 damage per landed hit and a damaged mob is invulnerable to equal damage for 10 ticks (500ms), so a 600ms cadence makes every hit land: a 10 HP cow dies within ~10 swings. The kill is the operative proof; the health read below stays as future-proofing but currently never resolves (no metadata key source dataset is wired, so Entity.Metadata.TryGet(Health) is false on every version).
                    for (int hit = 0; hit < 20; hit++)
                    {
                        // Re-anchor the (single) cow onto the player every few swings: entity pushing can shove even a NoAI mob out of the server's attack-reach check, and on the eras whose entity-move packets do not update the client view the drift is invisible, so the attacks silently hit nothing (seen live on 1.9/1.10: 20 swings, cow at full health, no cow left near the summon point server-side). The teleport keeps the server-side target inside reach without touching the client attack wire.
                        if (hit % 3 == 0)
                        {
                            // Selector forks: capitalized short entity names until 1.11, and the old c=1 count argument until 1.13 replaced it with limit/sort.
                            string cowSelector = protocol < 315
                                ? "@e[type=Cow,c=1]"
                                : protocol < 393
                                    ? "@e[type=minecraft:cow,c=1]"
                                    : "@e[type=minecraft:cow,limit=1,sort=nearest]";
                            await server.SendConsoleAsync($"tp {cowSelector} {Username}", ct.Token);
                        }

                        await client.Actions.Interaction.AttackEntityAsync(cow, ct.Token);
                        await client.Actions.Interaction.SwingAsync(ct: ct.Token);
                        attacksSent++;

                        // Pace the next swing past the invulnerability window WHILE streaming position echoes. A vanilla client sends a movement packet every tick, and the 1.9/1.10 servers advance the player's per-tick update (including the attack-strength charge 1.9 introduced) off that traffic: a silent client swings at zero charge forever (0.2x damage floor) and one echo per swing only reaches 1/5 charge, both seen live on protocols 107-210. Eight echoes per 600ms window let the
                        // charge fill (five ticks for a fist) so every swing lands a full-strength hit;
                        // the stream is a no-op elsewhere (players tick unconditionally there).
                        for (int echo = 0; echo < 8; echo++)
                        {
                            if (canMove)
                                await client.Actions.Movement.SendPositionAsync(ct.Token);

                            await Task.Delay(75, ct.Token);
                        }

                        (bool gone, float health) = await client.InvokeAsync(c =>
                        {
                            if (!c.State.Entities.TryGet(cow, out var entity))
                                return (true, float.NaN);

                            return (false, entity.Metadata.TryGet(
                                Umpk.Game.Entities.EntityMetadataKeys.Health, out float h) ? h : float.NaN);
                        }, ct.Token);
                        if (!float.IsNaN(health))
                            lastHealth = health;

                        if (gone && signals.EntityRemovals > removalsBefore)
                        {
                            removedByAttack = true;
                            break;
                        }

                        if (!float.IsNaN(health) && health < 10f)
                        {
                            healthDecreased = true;
                            break;
                        }
                    }

                    // The fatal blow's remove packet can lag the last swing; absorb that with a short bounded wait before concluding the attack had no effect.
                    if (!removedByAttack && !healthDecreased)
                        removedByAttack = await WaitUntilAsync(
                            client, c => !c.State.Entities.TryGet(cow, out _), TimeSpan.FromSeconds(3), ct.Token);

                    if (!removedByAttack && !healthDecreased)
                    {
                        // Server-side ground truth for the failure note: on the pre-1.13 command forms the failed no-op entitydata echoes the target's full NBT (including health) into the server log. The selector is scoped to the summon point so leftover world cows cannot shadow ours.
                        int px = (int)Math.Floor(spawnPos.X);
                        int py = (int)Math.Floor(spawnPos.Y);
                        int pz = (int)Math.Floor(spawnPos.Z);
                        int probeStart = server.LogText.Length;
                        await server.SendConsoleAsync(
                            protocol < 393
                                ? $"entitydata @e[type=Cow,x={px},y={py},z={pz},r=4,c=1] {{}}"
                                : $"data get entity @e[type=minecraft:cow,x={px},y={py},z={pz},distance=..4,limit=1] Health",
                            ct.Token);
                        await Task.Delay(1200, ct.Token);
                        string probeTail = server.LogText[Math.Min(probeStart, server.LogText.Length)..];
                        _out.WriteLine(
                            $"ATTACK-MISS {versionName}: cow={cow} attacks={attacksSent} meta={signals.MetadataChangesFor(cow)}\n" +
                            $"PROBE:\n{(probeTail.Length > 1600 ? probeTail[..1600] : probeTail)}");
                    }

                    report.EntityAttack = removedByAttack || healthDecreased;
                    report.EntityAttackNote = removedByAttack
                        ? "ok-killed-by-attack"
                        : healthDecreased
                            ? "ok-health-decrease"
                            : $"no-effect(id={cow},attacks={attacksSent},health={(float.IsNaN(lastHealth) ? "unobservable(no-metadata-key-source)" : lastHealth.ToString(System.Globalization.CultureInfo.InvariantCulture))},removals={signals.EntityRemovals - removalsBefore},meta={signals.MetadataChangesFor(cow)},spawns=[{signals.SpawnListSince(spawnsBefore)}],self=({spawnPos.X:0.#},{spawnPos.Y:0.#},{spawnPos.Z:0.#}))";
                }
                else if (!canAttack)
                    report.EntityAttackNote = "n/a-send-marker";

                else
                    report.EntityAttackNote = $"cow-id-unresolved(spawns=[{signals.SpawnListSince(spawnsBefore)}])";

                // The spawn, NoAI flag, or health change must produce decoded entity metadata.
                if (!rxMetadata)
                    report.EntityMetadataNote = "n/a-rx-marker";

                else if (cowId is int cowForMeta)
                {
                    await WaitUntilAsync(() => signals.MetadataChangesFor(cowForMeta) > 0, TimeSpan.FromSeconds(5), ct.Token);
                    report.EntityMetadata = signals.MetadataChangesFor(cowForMeta) > 0;
                }

                await server.SendConsoleAsync(KillCommand(protocol), ct.Token);
                await WaitUntilAsync(() => signals.EntityRemovals > 0, TimeSpan.FromSeconds(10), ct.Token);
            }
            else
            {
                report.EffectsNote = "n/a-rx-marker";
                report.EntityAttackNote = "n/a-rx-marker";
                report.EntityMetadataNote = "n/a-rx-marker";
            }

            // inventory: give an item (1.13+ index-based item registry) and confirm it decodes into a slot
            if (protocol >= 393)
            {
                await server.SendConsoleAsync($"give {Username} minecraft:stone 7", ct.Token);
                await WaitUntilAsync(() => AnyPlayerSlotFilled(client), TimeSpan.FromSeconds(10), ct.Token);
            }

            // chat (receive): broadcast a message from the server console; the client must decode it
            int chatBefore = signals.ChatReceived;
            await server.SendConsoleAsync($"say umpk-live-{Guid.NewGuid().ToString("N")[..8]}", ct.Token);
            await WaitUntilAsync(() => signals.ChatReceived > chatBefore, TimeSpan.FromSeconds(8), ct.Token);
            report.Chat = signals.ChatReceived > chatBefore;

            // chat (send): drive a real chat line over the serverbound chat wire (the signing-era codec split lives inside SendChatAsync). Wire proof on every version: the message must reach the server's console log as a player line. Where the player broadcast also decodes client-side (legacy chat / player_chat, through 1.20.1) the echo is additionally asserted through the chat-received path; from 1.20.2 the player_chat receive is a marker and system_chat only carries console/system lines, so the log round-trip is the honest send assertion there.
            if (canChatSend)
            {
                string sendToken = $"umpk-send-{Guid.NewGuid().ToString("N")[..8]}";
                int sendEchoSeen = 0;
                using (client.Events.Subscribe<ChatMessageReceived>(c =>
                {
                    if (c.Message is not null && ComponentContainsText(c.Message, sendToken))
                        Interlocked.Increment(ref sendEchoSeen);

                }))
                {
                    await client.Actions.Chat.SendChatAsync(sendToken, ct.Token);
                    bool logged = await WaitUntilAsync(
                        () => server.LogText.Contains(sendToken, StringComparison.Ordinal), TimeSpan.FromSeconds(8), ct.Token);
                    bool echoed = !rxPlayerEcho || await WaitUntilAsync(
                        () => Volatile.Read(ref sendEchoSeen) > 0, TimeSpan.FromSeconds(8), ct.Token);
                    report.ChatSend = logged && echoed;
                    if (report.ChatSend && !rxPlayerEcho)
                        report.ChatSendNote = "ok-log(echo-n/a-rx-marker)";

                }
            }
            else
                report.ChatSendNote = "n/a-send-marker";

            // command effect: change world time via console; the client must observe the SetTime packet
            int timeBefore = signals.TimeChanges;
            await server.SendConsoleAsync("time set 6000", ct.Token);
            await WaitUntilAsync(() => signals.TimeChanges > timeBefore, TimeSpan.FromSeconds(8), ct.Token);
            report.CommandEffect = signals.TimeChanges > timeBefore;

            // Surface an early mid-action disconnect with its reason before the next send hits a dead socket.
            if (client.Status == ClientStatus.Disconnected || signals.DisconnectDuringWindow > 0)
            {
                string sig = $"chunks={signals.ChunkLoads} spawns={signals.EntitySpawns} removals={signals.EntityRemovals} " +
                    $"content={signals.ContainerContentChanges} chat={signals.ChatReceived} time={signals.TimeChanges} blocks={signals.BlockChanges}";
                Assert.Fail($"{versionName}: session dropped during active actions (before block-break): {signals.LastDisconnect}\nSIGNALS: {sig}\nLOGS:\n{logs.Dump(50)}");
            }

            // block place + break round-trip. The server sets a known block beside the player (receive path: block_update decodes and World reflects stone), then the client drives the place SEND (use_item_on) and the dig SEND (player_action) and the server's responses are observed as further block_updates. Two blocks to the side and one up: an isolated spot the player does not occupy, in easy reach.
            var target = new BlockPos((int)Math.Floor(spawnPos.X) + 2, (int)Math.Floor(spawnPos.Y) + 1, (int)Math.Floor(spawnPos.Z));
            var above = new BlockPos(target.X, target.Y + 1, target.Z);
            // The block round-trip additionally requires a structural (queryable) gameplay column. StructuralChunkColumn is true across the whole current matrix - every era decodes its chunk
            // sections into a real block grid - so today this reduces to the block_update receive gate;
            // the structural term stays so the gate re-engages honestly if a future era ships opaque.
            bool blockObservable = rxBlock && LiveMatrix.StructuralChunkColumn(protocol);
            if (blockObservable)
            {
                // Re-issue the setblock until the block_update round-trip lands in World, bounded. Under the sequential matrix load a single server's block_update can be delayed well past a short window (GC pause, chunk-batch backlog); the setblock is server-authoritative and idempotent, so re-sending the same known state and re-polling is a robust, honest way to absorb the delay without weakening the assertion (the block genuinely must appear).
                bool placed = false;
                for (int attempt = 0; attempt < 6 && !placed; attempt++)
                {
                    await server.SendConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:stone", ct.Token);
                    placed = await WaitUntilAsync(
                        client, c => (c.State.HasWorld ? c.State.World.GetBlockStateId(target) : 0) != 0, TimeSpan.FromSeconds(6), ct.Token);
                }

                report.BlockBreak = placed;
                if (!placed)
                {
                    // Failure diagnostics: distinguish "block_update never decoded" from "the target cell itself never updates" (an unloaded/undecoded chunk reads as air forever, which also lets the dig's final-air check pass vacuously).
                    var under = new BlockPos((int)Math.Floor(spawnPos.X), (int)Math.Floor(spawnPos.Y) - 1, (int)Math.Floor(spawnPos.Z));
                    int targetState = await BlockStateAtAsync(client, target, ct.Token);
                    int underState = await BlockStateAtAsync(client, under, ct.Token);
                    string slog = server.LogText;
                    _out.WriteLine(
                        $"SETBLOCK-MISS {versionName}: target={target} state={targetState} underPlayer={under}:{underState} " +
                        $"chunkLoads={signals.ChunkLoads} blockChanges={signals.BlockChanges} self={spawnPos}\n" +
                        $"SERVERLOG-TAIL:\n{(slog.Length > 1500 ? slog[^1500..] : slog)}");
                }

                // Look at the target so the interaction hits the intended block/face.
                if (canMove)
                {
                    await client.Actions.Movement.LookAtAsync(new Vec3d(target.X + 0.5, target.Y + 0.5, target.Z + 0.5), ct.Token);
                    await client.Actions.Movement.SendPositionAsync(ct.Token);
                }

                // Client place SEND (use_item_on): place against the top face of the target. A malformed cursor form (the V1_9_4 i8/f32 flip) would fail to decode server-side and drop the session, so surviving the subsequent asserts validates the wire across the whole range. Where the client also holds a block (item registry from 1.13+), the placed block above the target is observed as a real block_update.
                if (canPlace)
                {
                    // Clear the placement destination first: the leg spawns at a randomized offset around the world spawn each boot, so terrain can already occupy the spot above the target, and a use_item_on into an occupied cell is silently rejected by the server. Then WAIT
                    // for the cell to actually be air client-side before placing: console lines are only
                    // processed on the next server tick, so an unawaited clear can race the placement and wipe the just-placed block (observed live on 1.17.1).
                    await server.SendConsoleAsync($"setblock {above.X} {above.Y} {above.Z} minecraft:air", ct.Token);
                    await WaitUntilAsync(
                        client, c => (c.State.HasWorld ? c.State.World.GetBlockStateId(above) : -1) == 0,
                        TimeSpan.FromSeconds(4), ct.Token);
                    await Task.Delay(150, ct.Token);

                    // Hold the given stone before placing: locate the hotbar slot the give landed in (player-window slots 36-44, matched by the stone identity so stray pickups cannot shadow it) and select it over the serverbound set_carried_item wire, so the use_item_on below places a real block whose appearance is asserted.
                    if (protocol >= 393)
                    {
                        int hotbar = await client.InvokeAsync(c =>
                        {
                            if (!c.State.Features.Inventory)
                                return -1;

                            IReadOnlyList<Umpk.Game.Items.ItemStack> slots = c.State.Inventory.PlayerSlots;
                            int firstFilled = -1;
                            for (int i = 36; i <= 44 && i < slots.Count; i++)
                            {
                                if (slots[i].IsEmpty)
                                    continue;

                                if (slots[i].Item.Id.Path == "stone")
                                    return i - 36;

                                if (firstFilled < 0)
                                    firstFilled = i - 36;

                            }

                            return firstFilled;
                        }, ct.Token);
                        if (hotbar >= 0)
                            await client.Actions.Inventory.SelectHeldSlotAsync(hotbar, ct.Token);

                    }

                    int placeBefore = signals.BlockChanges;
                    // The place is retried bounded, mirroring the chest-open absorber: a single use_item_on can be swallowed around teleport-ack/chunk-resend timing windows, and a re-click against an already-filled destination is silently rejected by the server, so the loop simply re-polls until the placed block is observed.
                    bool placedAbove = false;
                    for (int attempt = 0; attempt < 3 && !placedAbove; attempt++)
                    {
                        await client.Actions.Interaction.PlaceBlockAsync(
                            target, Direction.Up, new Vec3d(0.5, 1.0, 0.5), ct: ct.Token);
                        report.BlockPlaceSent = true;
                        placedAbove = await WaitUntilAsync(
                            client, c => (c.State.HasWorld ? c.State.World.GetBlockStateId(above) : 0) != 0 && signals.BlockChanges > placeBefore, TimeSpan.FromSeconds(4), ct.Token);
                    }
                    report.BlockPlace = placedAbove;
                    report.BlockPlaceNote = placedAbove
                        ? "send-ok placed-observed"
                        : (protocol >= 393 ? "send-ok place-not-observed" : "send-ok(wire) no-pre1.13-held-item");
                    if (!placedAbove && protocol >= 393)
                    {
                        string slog = server.LogText;
                        int aboveState = await BlockStateAtAsync(client, above, ct.Token);
                        string filled = await client.InvokeAsync(c =>
                        {
                            if (!c.State.Features.Inventory)
                                return "n/a";

                            var parts = new List<string>();
                            IReadOnlyList<Umpk.Game.Items.ItemStack> slots = c.State.Inventory.PlayerSlots;
                            for (int i = 0; i < slots.Count; i++)
                                if (!slots[i].IsEmpty)
                                    parts.Add($"{i}:{slots[i].Item.Id}x{slots[i].Count}");

                            return string.Join(",", parts);
                        }, ct.Token);
                        _out.WriteLine(
                            $"PLACE-MISS {versionName}: above={above} state={aboveState} changes={signals.BlockChanges - placeBefore} " +
                            $"heldSlot={await client.InvokeAsync(c => c.State.Self.HeldSlot, ct.Token)} filled=[{filled}]\n" +
                            $"SERVERLOG-TAIL:\n{(slog.Length > 1200 ? slog[^1200..] : slog)}");
                    }
                    // Clean the placed block so it does not interfere with the dig assertion.
                    if (placedAbove)
                        await server.SendConsoleAsync($"setblock {above.X} {above.Y} {above.Z} minecraft:air", ct.Token);

                }
                else
                    report.BlockPlaceNote = "n/a-send-marker";

                // Client dig SEND: break the target, observe the server's break block_update (the target goes to air and a block change is counted). Asserted where the dig is drivable and the placement receive round-trip succeeded, which closes the loop.
                int blockBefore = signals.BlockChanges;
                if (canDig)
                {
                    // The dig broke the block iff the server emits a block_update clearing the TARGET to air. A very active world can refill that spot within a tick (water/lava/gravity), so observe the transient break-to-air via the block_update event stream rather than requiring the FINAL world state to be air (which a refill would falsely fail).
                    int targetBrokeToAir = 0;
                    using IDisposable brokeSub = client.Events.Subscribe<BlockChanged>(bc =>
                    {
                        if (bc.Position.Equals(target) && bc.BlockStateId == 0)
                            Interlocked.Increment(ref targetBrokeToAir);

                    });

                    await client.Actions.Interaction.DigBlockAsync(target, ct: ct.Token);
                    report.BlockDigSent = true;
                    // Broke iff the target cleared to air, observed EITHER as a transient break-to-air block_update event (robust to an immediate world refill) OR as the final on-loop world state being air (robust when the break arrives batched in a section_blocks_update, which updates World but does not emit a per-block BlockChanged event).
                    report.BlockDigBroke = await WaitUntilAsync(
                        client,
                        c => Volatile.Read(ref targetBrokeToAir) > 0
                            || (c.State.HasWorld && c.State.World.GetBlockStateId(target) == 0),
                        TimeSpan.FromSeconds(12), ct.Token);
                }

                report.BlockBreakNote = !canDig
                    ? "recv-ok dig-send-marker"
                    : report.BlockDigBroke
                        ? "recv+dig-broke"
                        : $"recv-ok dig-sent server-break-not-observed(changes={signals.BlockChanges - blockBefore})";

                // Where the dig is drivable and the block was placed, the client dig MUST break it server-side (the pre-1.19 spurious-sequence bug would have dropped the session or been rejected here).
                if (canDig && placed)
                {
                    int digState = await BlockStateAtAsync(client, target, ct.Token);
                    Assert.True(
                        report.BlockDigBroke,
                        $"{versionName}: client-initiated dig did not break the block server-side (F6); " +
                        $"state={digState}, changes={signals.BlockChanges - blockBefore}, " +
                        $"status={client.Status}, {signals.LastDisconnect}.");
                }
            }
            else
            {
                // blockObservable reduces to rxBlock across the current matrix (the structural-column term is matrix-wide true), so the only reachable n/a here is the receive marker.
                report.BlockBreakNote = "n/a-rx-marker";
                report.BlockPlaceNote = "n/a-rx-marker";
            }

            // chest leg: place a chest via console, fill it, open it with a client right-click (use_item_on - block interaction wins over placement for a non-sneaking player), assert the open_screen -> ContainerOpened round-trip and the content decode, then quick-move the stack out and assert the server-confirmed click. Gated on the use_item_on send (not bound at 1.8, which uses the legacy block-place wire) and the open_screen receive (a marker on 1.9-1.13.2); the container content receive decodes matrix-wide.
            var chestPos = new BlockPos((int)Math.Floor(spawnPos.X) - 2, (int)Math.Floor(spawnPos.Y) + 1, (int)Math.Floor(spawnPos.Z));
            if (canPlace && rxOpenScreen)
            {
                // A chest refuses to open under a solid block, and the randomized per-boot spawn offset means terrain can sit right above the chosen spot; clear that cell first.
                await server.SendConsoleAsync($"setblock {chestPos.X} {chestPos.Y + 1} {chestPos.Z} minecraft:air", ct.Token);
                bool chestSet = false;
                for (int attempt = 0; attempt < 6 && !chestSet; attempt++)
                {
                    await server.SendConsoleAsync($"setblock {chestPos.X} {chestPos.Y} {chestPos.Z} minecraft:chest", ct.Token);
                    chestSet = await WaitUntilAsync(
                        client, c => (c.State.HasWorld ? c.State.World.GetBlockStateId(chestPos) : 0) != 0, TimeSpan.FromSeconds(6), ct.Token);
                }

                // Fill chest slot 0 with stone from the console. 1.17 replaced `replaceitem` with `item replace`; the chest leg's drivable band starts at 1.14, so only those two forms apply (the pre-1.13 setblock-with-Items-NBT form never runs here).
                string fill = protocol >= 755
                    ? $"item replace block {chestPos.X} {chestPos.Y} {chestPos.Z} container.0 with minecraft:stone 5"
                    : $"replaceitem block {chestPos.X} {chestPos.Y} {chestPos.Z} container.0 minecraft:stone 5";
                await server.SendConsoleAsync(fill, ct.Token);

                // Look at the chest so the use_item_on hit lands on the intended block, then right-click.
                if (canMove)
                {
                    await client.Actions.Movement.LookAtAsync(new Vec3d(chestPos.X + 0.5, chestPos.Y + 0.5, chestPos.Z + 0.5), ct.Token);
                    await client.Actions.Movement.SendPositionAsync(ct.Token);
                }

                // The right-click is retried bounded: a single use_item_on can be swallowed around chunk resend/teleport timing windows, and re-clicking an unopened chest is idempotent (the retry loop exits the moment an open is observed).
                int opensBefore = signals.ContainerOpens;
                for (int attempt = 0; attempt < 3 && signals.ContainerOpens == opensBefore; attempt++)
                {
                    await client.Actions.Interaction.PlaceBlockAsync(chestPos, Direction.Up, new Vec3d(0.5, 1.0, 0.5), ct: ct.Token);
                    await WaitUntilAsync(() => signals.ContainerOpens > opensBefore, TimeSpan.FromSeconds(4), ct.Token);
                }

                report.ChestOpen = signals.ContainerOpens > opensBefore;
                if (!report.ChestOpen)
                {
                    report.ChestOpenNote = $"no-open(chestSet={chestSet},opens={signals.ContainerOpens})";
                    string slog = server.LogText;
                    int chestState = await BlockStateAtAsync(client, chestPos, ct.Token);
                    _out.WriteLine(
                        $"CHEST-MISS {versionName}: chest={chestPos} state={chestState} chestSet={chestSet} " +
                        $"opens={signals.ContainerOpens} status={client.Status}\n" +
                        $"SERVERLOG-TAIL:\n{(slog.Length > 1200 ? slog[^1200..] : slog)}\n" +
                        $"CLIENTLOG:\n{logs.Dump(30)}");
                }

                if (report.ChestOpen)
                {
                    if (rxInv)
                    {
                        // Content: the console fill must decode into the open container view (chest slot 0 holds the stone).
                        report.ChestContent = await WaitUntilAsync(
                            client,
                            c => c.State.Inventory.HasOpenContainer
                                && c.State.Inventory.ContainerSlots is { Count: > 0 } slots
                                && !slots[0].IsEmpty,
                            TimeSpan.FromSeconds(8), ct.Token);

                        // Click: quick-move the stone out of chest slot 0, then prove the round-trip on BOTH sides. Client side: the moved slot must stay empty after reconciliation (a rejected click is resynced by the server, refilling it). Server side: the chest block entity must actually have lost the stone, probed authoritatively through the console (`data get block ... Items`) since an exactly-correct prediction produces zero container traffic on the 1.17.1+ state-id protocol (the click's changed-slots map already matches the server result, so nothing is broadcast).
                        if (report.ChestContent)
                        {
                            int contentBefore = signals.ContainerContentChanges;
                            await client.Actions.Inventory.QuickMoveAsync(0, ct.Token);
                            bool slotEmptied = await WaitUntilAsync(
                                client,
                                c => c.State.Inventory.HasOpenContainer
                                    && c.State.Inventory.ContainerSlots is { Count: > 0 } slots
                                    && slots[0].IsEmpty,
                                TimeSpan.FromSeconds(6), ct.Token);

                            int logStart = server.LogText.Length;
                            bool serverEmptied = false;
                            for (int attempt = 0; attempt < 3 && !serverEmptied; attempt++)
                            {
                                await server.SendConsoleAsync(
                                    $"data get block {chestPos.X} {chestPos.Y} {chestPos.Z} Items", ct.Token);
                                serverEmptied = await WaitUntilAsync(
                                    () =>
                                    {
                                        string tail = server.LogText[Math.Min(logStart, server.LogText.Length)..];
                                        return tail.Contains("block data: []", StringComparison.Ordinal)
                                            || (tail.Contains("block data:", StringComparison.Ordinal)
                                                && !tail.Contains("stone", StringComparison.Ordinal));
                                    },
                                    TimeSpan.FromSeconds(3), ct.Token);
                            }

                            report.ChestClick = slotEmptied && serverEmptied;
                            if (!report.ChestClick)
                                report.ChestClickNote =
                                    $"click(slotEmptied={slotEmptied},serverEmptied={serverEmptied},contentDelta={signals.ContainerContentChanges - contentBefore})";

                        }
                    }
                    else
                    {
                        report.ChestContentNote = "n/a-rx-marker";
                        report.ChestClickNote = "n/a-rx-marker";
                    }

                    await client.Actions.Inventory.CloseAsync(ct.Token);
                }

                // Remove the chest so the dig/place world area stays plain for the rest of the leg.
                await server.SendConsoleAsync($"setblock {chestPos.X} {chestPos.Y} {chestPos.Z} minecraft:air", ct.Token);
            }
            else
            {
                string chestGate = canPlace ? "n/a-rx-marker(open_screen)" : "n/a-send-marker(use_item_on)";
                report.ChestOpenNote = chestGate;
                report.ChestContentNote = chestGate;
                report.ChestClickNote = chestGate;
            }

            // keep-alive survival: hold the session for a 60s window from connect
            await SurviveAsync(client, signals, TimeSpan.FromSeconds(60), ct.Token);
            report.SurvivedSeconds = 60;
            report.KeepAliveSurvived = client.Status == ClientStatus.Playing && signals.DisconnectDuringWindow == 0;
            if (!report.KeepAliveSurvived)
            {
                string slog = server.LogText;
                _out.WriteLine($"SURVIVE-DROP {versionName}: {signals.LastDisconnect}\nSERVERLOG:\n{(slog.Length > 1500 ? slog[^1500..] : slog)}\nCLIENTLOG:\n{logs.Dump(30)}");
            }

            Assert.True(
                report.KeepAliveSurvived,
                $"{versionName}: session did not survive the 60s keep-alive window (status={client.Status}, " +
                $"disconnects={signals.DisconnectDuringWindow}, {signals.LastDisconnect}).");

            // strong-consistency snapshot of decoded state
            (int worldNonAir, bool hasWorld, int entityCount, int playerSlotCount, bool inventoryHasItem, bool commandTree) =
                await client.InvokeAsync(c =>
                {
                    int nonAir = c.State.HasWorld ? CountWorldNonAir(c.State.World) : 0;
                    int slots = c.State.Features.Inventory ? c.State.Inventory.PlayerSlots.Count : 0;
                    bool hasItem = c.State.Features.Inventory && AnyPlayerSlotFilledState(c.State);
                    bool cmdTree = c.State.ServerCommands.Tree is not null;
                    int entities = c.State.Features.Entities ? c.State.Entities.Count : 0;
                    return (nonAir, c.State.HasWorld, entities, slots, hasItem, cmdTree);
                }, ct.Token);

            // Every supported era exposes a structural block grid, so every leg must contain a decoded non-air world block.
            report.HasWorld = hasWorld;
            report.WorldNonAir = worldNonAir;
            report.ChunkLoads = signals.ChunkLoads;
            report.World = worldNonAir > 0;
            Assert.True(
                report.World,
                $"{versionName}: expected a decoded non-air world block; got {worldNonAir} " +
                $"(chunkLoads={signals.ChunkLoads}, hasWorld={hasWorld}).");

            // entities: spawn tracked AND removal observed (summon + kill round-trip)
            report.EntitySpawns = signals.EntitySpawns;
            if (rxEntity)
            {
                report.Entities = signals.EntitySpawns > 0 && signals.EntityRemovals > 0;
                Assert.True(
                    report.Entities,
                    $"{versionName}: expected a summoned entity to be tracked then removed; " +
                    $"spawns={signals.EntitySpawns}, removals={signals.EntityRemovals}, tracked={entityCount}.");
            }
            else
                report.EntitiesNote = "n/a-rx-marker";

            // inventory
            if (!rxInv)
            {
                // Pre-1.13 marker eras: container_set_content is a verbatim marker, so the inventory is not observable in decoded state.
                report.Inventory = true;
                report.InventoryNote = "n/a-rx-marker";
            }
            else if (protocol >= 393)
            {
                // 1.13+ (index-based item registry installed): the given item stack must decode into a slot, exercising the full inventory item round-trip (server /give -> set_slot -> decoded state).
                report.Inventory = inventoryHasItem && playerSlotCount > 0;
                Assert.True(
                    report.Inventory,
                    $"{versionName}: expected the given item to decode into a player inventory slot; " +
                    $"hasItem={inventoryHasItem}, slots={playerSlotCount}, contentChanges={signals.ContainerContentChanges}.");
            }
            else
            {
                // Pre-1.13 (legacy composite item ids not derivable from the flat name table, documented): validate the inventory content-sync pipeline (join content decodes and sizes the window).
                report.Inventory = signals.ContainerContentChanges > 0 && playerSlotCount > 0;
                Assert.True(
                    report.Inventory,
                    $"{versionName}: expected the join inventory content sync to decode and size the player " +
                    $"inventory; contentChanges={signals.ContainerContentChanges}, slots={playerSlotCount}.");
            }

            // chat (receive)
            if (rxChat)
                Assert.True(report.Chat, $"{versionName}: expected the server chat broadcast to decode; last={signals.LastChat}.");

            else
                report.ChatNote = "n/a-rx-marker";

            // chat (send round-trip: server log always, client echo where the player broadcast decodes)
            if (canChatSend)
                Assert.True(
                    report.ChatSend,
                    $"{versionName}: expected the client chat send to reach the server log" +
                    $"{(rxPlayerEcho ? " and echo back through the chat-received path" : string.Empty)}; " +
                    $"note={report.ChatSendNote}.");

            // Apply an effect on every leg and assert it where update_mob_effect is implemented.
            if (rxEntity && rxEffect)
                Assert.True(
                    report.Effects,
                    $"{versionName}: expected the console potion effect to decode (update_mob_effect) and be " +
                    $"tracked on Self; effectsApplied={signals.EffectsApplied}.");

            // entity interaction: the client attack wire must provably damage the summoned cow.
            bool canAttack2 = LiveMatrix.SendImplemented(version, "interact") || LiveMatrix.SendImplemented(version, "attack");
            if (rxEntity && canAttack2)
                Assert.True(
                    report.EntityAttack,
                    $"{versionName}: expected the client attack to kill the cow or observe a health decrease; " +
                    $"note={report.EntityAttackNote}.");

            // entity metadata: the summoned cow must have produced decoded set_entity_data.
            if (rxEntity && rxMetadata)
                Assert.True(
                    report.EntityMetadata,
                    $"{versionName}: expected at least one decoded metadata update for the summoned cow.");

            // chest open + content + click (gated as driven above).
            if (canPlace && rxOpenScreen)
            {
                Assert.True(
                    report.ChestOpen,
                    $"{versionName}: expected the client right-click on the chest to open a container " +
                    $"(open_screen -> ContainerOpened); note={report.ChestOpenNote}.");
                if (rxInv)
                {
                    Assert.True(
                        report.ChestContent,
                        $"{versionName}: expected the console-filled chest content to decode into the open " +
                        $"container view; contentChanges={signals.ContainerContentChanges}.");
                    Assert.True(
                        report.ChestClick,
                        $"{versionName}: expected the quick-move click round-trip to be confirmed by the server " +
                        $"(authoritative container update + reconciled slot); note={report.ChestClickNote}.");
                }
            }

            // command effect (time set -> SetTime observed)
            if (rxTime)
                Assert.True(report.CommandEffect, $"{versionName}: expected a server command (time set) to change client state; timeChanges={signals.TimeChanges}.");

            else
                report.CommandEffectNote = "n/a-rx-marker";

            // The client's world must reflect the server's block update where that receive codec exists.
            if (blockObservable)
                Assert.True(
                    report.BlockBreak,
                    $"{versionName}: expected the placed block to appear in ClientState.World (block_update receive " +
                    $"round-trip); note={report.BlockBreakNote}.");

            // block place round-trip: with the held slot selected onto the given stone, the client use_item_on must produce a server-confirmed block above the target. Asserted wherever the place is drivable (not 1.8) and the give landed an item (1.13+ item registry); pre-1.13 the wire is still driven but the client holds nothing, so only the send is recorded.
            if (canPlace && protocol >= 393 && blockObservable)
                Assert.True(
                    report.BlockPlace,
                    $"{versionName}: expected the client-placed block to be observed via block_update; " +
                    $"note={report.BlockPlaceNote}.");

            // movement send (only where the serverbound movement codec is implemented)
            if (LiveMatrix.SendImplemented(version, "move_player_pos"))
                Assert.True(
                    report.Movement,
                    $"{versionName}: expected the serverbound position/rotation send to be accepted; err={report.MovementError}.");

            // commands: the declare-commands tree exists on the wire from 1.13 (protocol 393), but the codec is a verbatim marker on 1.16-1.18.2 (protocols 735-758; 1.19+ decode again), so gate the decode assertion on the codec being IMPLEMENTED (honest per version) rather than on the
            // protocol alone. Where it is a marker the tree is not decoded into state and is recorded n/a;
            // pre-1.13 must have no tree at all.
            bool rxCommands = LiveMatrix.ReceiveImplemented(version, "commands");
            report.CommandTree = commandTree;
            if (rxCommands)
            {
                report.Commands = commandTree;
                Assert.True(commandTree, $"{versionName}: expected a decoded server command tree where the codec is implemented.");
            }
            else if (LiveMatrix.HasCommandTree(protocol))
            {
                // 1.16-1.18.2: declare-commands is present on the wire but kept verbatim (era marker), so it is not decoded into ClientState.ServerCommands - recorded n/a, not asserted.
                report.Commands = true;
                report.CommandsNote = "n/a-rx-marker";
            }
            else
            {
                report.Commands = !commandTree;
                Assert.False(commandTree, $"{versionName}: pre-1.13 expected no server command tree.");
            }
        }
        finally
        {
            report.MovementFinalStatus = client.Status.ToString();
        }

        // clean disconnect
        await client.DisconnectAsync(CancellationToken.None);
        report.Disconnect = client.Status == ClientStatus.Disconnected;
        Assert.True(report.Disconnect, $"{versionName}: expected Disconnected after DisconnectAsync, got {client.Status}.");

        report.Result = "PASS";
        _out.WriteLine(report.ToString());
        LegResultLog.Append(report);
    }

    /// <summary>Builds a client, wires the event signals, and connects with bounded retries.</summary>
    private async Task<UmpkClient> ConnectWithRetryAsync(
        JavaVersion version, int protocol, LegSignals signals, CapturingLoggerFactory logs, CancellationToken ct)
    {
        var endpoint = new ServerEndpoint("127.0.0.1", (ushort)LocalServer.Port);
        Exception? last = null;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            UmpkClient client = new UmpkClientBuilder()
                .UseVersion(version)
                .UseProfile(new GameProfile(Guid.NewGuid(), Username))
                .UseStaticRegistries(JavaGameData.Registries(protocol))
                .UseLoggerFactory(logs)
                // Keep Terrain/Entities/Inventory on for state tracking, but disable the local physics engine: with no position echo the idle physics tick would fall the tracked position into the void and diverge from the server-authoritative spawn, corrupting summon/dig coords. The serverbound position/rotation send path is still exercised via ClientActions.Movement.
                .ConfigureFeatures(f => { f.Physics = false; f.Pathfinding = false; })
                .ConfigureOptions(o => o.AutoSendPosition = false)
                .Build();

            signals.Attach(client);
            try
            {
                await client.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                return client;
            }
            catch (Exception ex)
            {
                last = ex;
                await client.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Could not connect after retries.", last);
    }

    /// <summary>Holds the session for <paramref name="window"/>, failing fast if it disconnects early.</summary>
    private static async Task SurviveAsync(UmpkClient client, LegSignals signals, TimeSpan window, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            if (client.Status == ClientStatus.Disconnected || signals.DisconnectDuringWindow > 0)
                return;

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>Like <see cref="WaitUntilAsync(Func{bool}, TimeSpan, CancellationToken)"/> but the condition is evaluated ON the session loop. World mutations (block_update etc.) apply on the loop, so an off-loop read of the block grid can miss a just-applied write under weak memory ordering; polling the condition via the loop makes the block round-trip observation race-free.</summary>
    private static async Task<bool> WaitUntilAsync(UmpkClient client, Func<UmpkClient, bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await client.InvokeAsync(condition, ct).ConfigureAwait(false))
                return true;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return await client.InvokeAsync(condition, ct).ConfigureAwait(false);
    }

    /// <summary>The tracked world block-state id at a position, read on the session loop.</summary>
    private static Task<int> BlockStateAtAsync(UmpkClient client, BlockPos pos, CancellationToken ct)
        => client.InvokeAsync(c => c.State.HasWorld ? c.State.World.GetBlockStateId(pos) : -1, ct);

    /// <summary>True when the component's flattened text contains the token, searching translatable-argument subtrees explicitly: vanilla wraps a player chat line as <c>chat.type.text</c> whose key template has no placeholders client-side, so <c>ToPlainText</c> alone would drop the message argument.</summary>
    private static bool ComponentContainsText(Umpk.Text.Component component, string token)
    {
        if (component.ToPlainText().Contains(token, StringComparison.Ordinal))
            return true;

        if (component.Content is Umpk.Text.TranslatableContent translatable)
            foreach (Umpk.Text.Component arg in translatable.Args)
                if (ComponentContainsText(arg, token))
                    return true;

        foreach (Umpk.Text.Component child in component.Children)
            if (ComponentContainsText(child, token))
                return true;

        return false;
    }

    /// <summary>Version-appropriate summon command for a cow at the player's position. The cow is summoned with NoAI (supported by every version in the matrix) so it stays put: the client-driven attack needs the target inside the server's interaction reach for the whole bounded hit loop, and a wandering cow would drift out of range while the effects section runs.</summary>
    private static string SummonCommand(int protocol, Vec3d pos)
    {
        // 1.11 (protocol 315) moved entity ids to the namespaced lowercase form; 1.8 - 1.10 use the capitalized short name.
        string entity = protocol < 315 ? "Cow" : "minecraft:cow";
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "summon {0} {1:0.##} {2:0.##} {3:0.##} {{NoAI:1b}}", entity, pos.X, pos.Y, pos.Z);
    }

    /// <summary>Version-appropriate kill command for the summoned cow.</summary>
    private static string KillCommand(int protocol)
        => protocol < 315 ? "kill @e[type=Cow]" : "kill @e[type=minecraft:cow]";

    /// <summary>Version-appropriate potion-effect command targeting the LOCAL PLAYER (the only entity for which vanilla sends update_mob_effect). 1.13 (protocol 393) split the command into <c>effect give</c> with namespaced ids; pre-1.13 uses the flat <c>effect</c> form with a numeric effect id (1 = speed). Long duration so it is still active when the client decodes it.</summary>
    private static string EffectCommand(int protocol) => protocol < 393
        ? $"effect {Username} 1 60 0"                       // pre-1.13: effect <player> <id> <secs> <amp>
        : $"effect give {Username} minecraft:speed 60 0";  // 1.13+: effect give <player> <effect> <secs> <amp>

    /// <summary>True when any player-inventory slot holds an item (off-loop, eventually-consistent read).</summary>
    private static bool AnyPlayerSlotFilled(UmpkClient client)
        => client.State.Features.Inventory && AnyPlayerSlotFilledState(client.State);

    private static bool AnyPlayerSlotFilledState(ClientState state)
    {
        foreach (var slot in state.Inventory.PlayerSlots)
            if (!slot.IsEmpty)
                return true;

        return false;
    }

    /// <summary>The tracked world block-state id at a position, or -1 when the world is unavailable.</summary>
    private static int BlockStateAt(UmpkClient client, BlockPos pos)
        => client.State.HasWorld ? client.State.World.GetBlockStateId(pos) : -1;

    private static int CountWorldNonAir(World world)
    {
        int count = 0;
        foreach (ChunkColumn column in world.LoadedColumns)
            for (int s = 0; s < column.SectionCount && count == 0; s++)
            {
                ChunkSection? section = column.GetSection(s);
                if (section is null)
                    continue;

                for (int y = 0; y < ChunkSection.Size && count == 0; y++)
                    for (int x = 0; x < ChunkSection.Size; x++)
                        for (int z = 0; z < ChunkSection.Size; z++)
                            if (section.GetBlockStateId(x, y, z) != 0)
                                count++;

            }

        return count;
    }
}

/// <summary>Forces sequential execution of all live-server tests (shared state, one port).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveServerCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "live-server";
}
