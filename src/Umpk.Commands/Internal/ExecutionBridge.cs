using Brig = Brigadier.NET.Context;

namespace Umpk.Commands.Internal;

/// <summary>Bridges UMPK's async <see cref="CommandExecution{TSource}"/> onto Brigadier's synchronous <c>Command&lt;TSource&gt;</c> delegate.</summary>
/// <remarks>Brigadier's parse/execute engine is synchronous by design. A command body is invoked through this shim: when the returned <see cref="ValueTask{Int32}"/> is already completed (the common case, since bodies queue replies rather than await network I/O), its result is taken without blocking. When it is not, the shim awaits it synchronously. Library code runs without a synchronization context, so no deadlock is possible; the wait is bounded to the body's own continuation. This is the single point where async meets Brigadier's sync contract, isolated so the public surface stays fully async.</remarks>
internal static class ExecutionBridge<TSource>
    where TSource : ICommandSource
{
    internal static int Invoke(CommandExecution<TSource> execution, Brig.CommandContext<TSource> context)
    {
        var wrapped = new BrigadierCommandContext<TSource>(context);
        ValueTask<int> task = execution(wrapped);
        if (task.IsCompletedSuccessfully)
            return task.Result;

        return task.AsTask().GetAwaiter().GetResult();
    }
}
