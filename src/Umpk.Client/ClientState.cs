using Umpk.Client.Commands;
using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.Scoreboard;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client;

/// <summary>The full tracked state of a client session. Feature-gated modules throw <see cref="FeatureDisabledException"/> when their feature is off. All mutation happens on the session loop; off-loop reads see eventually-consistent snapshots (use <see cref="UmpkClient.InvokeAsync"/> for strong consistency).</summary>
public sealed class ClientState
{
    private readonly ClientFeatures _features;
    private World? _world;
    private EntityStore? _entities;
    private InventoryState? _inventory;

    /// <summary>The composed feature toggles for this session.</summary>
    public ClientFeatures Features => _features;

    internal ClientState(ClientFeatures features)
    {
        _features = features;
        if (features.Entities)
            _entities = new EntityStore();

        if (features.Inventory)
            _inventory = new InventoryState();

    }

    /// <summary>The local player's tracked state. Always present.</summary>
    public SelfState Self { get; } = new();

    /// <summary>What the session knows about the server: its brand, its measured tick rate, and the keep-alive exchange. Always present.</summary>
    public ServerState Server { get; } = new();

    /// <summary>The SERVER-MEASURED round trip for this client in ms, or null before the server reports one. <see cref="ServerState.ObservedLatency"/> is the tab-list figure kept current by the UI applier; the direct tab-list read is the fallback for the window before that applier has seen an entry for us, and for a server that only publishes the list once. Never confuse it with <see cref="ServerState.KeepAliveTurnaround"/>: a Java client is only ever the RESPONDER on keep-alive and the id is the server's own clock reading, so it cannot measure RTT.</summary>
    public int? ObservedLatency =>
        Server.ObservedLatency
        ?? (TabList.TryGet(Self.Uuid, out TabListEntry? self) ? self.Latency : null);

    /// <summary>The network-synced registries. Populated during configuration.</summary>
    public RegistryAccess? Registries { get; internal set; }

    /// <summary>The scoreboard/teams model. Always present.</summary>
    public Scoreboard Scoreboard { get; } = new();

    /// <summary>The player/tab list. Always present.</summary>
    public TabList TabList { get; } = new();

    /// <summary>Boss bars keyed by uuid. Always present.</summary>
    public BossBarState BossBars { get; } = new();

    /// <summary>Maps by id. Always present.</summary>
    public MapState Maps { get; } = new();

    /// <summary>Advancements. Always present.</summary>
    public AdvancementState Advancements { get; } = new();

    /// <summary>The server command tree, updated by declare-commands. Always present.</summary>
    public ServerCommandsState ServerCommands { get; } = new();

    /// <summary>The recipe state: the unlocked recipe book (enumerate <see cref="RecipeState.UnlockedRecipes"/> / <see cref="RecipeState.UnlockedRecipeIds"/>) plus the raw recipe registry. Always present.</summary>
    public RecipeState Recipes { get; } = new();

    /// <summary>The dialog the server is currently showing (1.21.6+). Always present; it stays empty on versions that carry no dialog packets.</summary>
    public DialogState Dialogs { get; } = new();

    /// <summary>Inbound chat-stream bookkeeping: the running global chat index (1.21.5+) and the count of ordering gaps observed on this join. Always present; inert on protocols that carry no global index.</summary>
    public ChatState Chat { get; } = new();

    /// <summary>A monotone count of the client's own ticks since the session started, the clock every derived duration on this state is dated against (today: <see cref="ActiveEffect.AppliedAtTick"/>).</summary>
    /// <remarks>
    /// <para>Advanced once per iteration of <c>UmpkClient.OnTickOnLoopAsync</c>, which is the client tick and is not gated on any feature (only the physics block INSIDE it is), so the counter runs whenever a session runs. It is the client's own cadence, not the server's, so anything derived from it carries client/server tick drift - which is the drift every other prediction on this loop already lives with, and why a derived duration reports itself as estimated.</para>
    /// <para>It counts TICKS, not wall time, and it never resets within a session, so subtraction across two reads is always a non-negative tick count.</para>
    /// </remarks>
    public long SessionTick { get; private set; }

    /// <summary>Advances the session tick counter. Session-loop only.</summary>
    /// <param name="count">How many ticks to advance; must be positive.</param>
    internal void AdvanceSessionTick(int count = 1) => SessionTick += count;

