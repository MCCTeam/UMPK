using Umpk.Client.State;
using Umpk.Game.Players;
using Umpk.Geometry;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of <see cref="SelfState"/>: identity, kinematics, vitals, abilities-adjacent facts (held slot, sneaking/sprinting), and game mode. Field-for-field with the tracker it projects, so a field added to <see cref="SelfState"/> and not to this record is a missing line in <see cref="Project"/>, not a silent gap.</summary>
/// <param name="EntityId">The local player's entity id, assigned at join.</param>
/// <param name="Uuid">The local player's uuid.</param>
/// <param name="Username">The local player's username.</param>
/// <param name="Position">The player's feet position. The tracker's DEFAULT (the origin) until <paramref name="HasSpawned"/> is true; see that parameter.</param>
/// <param name="Velocity">The player's velocity.</param>
/// <param name="Yaw">Yaw in degrees.</param>
/// <param name="Pitch">Pitch in degrees.</param>
/// <param name="OnGround">Whether the player is on the ground per the last physics/move step.</param>
/// <param name="Health">Health (0-20 by default).</param>
/// <param name="Food">Food level.</param>
/// <param name="Saturation">Food saturation.</param>
/// <param name="AirSupply">Remaining breath in ticks; counts down from <paramref name="MaxAirSupply"/> underwater and refills at four per tick out of it. Negative values are vanilla's drowning window.</param>
/// <param name="MaxAirSupply">A full lung, in ticks (300 on every version).</param>
/// <param name="ExperienceLevel">Experience level.</param>
/// <param name="ExperienceProgress">Experience bar progress from 0 to 1.</param>
/// <param name="TotalExperience">Total experience.</param>
/// <param name="HeldSlot">The selected hotbar slot, from 0 to 8.</param>
/// <param name="GameMode">The current game mode. The tracker's DEFAULT (<see cref="Umpk.Game.Players.GameMode.Survival"/>) until <paramref name="HasSpawned"/> is true; see that parameter.</param>
/// <param name="Sneaking">Whether the player is currently sneaking (client-driven input state).</param>
/// <param name="Sprinting">Whether the player is currently sprinting.</param>
/// <param name="HasSpawned">Whether the server has actually placed the player in the world for this session (the join packet applied and the initial position teleport received). FALSE means <paramref name="Position"/> and <paramref name="GameMode"/> are the tracker's DEFAULTS, not readings: the client cannot know either until the server says so, and a host that reports them anyway is publishing numbers it made up. Await <see cref="UmpkClient.Spawned"/> first.</param>
public sealed record SelfSnapshot(
    int EntityId,
    Guid Uuid,
    string Username,
    Vec3d Position,
    Vec3d Velocity,
    float Yaw,
    float Pitch,
    bool OnGround,
    float Health,
    int Food,
    float Saturation,
    int AirSupply,
    int MaxAirSupply,
    int ExperienceLevel,
    float ExperienceProgress,
    int TotalExperience,
    int HeldSlot,
    GameMode GameMode,
    bool Sneaking,
    bool Sprinting,
    bool HasSpawned)
{
    /// <summary>The eye position for a player standing at <see cref="Position"/> (feet + 1.62).</summary>
    public Vec3d EyePosition => PlayerReach.EyePosition(Position);

    /// <summary>Whether <see cref="GameMode"/> is spectator.</summary>
    public bool IsSpectator => GameMode == GameMode.Spectator;

    /// <summary>Projects a <see cref="SelfSnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static SelfSnapshot Project(ClientState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SelfState self = state.Self;
        return new SelfSnapshot(
            self.EntityId,
            self.Uuid,
            self.Username,
            self.Position,
            self.Velocity,
            self.Yaw,
            self.Pitch,
            self.OnGround,
            self.Health,
            self.Food,
            self.Saturation,
            self.AirSupply,
            self.MaxAirSupply,
            self.ExperienceLevel,
            self.ExperienceProgress,
            self.TotalExperience,
            self.HeldSlot,
            self.GameMode,
            self.Sneaking,
            self.Sprinting,
            self.HasSpawned);
    }
}
