using Umpk.Physics;

namespace Umpk.Pathfinding.Execution;

/// <summary>What a template emits for one tick: the desired <see cref="MovementInput"/> plus the target look angles, and the expected <see cref="PhysicsState"/> the driver anticipates after applying that input. Templates never touch the live engine. The client/navigator layer owns the engine handoff: it reads this per tick, sets rotation, pushes the input, and can compare the real post-step state against <see cref="ExpectedState"/> for drift detection.</summary>
public readonly record struct TemplateOutput
{
    /// <summary>The movement input to push into the physics engine this tick.</summary>
    public MovementInput Input { get; init; }

    /// <summary>The desired facing yaw in degrees.</summary>
    public float TargetYaw { get; init; }

    /// <summary>The desired facing pitch in degrees.</summary>
    public float TargetPitch { get; init; }

    /// <summary>The state the driver expects after applying <see cref="Input"/> and rotation (a one-tick forward simulation over the planning view), or <c>null</c> when the template did not compute one.</summary>
    public PhysicsState? ExpectedState { get; init; }

    /// <summary>Builds a template output and computes its one-tick expected state by forward-simulating <paramref name="input"/> from <paramref name="physics"/> (with the emitted look angles applied) over the execution context's frozen world. The expected state comes from <see cref="PhysicsSimulator"/>, never from poking a live engine.</summary>
    /// <remarks>This is also where <see cref="PathExecutionContext.AllowSprint"/> is enforced. Every template that emits an input builds it here (the only other <c>TemplateOutput</c> constructions are <c>PathExecutor</c>'s three idle/abort outputs, all <see cref="MovementInput.None"/>), so stripping the bit once at this seam covers the live input and the expected state it is compared against in the same place. Teaching each template to consult the flag risks leaving a future template without it.</remarks>
    internal static TemplateOutput From(
        MovementInput input, float yaw, float pitch, PathExecutionContext ctx, in PhysicsState physics)
    {
        if (input.Sprint && !ctx.AllowSprint)
            input = input with { Sprint = false };

        PhysicsState start = physics with { Yaw = yaw, Pitch = pitch };
        PhysicsState expected = PhysicsSimulator.Run(start, ctx.Conditions, ctx.World, ctx.Profile, [input]);
        return new TemplateOutput
        {
            Input = input,
            TargetYaw = yaw,
            TargetPitch = pitch,
            ExpectedState = expected,
        };
    }
}
