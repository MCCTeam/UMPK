using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Replays recorded clientbound play frames through the real descriptor binding into the applier chain and asserts concrete tracked state per capture: enough signals that deleting an applier makes the corpus leg fail. Each capture asserts baseline progress (all implemented frames decode cleanly) plus content-specific outcomes structurally present in that fragment: chunk loads, entity spawns and movement, time changes, damage events, and recipe updates. Only signals that are actually asserted are subscribed.</summary>
public sealed class CorpusReplayTests
{
    public static IEnumerable<object[]> Captures()
    {
        string root = FindCorpusRoot();
        if (root is null)
            yield break;

        foreach (string path in CorpusLoader.DiscoverCaptures(root))
            yield return [path];

    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task Replay_Play_Frames_Populates_State(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;

        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(corpus.Protocol);

        // Decode with the capture protocol's real static registries, the way a live session does. An empty registry view puts registry-resolving codecs (item stacks above all) into a degraded mode no session ever runs in, so a frame carrying a real item id would fail here for a reason the client would never hit. Mirrors the same change in the conformance runner.
        var codecContext = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        var signals = new Signals();
        // Only signals this test actually asserts are subscribed, so the subscription set does not imply coverage the assertions lack.
        harness.Events.Subscribe<ChunkLoaded>(_ => signals.ChunkLoaded++);
        harness.Events.Subscribe<EntitySpawned>(_ => signals.EntitySpawned++);
        harness.Events.Subscribe<EntityMoved>(_ => signals.EntityMoved++);
        harness.Events.Subscribe<EntityDamaged>(_ => signals.EntityDamaged++);
        harness.Events.Subscribe<RecipesUpdated>(_ => signals.RecipesUpdated++);
        harness.Events.Subscribe<TimeChanged>(_ => signals.TimeChanged++);

        int applied = 0;
        int decodeFailures = 0;
        // Track which applier-proving packet types the fragment actually carries as implemented codecs. Content-specific assertions below are gated on these: a corpus that never carries an add_entity frame cannot be expected to spawn entities, and one where update_recipes is a marker (protocols 764-767 markerize the pre-1.21.2 string-recipe form) cannot bump the recipe revision. This keeps the property that deleting an applier reddens every corpus that does carry that packet, while not asserting content a fragment structurally lacks.
        bool hasAddEntity = false;
        bool hasUpdateRecipes = false;
        bool hasSetTime = false;
        bool hasEntityMove = false;
        bool loginTerminated = false;
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsEffectivePlayClientbound(descriptor, frame, ref loginTerminated))
                continue;

            if (!descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry) ||
                !registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) || !codec.IsImplemented)
                continue;

            if (codec.Type.Id == Identifier.Minecraft("add_entity"))
                hasAddEntity = true;

            else if (codec.Type.Id == Identifier.Minecraft("update_recipes"))
                hasUpdateRecipes = true;

            else if (codec.Type.Id == Identifier.Minecraft("set_time"))
                hasSetTime = true;

            else if (codec.Type.Id == Identifier.Minecraft("move_entity_pos")
                || codec.Type.Id == Identifier.Minecraft("move_entity_pos_rot")
                || codec.Type.Id == Identifier.Minecraft("teleport_entity")
                || codec.Type.Id == Identifier.Minecraft("entity_position_sync"))
                hasEntityMove = true;

            object packet;
            try
            {
                packet = codec.Decode(frame.Body, codecContext);
            }
            catch
            {
                decodeFailures++;
                continue;
            }

            try
            {
                await harness.ApplyAsync(packet);
                applied++;
            }
            catch
            {
                // A single applier fault must not abort the replay.
            }
        }

        int entityCount = harness.State.EntitiesOrNull?.Count ?? 0;
        int recipeRevision = harness.State.Recipes.Revision;
        string name = Path.GetFileNameWithoutExtension(capturePath).ToLowerInvariant();
        bool isChunkJoin = name.Contains("chunk-join");
        bool is770 = corpus.Protocol == 770;
        string dump = signals.Dump(applied, decodeFailures, entityCount, recipeRevision, harness.State.HasWorld);

        // Baseline: play frames were actually applied.
        Assert.True(applied > 0, $"no play packets applied from {capturePath}. {dump}");

        // Every capture is byte-clean per the conformance suite, so no implemented codec may fail to decode. A future capture that legitimately carries an unimplemented frame is skipped by the IsImplemented gate in the loop and never reaches this counter, so 0 stays correct. If a real capture ever needs an exception, add a small documented allowlist here rather than relaxing it.
        Assert.Equal(0, decodeFailures);

        // Time signal (WorldApplier set_time -> TimeChanged): any capture that carries set_time and built a world must surface at least one time change. 1.8 captures carry set_time but wire up no world/time applier (HasWorld=false), so they are correctly excluded rather than asserted.
        if (hasSetTime && harness.State.HasWorld)
            Assert.True(signals.TimeChanged > 0, $"expected a time change in {name}. {dump}");

        // Entity-movement signal (EntityApplier move/teleport/position-sync -> EntityMoved): any capture that carries a movement packet moves a tracked entity at least once. Keeps EntityMoved from being a dead subscription on the chunk-join and play captures whose histogram has move frames. Gated on hasAddEntity too: movement is only observable on a TRACKED entity, and the pre-1.19 eras (flattening 477-578 and netty-modern 735-758) markerize the split spawn packets (add_mob/add_entity/add_player), so nothing is tracked to move even though the movement packets decode. (Same content-scoping principle as the add_entity/update_recipes gates below; the property holds because every era that implements spawns - 759-776 - still asserts movement.)
        if (hasEntityMove && hasAddEntity)
            Assert.True(signals.EntityMoved > 0, $"expected entity movement in {name}. {dump}");

        // Content-specific, applier-proving assertions. Each ties to a distinct applier so that deleting that applier makes this corpus leg red.
        if (isChunkJoin)
        {
            // WorldApplier: chunk columns loaded and a world built.
            Assert.True(signals.ChunkLoaded > 0, $"expected chunk loads in {name}. {dump}");
            Assert.True(harness.State.HasWorld, $"expected a world in {name}. {dump}");
            // EntityApplier: entities spawned and tracked (only when the fragment carries add_entity;
            // some short 764-767 chunk-join captures contain no spawn packet).
            if (hasAddEntity)
            {
                Assert.True(signals.EntitySpawned > 0, $"expected entity spawns in {name}. {dump}");
                Assert.True(entityCount > 0, $"expected tracked entities in {name}. {dump}");
            }

            // InventoryApplier UpdateRecipes: recipe registry tracked (only where update_recipes is implemented; protocols 764-767 markerize the pre-1.21.2 string-recipe form).
            if (hasUpdateRecipes)
            {
                Assert.True(recipeRevision > 0, $"expected a recipe update in {name}. {dump}");
                Assert.True(signals.RecipesUpdated > 0, $"expected RecipesUpdated in {name}. {dump}");
            }

            if (is770)
            {
                // EntityApplier DamageEvent: only the 770 chunk-join fragment carries damage events.
                Assert.True(signals.EntityDamaged > 0, $"expected damage events in {name}. {dump}");
            }
        }
    }

    /// <summary>Suite-level coverage floor: the per-capture assertions above are gated (entity spawns only asserted where a fragment carries add_entity, recipes only where update_recipes is implemented). If a future corpus refresh dropped every add_entity or every update_recipes frame, those gated legs would silently assert nothing. This fact fails if the entity-applier leg or the recipe-applier leg never fires across the whole corpus set, so gated coverage cannot reach zero unnoticed.</summary>
    [Fact]
    public async Task CorpusSet_Exercises_EntityAndRecipeAppliers_OnAtLeastOneCapture()
    {
        string root = FindCorpusRoot();
        Assert.False(root is null, "corpus root not found");

        bool anyEntitySpawn = false;
        bool anyRecipeUpdate = false;
        foreach (string path in CorpusLoader.DiscoverCaptures(root))
        {
            (int spawned, int recipes) = await ReplayApplierSignalsAsync(path);
            anyEntitySpawn |= spawned > 0;
            anyRecipeUpdate |= recipes > 0;
        }

        Assert.True(anyEntitySpawn, "no capture in the corpus set spawned an entity; the entity-applier leg is unexercised");
        Assert.True(anyRecipeUpdate, "no capture in the corpus set updated recipes; the recipe-applier leg is unexercised");
    }

    private static async Task<(int EntitySpawned, int RecipesUpdated)> ReplayApplierSignalsAsync(string capturePath)
    {
        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        if (!JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version))
            return (0, 0);

        ProtocolDescriptor descriptor = version!.Protocol;
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(corpus.Protocol);
        var codecContext = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        int entitySpawned = 0;
        int recipesUpdated = 0;
        harness.Events.Subscribe<EntitySpawned>(_ => entitySpawned++);
        harness.Events.Subscribe<RecipesUpdated>(_ => recipesUpdated++);

        bool loginTerminated = false;
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsEffectivePlayClientbound(descriptor, frame, ref loginTerminated))
                continue;

            if (!descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry) ||
                !registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) || !codec.IsImplemented)
                continue;

            object packet;
            try
            {
                packet = codec.Decode(frame.Body, codecContext);
            }
            catch
            {
                continue;
            }

            try
            {
                await harness.ApplyAsync(packet);
            }
            catch
            {
                // A single applier fault must not abort the replay.
            }
        }

        return (entitySpawned, recipesUpdated);
    }

    private sealed class Signals
    {
        public int ChunkLoaded, EntitySpawned, EntityMoved, EntityDamaged, RecipesUpdated, TimeChanged;

        public string Dump(int applied, int decodeFailures, int entityCount, int recipeRevision, bool hasWorld)
            => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"signals[applied={applied} decodeFail={decodeFailures} hasWorld={hasWorld} entities={entityCount} " +
                $"chunk={ChunkLoaded} spawn={EntitySpawned} move={EntityMoved} damage={EntityDamaged} " +
                $"recipeRev={recipeRevision} recipeEvt={RecipesUpdated} time={TimeChanged}]");
    }

    // Pre-config phase-lag latch, mirroring ConformanceRunner.CheckFrame. Pre-1.20.2 versions (e.g. the 1.14-1.15.2 flattening era) have no configuration phase and no terminal-packet gate, so once a clientbound login_finished is observed the recorder can still label the first burst of Play frames (JoinGame, chunks) with the lagging Login phase. Treat such a Login-labeled clientbound frame as Play when the Play registry knows its wire id; updates <paramref name="loginTerminated"/> when the login_finished frame itself is seen. Without this, chunk-join/JoinGame frames recorded in the lag window are skipped, so no world is built (the failure this corrects on the 477-578 corpora).
    private static bool IsEffectivePlayClientbound(ProtocolDescriptor descriptor, RecordedFrame frame, ref bool loginTerminated)
    {
        if (frame.Direction != CorpusDirection.Clientbound)
            return false;

        ProtocolPhase phase = MapPhase(frame.Phase);
        if (phase == ProtocolPhase.Play)
            return true;

        if (phase != ProtocolPhase.Login)
            return false;

        if (!loginTerminated &&
            descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry loginReg) &&
            loginReg.TryGetInbound(frame.WireId, out BoundPacketCodec loginCodec) &&
            loginCodec.Type.Id == LoginPackets.Clientbound.LoginFinished.Id)
        {
            loginTerminated = true;
            return false;
        }

        bool hasConfig = descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound, out _) ||
            descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound, out _);
        return loginTerminated && !hasConfig &&
            descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play) &&
            play.TryGetInbound(frame.WireId, out _);
    }

    private static ProtocolPhase MapPhase(CorpusPhase phase) => phase switch
    {
        CorpusPhase.Handshake => ProtocolPhase.Handshake,
        CorpusPhase.Status => ProtocolPhase.Status,
        CorpusPhase.Login => ProtocolPhase.Login,
        CorpusPhase.Configuration => ProtocolPhase.Configuration,
        CorpusPhase.Play => ProtocolPhase.Play,
        _ => ProtocolPhase.Handshake,
    };

    // True when the frame is the clientbound login_finished (LoginSuccess) that terminates the login flow, resolved through the descriptor so no wire-id constant is hard-coded per protocol.
    private static bool IsClientboundLoginFinished(ProtocolDescriptor descriptor, RecordedFrame frame)
    {
        if (MapPhase(frame.Phase) != ProtocolPhase.Login || frame.Direction != CorpusDirection.Clientbound)
            return false;

        return descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry registry) &&
            registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) &&
            codec.Type.Id == LoginPackets.Clientbound.LoginFinished.Id;
    }

    private static string FindCorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null!;
    }
}