    /// <summary>The world. Requires the Terrain feature and a completed join.</summary>
    public World World => _world
        ?? throw (_features.Terrain
            ? new InvalidOperationException("The world is not available until the session has joined a dimension.")
            : new FeatureDisabledException("Terrain"));

    /// <summary>Whether the world has been created (dimension known).</summary>
    public bool HasWorld => _world is not null;

    /// <summary>Whether the chunk column the local player is standing in has actually been received. False both before the first column of a freshly installed world arrives (a join, a respawn or a dimension change) and whenever the player is outside every loaded column.</summary>
    /// <remarks>Supported clients use this readiness condition for both the local physics tick and the movement report. Older eras test whether the player's current chunk is loaded. Newer eras use the connection's level-load readiness flag, which resets on join and respawn. Without it the client integrates gravity against a world with no terrain in it yet, so every dimension change begins with a free fall from the arrival position that the server then has to teleport back, and the falling positions go out on the wire as real movement.</remarks>
    public bool IsLocalChunkLoaded =>
        _world is { } world && world.GetColumn(BlockPos.Containing(Self.Position)) is not null;

    /// <summary>The entity tracker. Requires the Entities feature.</summary>
    public EntityStore Entities => _entities ?? throw new FeatureDisabledException("Entities");

    /// <summary>The inventory tracker. Requires the Inventory feature.</summary>
    public InventoryState Inventory => _inventory ?? throw new FeatureDisabledException("Inventory");

    internal EntityStore? EntitiesOrNull => _entities;

    internal InventoryState? InventoryOrNull => _inventory;

    internal World? WorldOrNull => _world;

    internal void InstallWorld(World world) => _world = world;

    internal void ResetForRespawn(World world) => _world = world;

    /// <summary>
    /// Tears down everything the play phase built, for a play-to-configuration re-entry (<c>minecraft:start_configuration</c>, 1.20.2+). The protocol's re-entry behavior determines what survives:
    /// <list type="bullet">
    /// <item>
    /// The client discards the level and play state, including the player, tab list, and boss bars. Everything held on the play listener or on the level therefore dies here: the player info map, the command dispatcher, the recipe container, the advancements, the scoreboard, the map data and every entity.
    /// </item>
    /// <item>
    /// Session metadata survives both hops and is not resent. Re-entry sends only <c>finish_configuration</c>; no registry data, tags, known-pack negotiation, or brand follows. Clearing the registries here would leave the client with nothing to resolve dimension types against for the rest of the session.
    /// </item>
    /// </list>
    /// Surviving, deliberately: <see cref="Registries"/>, <see cref="Server"/> (its brand above all), the cookie store held by the client, and the local player's identity on <see cref="Self"/>.
    /// </summary>
    internal void ResetForConfigurationReentry()
    {
        // Level-owned in vanilla: cleared with the level, refilled by the join that ends the re-entry.
        _world = null;
        _entities?.Clear();
        Scoreboard.Clear();
        Maps.Clear();

        // Play-listener-owned in vanilla: recreated with the listener.
        TabList.Clear();
        TabList.Header = null;
        TabList.Footer = null;
        BossBars.Clear();
        Advancements.Clear();
        ServerCommands.Tree = null;
        Recipes.Clear();
        _inventory?.Clear();

        // player behavior becomes null, so nothing may be sent that presumes a spawned player; the join packet that ends the re-entry sets this true again.
        Self.HasSpawned = false;

        // The screen is replaced by ServerReconfigScreen, which closes any dialog the play phase showed.
        Dialogs.Clear();
    }

    /// <summary>Drops the state that must not survive a session. A dialog belongs to the connection that showed it, so a reconnect must not present a stale one as still open.</summary>
    /// <returns>True when a dialog was open and has been cleared.</returns>
    internal bool ResetForSessionEnd()
    {
        bool wasShowing = Dialogs.IsShowing;
        Dialogs.Clear();
        Server.ResetForSessionEnd();

        // The chat-signing counters describe ONE connection's profile key: the coordinator that owns them is built per connect and per negotiated version. Leaving them behind lets a 1.19.3 session that rotated once reconnect to a 1.19.2 server and report ProfileKeyRotationSupported == false beside ProfileKeyRotations == 1, plus a refresh instant belonging to a key this session never held.
        Chat.ResetProfileKeyState();
        return wasShowing;
    }
}
