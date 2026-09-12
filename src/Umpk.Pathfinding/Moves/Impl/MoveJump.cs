using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Moves.Impl;

/// <summary>The unified jump-family move. A single <see cref="IMove"/> dispatching on <see cref="JumpFlavor"/> to cover walk, step, sprint-jump, and sidewall variants. All feasibility and cost logic lives in <see cref="JumpFeasibility"/>.</summary>
public sealed class MoveJump : IMove
{
    /// <summary>The descriptor this move evaluates.</summary>
    public JumpDescriptor Descriptor { get; }

    /// <inheritdoc/>
    public MoveType Type { get; }

    /// <inheritdoc/>
    public int XOffset => Descriptor.XOffset;

    /// <inheritdoc/>
    public int ZOffset => Descriptor.ZOffset;

    /// <summary>The vertical delta of the move.</summary>
    public int YDelta => Descriptor.YDelta;

    /// <summary>The jump flavor.</summary>
    public JumpFlavor Flavor => Descriptor.Flavor;

    /// <inheritdoc/>
    public bool DynamicY => false;

    /// <summary>Creates a jump move from a descriptor.</summary>
    public MoveJump(JumpDescriptor descriptor)
    {
        Descriptor = descriptor;
        Type = DeriveMoveType(descriptor);
    }

    /// <inheritdoc/>
    public void Calculate(CalculationContext ctx, int x, int y, int z, ref MoveResult result)
        => JumpFeasibility.Evaluate(ctx, x, y, z, Descriptor, ref result);

    /// <summary>A same-Y cardinal walk.</summary>
    public static MoveJump Traverse(int dx, int dz) => new(new JumpDescriptor(dx, dz, 0, JumpFlavor.Walk));

    /// <summary>A same-Y diagonal walk.</summary>
    public static MoveJump Diagonal(int dx, int dz) => new(new JumpDescriptor(dx, dz, 0, JumpFlavor.Walk));

    /// <summary>A one-block ascend step.</summary>
    public static MoveJump Ascend(int dx, int dz) => new(new JumpDescriptor(dx, dz, 1, JumpFlavor.Step));

    /// <summary>A diagonal one-block ascend step.</summary>
    public static MoveJump DiagonalAscend(int dx, int dz) => new(new JumpDescriptor(dx, dz, 1, JumpFlavor.Step));

    /// <summary>A diagonal one-block descend step.</summary>
    public static MoveJump DiagonalDescend(int dx, int dz) => new(new JumpDescriptor(dx, dz, -1, JumpFlavor.Step));

    /// <summary>A sprint jump across a gap.</summary>
    public static MoveJump Parkour(int dx, int dz, int yDelta = 0) => new(new JumpDescriptor(dx, dz, yDelta, JumpFlavor.SprintJump));

    /// <summary>A sidewall sprint jump.</summary>
    public static MoveJump Sidewall(int dx, int dz, int yDelta = 0) => new(new JumpDescriptor(dx, dz, yDelta, JumpFlavor.Sidewall));

    private static MoveType DeriveMoveType(JumpDescriptor d)
    {
        return d.Flavor switch
        {
            JumpFlavor.Walk => d.IsCardinal ? MoveType.Traverse : MoveType.Diagonal,
            JumpFlavor.Step => d.YDelta > 0 ? MoveType.Ascend : MoveType.Descend,
            JumpFlavor.SprintJump => MoveType.Parkour,
            JumpFlavor.Sidewall => MoveType.Parkour,
            _ => MoveType.Traverse,
        };
    }
}
