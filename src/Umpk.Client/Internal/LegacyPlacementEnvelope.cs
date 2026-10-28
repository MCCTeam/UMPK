using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns a source-bound placement-relevance enclosure. It never publishes an exact body or position.</summary>
internal sealed class LegacyPlacementEnvelope
{
    private Aabb _endpoints;
    private Vec3d _lastEndpoint;
    private bool _known;
    private readonly Func<Entity, bool> _metadataIsKnown;

    public int Protocol { get; }

    public LegacyPlacementEnvelope(Vec3d initial, int protocol, Func<Entity, bool> metadataIsKnown)
    {
        Protocol = protocol;
        _metadataIsKnown = metadataIsKnown;
        _known = Admitted(initial);
        // Native AddMob invokes sm.a(DDDFF), which clamps spawn X/Z before setting its box; packet-coordinate bases retain the original values. Retain both points without altering either runtime position or packet base.
        double nativeX = Math.Clamp(initial.X, -30000000D, 30000000D);
        double nativeZ = Math.Clamp(initial.Z, -30000000D, 30000000D);
        _endpoints = new(Math.Min(initial.X, nativeX), initial.Y, Math.Min(initial.Z, nativeZ),
            Math.Max(initial.X, nativeX), initial.Y, Math.Max(initial.Z, nativeZ));
        _lastEndpoint = initial;
    }

    public void Invalidate() => _known = false;

    public void ObserveEndpoint(Vec3d value, bool independent)
    {
        if (!_known)
            return;
        if (!Admitted(value) || !independent && value != _lastEndpoint)
        {
            Invalidate();
            return;
        }
        _endpoints = new(Math.Min(_endpoints.MinX, value.X), Math.Min(_endpoints.MinY, value.Y), Math.Min(_endpoints.MinZ, value.Z),
            Math.Max(_endpoints.MaxX, value.X), Math.Max(_endpoints.MaxY, value.Y), Math.Max(_endpoints.MaxZ, value.Z));
        _lastEndpoint = value;
    }

    public bool TryGet(Entity entity, out Aabb envelope)
    {
        envelope = default;
        if (!_known)
            return false;
        if (entity.Position != _lastEndpoint || entity.Vehicle is not null || entity.Passengers.Count != 0
            || !_metadataIsKnown(entity))
        {
            Invalidate();
            return false;
        }
        // Each admitted native profile proves its cached body offsets below one block, including retained-minimum age growth. One block also encloses the proved <1/4096 interpolation error for admitted endpoints and native counts zero through three; outward rounding covers these final additions.
        envelope = new(Math.BitDecrement(_endpoints.MinX - 1), Math.BitDecrement(_endpoints.MinY - 1), Math.BitDecrement(_endpoints.MinZ - 1),
            Math.BitIncrement(_endpoints.MaxX + 1), Math.BitIncrement(_endpoints.MaxY + 1), Math.BitIncrement(_endpoints.MaxZ + 1));
        return true;
    }

    private static bool Admitted(Vec3d value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z)
            && Math.Abs(value.X) <= 2147483648D && Math.Abs(value.Y) <= 2147483648D && Math.Abs(value.Z) <= 2147483648D;

}
