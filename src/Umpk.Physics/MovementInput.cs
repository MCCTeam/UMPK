using System.Runtime.CompilerServices;

namespace Umpk.Physics;

/// <summary>The per-tick movement intent pushed into <see cref="PlayerPhysics.Step"/>, mirroring vanilla <c>ClientInput</c>/<c>KeyboardInput</c>. A value type: the host builds one per tick from action APIs, pathfinding execution, or plugin input. <see cref="AutoJump"/> is the auto-jump toggle, off by default to match headless-client server-visible behavior.</summary>
public readonly record struct MovementInput
{
    /// <summary>Forward movement (W).</summary>
    public bool Forward { get; init; }

    /// <summary>Backward movement (S).</summary>
    public bool Back { get; init; }

    /// <summary>Strafe left (A).</summary>
    public bool Left { get; init; }

    /// <summary>Strafe right (D).</summary>
    public bool Right { get; init; }

    /// <summary>Jump / ascend intent.</summary>
    public bool Jump { get; init; }

    /// <summary>Sneak / descend intent.</summary>
    public bool Sneak { get; init; }

    /// <summary>Sprint intent.</summary>
    public bool Sprint { get; init; }

    /// <summary>Auto-jump enabled; off by default.</summary>
    public bool AutoJump { get; init; }

    /// <summary>An input with nothing pressed.</summary>
    public static MovementInput None => default;

    /// <summary>The raw input vector <c>(xxa, zza)</c> before yaw rotation, normalized if its magnitude exceeds 1. Forward = +zza, Back = -zza, Left = +xxa, Right = -xxa (vanilla convention).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (float Xxa, float Zza) GetMoveVector()
    {
        float xxa = 0;
        float zza = 0;

        if (Forward)
            zza += 1.0f;

        if (Back)
            zza -= 1.0f;

        if (Left)
            xxa += 1.0f;

        if (Right)
            xxa -= 1.0f;

        float lenSqr = xxa * xxa + zza * zza;
        if (lenSqr > 1.0f)
        {
            float len = MathF.Sqrt(lenSqr);
            xxa /= len;
            zza /= len;
        }

        return (xxa, zza);
    }
}
