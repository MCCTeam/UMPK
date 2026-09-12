using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Snapshots;

/// <summary>The off-loop read-model. Every method marshals onto the session loop through <see cref="UmpkClient.InvokeAsync{T}"/>, because tracked state is mutated there and enumerating a live dictionary off-loop is how a host gets a torn read. Each has a pure static Project counterpart so the projection is pinned by a unit test with no loop at all.</summary>
public sealed class ClientSnapshots
{
    private readonly UmpkClient _client;

    internal ClientSnapshots(UmpkClient client) => _client = client;

    /// <summary>Snapshots the local player's identity, kinematics, vitals, and game mode.</summary>
    public Task<SelfSnapshot> SelfAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => SelfSnapshot.Project(client.State), ct);

    /// <summary>Snapshots the local player's server-advertised abilities.</summary>
    public Task<AbilitiesSnapshot> AbilitiesAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => AbilitiesSnapshot.Project(client.State.Self), ct);

    /// <summary>Snapshots the local player's active status effects.</summary>
    public Task<IReadOnlyList<EffectSnapshot>> EffectsAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => EffectSnapshot.Project(client.State), ct);

    /// <summary>Snapshots the tab (player) list, including header and footer.</summary>
    public Task<TabListSnapshot> TabListAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => TabListSnapshot.Project(client.State.TabList), ct);

    /// <summary>Snapshots the scoreboard objectives and teams.</summary>
    public Task<ScoreboardSnapshot> ScoreboardAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => ScoreboardSnapshot.Project(client.State.Scoreboard), ct);

    /// <summary>Snapshots the active boss bars.</summary>
    public Task<IReadOnlyList<BossBarSnapshot>> BossBarsAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => Project(client.State.BossBars), ct);

    /// <summary>Snapshots the advancements. Structured advancement contents decode only on protocols 770-773 and 26.2; on other bands UMPK relays the packet verbatim and the advancement set stays empty (hole H14, see <see cref="AdvancementsSnapshot.Entries"/>).</summary>
    public Task<AdvancementsSnapshot> AdvancementsAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => AdvancementsSnapshot.Project(client.State.Advancements), ct);

    /// <summary>Reads the session info: the connected endpoint, the negotiated version name and protocol, the server brand, the measured tick rate, and the two latency figures. Every nullable member means "not known yet" rather than a zero.</summary>
    public Task<SessionSnapshot> SessionAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => SessionSnapshot.Project(client.State, client.Session), ct);

    /// <summary>Snapshots the player inventory: all 46 slots, the held slot, the cursor, and the state id. Requires the Inventory feature.</summary>
    public Task<PlayerInventorySnapshot> PlayerInventoryAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => PlayerInventorySnapshot.Project(client.State), ct);

    /// <summary>Snapshots the currently open container (window id, slots, properties, trades, semantic menu type), or null when only the player inventory is open. Requires the Inventory feature.</summary>
    public Task<OpenContainerSnapshot?> OpenContainerAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => OpenContainerSnapshot.Project(client.State), ct);

    /// <summary>Snapshots the cursor (carried) stack. Requires the Inventory feature.</summary>
    public Task<ItemStack> CursorAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => client.State.Inventory.Cursor, ct);

    /// <summary>Snapshots the recipe book: the unlocked recipes named both ways the wire can name them, the four per-book open/filtering flags, and the count of additions this build keeps opaque. Always present, even on a version that carries no recipe-book packets (the book simply stays empty).</summary>
    public Task<RecipeBookSnapshot> RecipeBookAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => RecipeBookSnapshot.Project(client.State.Recipes), ct);

    /// <summary>Snapshots the stack in the selected hotbar slot. Requires the Inventory feature.</summary>
    public Task<ItemStack> HeldItemAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(
            client => PlayerInventorySnapshot.ResolveHeldItem(client.State.Inventory.PlayerSlots, client.State.Self.HeldSlot), ct);

    /// <summary>Reads which recipe-book placement form the negotiated version actually uses. There is exactly one live form per version; branch on it rather than on the shape of a typed recipe (see <see cref="RecipeCraftPlan.Plan"/>).</summary>
    public Task<RecipePlacementSupport> RecipePlacementAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => RecipePlacementSupport.Resolve(client.Actions.Capabilities, client.Session), ct);

    /// <summary>Snapshots the dialog the server is currently showing (1.21.6+), or null when none is open. Never throws on a version without dialogs; the state simply stays empty there.</summary>
    public Task<DialogSnapshot?> DialogAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => DialogSnapshot.Project(client.State.Dialogs), ct);

    /// <summary>Snapshots every tracked entity. Requires the Entities feature.</summary>
    public Task<IReadOnlyList<EntitySnapshot>> EntitiesAsync(CancellationToken ct = default) =>
        _client.InvokeAsync(client => Project(client.State.Entities.All), ct);

    /// <summary>Snapshots the entity with the given network id, or null when untracked. Requires the Entities feature.</summary>
    /// <remarks>Named distinctly from <see cref="EntityByUuidAsync"/> rather than overloaded as <c>EntityAsync</c>: two same-arity overloads (int-keyed, uuid-keyed), each wanting its own defaulted <see cref="CancellationToken"/>, is exactly the shape RS0026/RS0027 (this package's public-API back-compat analyzers) forbid - see <see href="https://github.com/dotnet/roslyn/blob/main/docs/Adding%20Optional%20Parameters%20in%20Public%20API.md"/>.</remarks>
    public Task<EntitySnapshot?> EntityByIdAsync(int entityId, CancellationToken ct = default) =>
        _client.InvokeAsync(
            client => client.State.Entities.TryGet(entityId, out Entity? entity) ? EntitySnapshot.Project(entity) : null, ct);

    /// <summary>Snapshots the entity with the given uuid, or null when untracked. Requires the Entities feature.</summary>
    public Task<EntitySnapshot?> EntityByUuidAsync(Guid uuid, CancellationToken ct = default) =>
        _client.InvokeAsync(
            client => client.State.Entities.TryGetByUuid(uuid, out Entity? entity) ? EntitySnapshot.Project(entity) : null, ct);

    /// <summary>Snapshots every tracked entity whose type id matches <paramref name="typeId"/> (a namespaced or bare id, for example <c>minecraft:zombie</c> or <c>zombie</c>). Requires the Entities feature.</summary>
    /// <exception cref="ArgumentException"><paramref name="typeId"/> is null, empty, or whitespace.</exception>
    public Task<IReadOnlyList<EntitySnapshot>> EntitiesOfTypeAsync(string typeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        return _client.InvokeAsync(client => Project(client.State.Entities.OfType(typeId)), ct);
    }

    /// <summary>Snapshots tracked entities within <paramref name="radius"/> blocks of the local player. Requires the Entities feature.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
    public Task<IReadOnlyList<EntitySnapshot>> EntitiesNearbyAsync(double radius, CancellationToken ct = default)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        return _client.InvokeAsync(
            client => Project(client.State.Entities.Nearby(client.State.Self.Position, radius)), ct);
    }

    /// <summary>Snapshots the closest tracked entity within <paramref name="radius"/> blocks of the local player, or null when none qualify. When <paramref name="predicate"/> is given it is applied before distance, so a closer entity that fails it never shadows a farther one that passes. Requires the Entities feature.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
    public Task<EntitySnapshot?> NearestEntityAsync(
        double radius, Func<Entity, bool>? predicate = null, CancellationToken ct = default)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        return _client.InvokeAsync(client =>
        {
            Entity? nearest = client.State.Entities.Nearest(client.State.Self.Position, radius, predicate);
            return nearest is null ? null : EntitySnapshot.Project(nearest);
        }, ct);
    }

    /// <summary>Reads the block at a world position. Requires the Terrain feature.</summary>
    public Task<BlockSnapshot> BlockAsync(BlockPos position, CancellationToken ct = default) =>
        _client.InvokeAsync(client => ReadBlock(client.State.World, position), ct);

    /// <summary>Samples the top-down surface of a rectangular XZ region in one session-loop read; see <see cref="SurfaceRegionSnapshot.Sample"/>. Requires the Terrain feature.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="length"/> is not positive.</exception>
    public Task<SurfaceRegionSnapshot> SurfaceRegionAsync(
        int originX, int originZ, int width, int length, int? ceilingY = null, CancellationToken ct = default)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        return _client.InvokeAsync(
            client => SurfaceRegionSnapshot.Sample(client.State.World, originX, originZ, width, length, ceilingY), ct);
    }

    /// <summary>A grid of per-chunk loaded state, <paramref name="columns"/> x <paramref name="rows"/>, centered on the local player's chunk. Requires the Terrain feature.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> or <paramref name="rows"/> is not positive.</exception>
    public Task<ChunkStatusGrid> ChunkStatusAsync(int columns, int rows, CancellationToken ct = default)
    {
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));

        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows));

        return _client.InvokeAsync(client =>
        {
            ChunkPos center = ChunkPos.Containing(BlockPos.Containing(client.State.Self.Position));
            return ChunkStatusGrid.Around(client.State.World, center, columns, rows);
        }, ct);
    }

    /// <summary>Casts a ray from the local player's eye along the view direction up to <paramref name="maxDistance"/> blocks, honoring the negotiated protocol's own block shapes, and returns the first block hit, or null on a miss. Also null before the world exists (no session, or the Terrain feature disabled): a raycast with nothing to hit is a miss, not a feature error.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is not positive.</exception>
    public Task<BlockHitResult?> RaycastViewAsync(
        double maxDistance = PlayerReach.SurvivalBlockReach, bool includeFluids = false, CancellationToken ct = default)
    {
        if (maxDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));

        return _client.InvokeAsync(client => CastView(client, maxDistance, includeFluids), ct);
    }

    private static IReadOnlyList<EntitySnapshot> Project(IEnumerable<Entity> entities)
    {
        var list = new List<EntitySnapshot>();
        foreach (Entity entity in entities)
            list.Add(EntitySnapshot.Project(entity));

        return list;
    }

    private static IReadOnlyList<BossBarSnapshot> Project(BossBarState bars)
    {
        var list = new List<BossBarSnapshot>(bars.Count);
        foreach (BossBar bar in bars.Bars)
            list.Add(BossBarSnapshot.Project(bar));

        return list;
    }

    private static BlockSnapshot ReadBlock(World world, BlockPos position)
    {
        // The loaded flag has to come from the column, not from the state: an unloaded column reads as air (World.GetBlockStateId returns 0 outside loaded chunks), indistinguishable from a real air block.
        bool loaded = world.GetColumn(position) is not null;
        BlockState state = world.GetBlock(position);
        return new BlockSnapshot(position, state, loaded);
    }

    private static BlockHitResult? CastView(UmpkClient client, double maxDistance, bool includeFluids)
    {
        if (!client.State.HasWorld || client.Session is null)
            return null;

        SelfState self = client.State.Self;
        Vec3d eye = PlayerReach.EyePosition(self.Position);
        double yawRad = self.Yaw * Math.PI / 180.0;
        double pitchRad = self.Pitch * Math.PI / 180.0;
        double cosPitch = Math.Cos(pitchRad);
        var direction = new Vec3d(-Math.Sin(yawRad) * cosPitch, -Math.Sin(pitchRad), Math.Cos(yawRad) * cosPitch);
        Vec3d end = eye.Add(direction.Scale(maxDistance));

        // The negotiated protocol's own generated shape tables (real slab/stair/fence/wall/pane/carpet geometry per protocol), resolved from the live session rather than one fixed dataset.
        IBlockShapeSource shapes = JavaGameData.BlockShapes(client.Session.Version.Version.Protocol);
        BlockHitResult hit = Raycast.CastBlock(client.State.World, eye, end, includeFluids, shapes);
        return hit.Hit ? hit : null;
    }
}
