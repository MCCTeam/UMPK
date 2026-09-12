namespace Umpk.Client;

/// <summary>Build-time composition of the optional client state/behavior modules. A disabled module registers no appliers and allocates no storage; the matching <see cref="ClientState"/> property throws <see cref="FeatureDisabledException"/>. Physics implies terrain; pathfinding implies physics.</summary>
public sealed class ClientFeatures
{
    private bool _terrain = true;
    private bool _entities = true;
    private bool _inventory = true;
    private bool _physics = true;
    private bool _pathfinding = true;

    /// <summary>Whether world/terrain tracking (chunks, light, biomes, border) is enabled.</summary>
    public bool Terrain
    {
        get => _terrain;
        set => _terrain = value;
    }

    /// <summary>Whether entity tracking is enabled.</summary>
    public bool Entities
    {
        get => _entities;
        set => _entities = value;
    }

    /// <summary>Whether inventory/container tracking is enabled.</summary>
    public bool Inventory
    {
        get => _inventory;
        set => _inventory = value;
    }

    /// <summary>Whether the local-player physics engine is enabled. Implies <see cref="Terrain"/>.</summary>
    public bool Physics
    {
        get => _physics;
        set => _physics = value;
    }

    /// <summary>Whether pathfinding is enabled. Implies <see cref="Physics"/> and <see cref="Terrain"/>.</summary>
    public bool Pathfinding
    {
        get => _pathfinding;
        set => _pathfinding = value;
    }

    /// <summary>Resolves the implied dependencies (physics implies terrain; pathfinding implies physics).</summary>
    public ClientFeatures Normalized()
    {
        var copy = new ClientFeatures
        {
            Terrain = Terrain,
            Entities = Entities,
            Inventory = Inventory,
            Physics = Physics,
            Pathfinding = Pathfinding,
        };
        if (copy.Pathfinding)
            copy.Physics = true;

        if (copy.Physics)
            copy.Terrain = true;

        return copy;
    }
}
