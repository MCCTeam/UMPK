using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>The lifecycle state of an action template.</summary>
public enum TemplateState
{
    /// <summary>The segment is still executing.</summary>
    InProgress,

    /// <summary>The segment completed successfully.</summary>
    Complete,

    /// <summary>The segment failed and the driver should replan.</summary>
    Failed,
}

/// <summary>The per-tick movement controller for one path segment. Reads the current <see cref="PhysicsState"/> snapshot and emits a <see cref="TemplateOutput"/> (input + look angles + expected next state); it never mutates a live engine or a shared input object. World queries use the immutable <see cref="PlanningWorldView"/> the template was constructed with.</summary>
public interface IActionTemplate
{
    /// <summary>The block position the segment starts at.</summary>
    Vec3d ExpectedStart { get; }

    /// <summary>The block position the segment ends at.</summary>
    Vec3d ExpectedEnd { get; }

    /// <summary>Advances the segment one tick and reports its lifecycle state and the emitted output.</summary>
    TemplateState Tick(in PhysicsState physics, out TemplateOutput output);
}
