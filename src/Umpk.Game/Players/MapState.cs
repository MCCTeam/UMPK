using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Players;

/// <summary>The per-session filled-map state: the set of known maps keyed by map id. The client-package map handler creates a <see cref="MapData"/> on first receipt and applies subsequent updates to it.</summary>
public sealed class MapState
{
    private readonly Dictionary<int, MapData> _maps = [];

    /// <summary>The known maps.</summary>
    public IReadOnlyCollection<MapData> Maps => _maps.Values;

    /// <summary>The number of known maps.</summary>
    public int Count => _maps.Count;

    /// <summary>Gets the existing map for <paramref name="mapId"/> or creates and tracks a new one.</summary>
    public MapData GetOrCreate(int mapId)
    {
        if (_maps.TryGetValue(mapId, out MapData? map))
            return map;

        map = new MapData(mapId);
        _maps[mapId] = map;
        return map;
    }

    /// <summary>Gets the map for a map id.</summary>
    public bool TryGet(int mapId, [NotNullWhen(true)] out MapData? map) => _maps.TryGetValue(mapId, out map);

    /// <summary>Removes the map for a map id; returns true when present.</summary>
    public bool Remove(int mapId) => _maps.Remove(mapId);

    /// <summary>Removes every map.</summary>
    public void Clear() => _maps.Clear();
}
