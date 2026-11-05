using Umpk.Client.Actions;
using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Internal;

/// <summary>The real local player's legacy callbacks and cached dimensions. The physics holder owns movement and velocity; this owner never inserts self into EntityStore.</summary>
internal sealed class TrackedLegacySelfGeometry
{
    private readonly ClientState _state;
    private readonly int _protocol;
    private readonly LegacyPlayerGeometryProfile _profile;
    private readonly TrackedLegacyPlayerGeometry _tracked;
    private World? _world;
    private LegacyPlayerResizeClearance? _clearance;
    private byte _sharedFlags;
    private bool _spinAttack;
    private bool _metadataKnown = true;
    private bool _requestedSneak;
    private float _eyeHeight = 1.62F;

    public TrackedLegacySelfGeometry(ClientState state, int protocol)
    {
        _state = state;
        _protocol = protocol;
        _profile = LegacyPlayerGeometryProfile.ForProtocol(protocol);
        _tracked = new(protocol, state.Self.Position);
    }

    public bool IsKnown => _tracked.TryGet(out _);
    public bool Sleeping => _tracked.Geometry.Sleeping;
    public bool IsSneaking => _requestedSneak && !Sleeping;
    public bool BlocksInput => Sleeping || _state.Self.Health <= 0F;
    public float EyeHeight => _eyeHeight;

    public bool TryGet(out Aabb bounds, out PhysicsBodyDimensions dimensions)
    {
        bool known = _tracked.TryGet(out bounds);
        if (known)
            RefreshEye();
        dimensions = new(_tracked.Geometry.Width, _tracked.Geometry.Height, _eyeHeight);
        return known;
    }

    public void ObserveInput(bool sneaking)
    {
        _requestedSneak = sneaking;
        FeedFlags();
        if (IsKnown)
            RefreshEye();
    }

    public void ObserveMetadata(EntityMetadataList metadata)
    {
        foreach (EntityDataEntry entry in metadata.Entries)
        {
            if (entry.Index == 0)
            {
                if (entry.Value.Kind != MetadataValueKind.Byte)
                    _metadataKnown = false;
                else
                    _sharedFlags = unchecked((byte)entry.Value.AsByte());
            }
            else if (_profile.SwimmingAndSpin && entry.Index == 6)
            {
                if (entry.Value.Kind != MetadataValueKind.Byte)
                    _metadataKnown = false;
                else
                    _spinAttack = (entry.Value.AsByte() & 4) != 0;
            }
        }
        if (!_metadataKnown)
            _tracked.InvalidateMetadata();
        FeedFlags();
        if (IsKnown)
            RefreshEye();
    }

    public void ObservePosition(Vec3d position) => _tracked.ObservePosition(position);

    public void ObserveMovement(in PhysicsState state) => _tracked.ObserveMovement(state.Position, state.BoundingBox);

    public void BeginSleep(IBlockInteractionSource source, BlockPos bed)
    {
        FeedFlags();
        if (_tracked.BeginSleep(_state.WorldOrNull, source, bed, _state.EntityMembership, _state.EntityGeometry.EmptyChunkFor(_protocol)))
        {
            _state.Self.Position = _tracked.Geometry.Position;
            _state.Self.Velocity = Vec3d.Zero;
        }
        FeedFlags();
        if (IsKnown)
            RefreshEye();
    }

    public void Wake(IBlockInteractionSource source)
    {
        _tracked.Wake(_state.WorldOrNull, source, _state.EntityMembership, _state.EntityGeometry.EmptyChunkFor(_protocol));
        if (_tracked.IsPositionKnown)
            _state.Self.Position = _tracked.Geometry.Position;
        FeedFlags();
        if (IsKnown)
            RefreshEye();
    }

    public void HandleDeath()
    {
        _tracked.HandleDeath();
        SelfState self = _state.Self;
        self.Health = 0F;
        // The original LocalPlayer damage override performs no local damage calculation. These listener paths retain the constructor/reset damage yaw of zero.
        self.Velocity = _profile.DeathVelocity(self.Yaw, damageYaw: 0F);
        if (_profile.ClearsFireOnDeath)
            _sharedFlags &= 0xFE;
        FeedFlags();
        if (IsKnown)
            RefreshEye();
    }

    public void TickSize(IBlockShapeSource shapes)
    {
        World? world = _state.WorldOrNull;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _clearance = world is not null && _profile.HasSizePhase ? new LegacyPlayerResizeClearance(world, shapes, _protocol, _state.EntityMembership) : null;
        }
        FeedFlags();
        _tracked.Tick(_clearance);
        if (IsKnown)
            RefreshEye();
    }

    private void FeedFlags()
    {
        // Original LocalPlayer.isSneaking reads local input and suppresses it while sleeping; synchronized peer flag2 does not own this local decision.
        byte flags = (byte)(_sharedFlags & ~2);
        if (IsSneaking)
            flags |= 2;
        _tracked.ObserveFlags(flags, _spinAttack);
    }

    private void RefreshEye()
    {
        float eye = 1.62F;
        if (Sleeping)
            eye = .2F;
        else if (!_profile.HasSizePhase)
        {
            if (IsSneaking)
                eye -= .08F;
        }
        else
        {
            bool crouch = IsSneaking || _tracked.Geometry.Height == 1.65F;
            bool horizontal = (_sharedFlags & 128) != 0 || _tracked.Geometry.Height == .6F || _profile.SwimmingAndSpin && _spinAttack;
            if (_profile.SwimmingAndSpin && horizontal)
                eye = .4F;
            else if (crouch)
                eye -= .08F;
            else if (horizontal)
                eye = .4F;
        }
        _eyeHeight = eye;
    }
}
