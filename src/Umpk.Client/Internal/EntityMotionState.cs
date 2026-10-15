using System.Runtime.CompilerServices;
using Umpk.Client.Actions;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Instance-owned native motion controllers. Observations and ticks run on the session loop.</summary>
internal sealed class EntityMotionState
{
    private readonly ConditionalWeakTable<Entity, VanillaEntityVelocity> _remotePlayers = new();

    public void RegisterRemotePlayer(Entity entity, int protocol)
    {
        RemotePlayerProfile profile = RemotePlayerProfile.ForProtocol(protocol);
        if (!profile.AppliesSpawnVelocity)
            entity.Velocity = Vec3d.Zero;
        if (profile.VelocitySteps == 0)
            return;
        var velocity = new VanillaEntityVelocity(profile.VelocitySteps);
        velocity.SetCurrent(entity.Velocity);
        _remotePlayers.Add(entity, velocity);
    }

    public void ObserveVelocity(Entity entity, Vec3d value)
    {
        if (_remotePlayers.TryGetValue(entity, out VanillaEntityVelocity? velocity))
        {
            velocity.SetCurrent(entity.Velocity);
            velocity.Observe(value);
            entity.Velocity = velocity.Value;
        }
        else
            entity.Velocity = value;
    }

    public void Tick(EntityStore? entities)
    {
        if (entities is null)
            return;
        foreach (Entity entity in entities.All)
        {
            if (!_remotePlayers.TryGetValue(entity, out VanillaEntityVelocity? velocity))
                continue;
            // Native rideTick zeros current movement before invoking the player's tick; the pending lerpMotion target survives.
            velocity.SetCurrent(entity.Vehicle is null ? entity.Velocity : Vec3d.Zero);
            velocity.Tick();
            entity.Velocity = velocity.Value;
        }
    }

    public void Remove(Entity entity) => _remotePlayers.Remove(entity);

    public void Clear() => _remotePlayers.Clear();

    private readonly record struct RemotePlayerProfile(int VelocitySteps, bool AppliesSpawnVelocity)
    {
        // Actual RemotePlayer traces cover all 75 releases; generic spawn starts carrying players in 1.20.2, before Entity.recreateFromPacket applies their velocity in 1.21.5.
        public static RemotePlayerProfile ForProtocol(int protocol) => new(protocol >= 762 ? 3 : 0, protocol >= 770);
    }
}
