using System.Runtime.CompilerServices;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Packet coordinates owned by each tracked entity instance, independent of its interpolated or externally moved position.</summary>
internal sealed class EntityPacketBases
{
    private sealed class Entry(Vec3d position)
    {
        public Vec3d Position { get; set; } = position;
    }

    private readonly ConditionalWeakTable<Entity, Entry> _entries = new();

    public Vec3d Get(Entity entity) => GetEntry(entity).Position;

    public void Set(Entity entity, Vec3d position) => GetEntry(entity).Position = position;

    public void Remove(Entity entity) => _entries.Remove(entity);

    public void Clear() => _entries.Clear();

    private Entry GetEntry(Entity entity) => _entries.GetValue(entity, static value => new Entry(value.Position));
}
