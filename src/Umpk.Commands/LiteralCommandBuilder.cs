using Brigadier.NET.Tree;
using Umpk.Commands.Internal;

namespace Umpk.Commands;

/// <summary>Builds a literal command node (a fixed keyword such as <c>tp</c> or <c>list</c>).</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
public sealed class LiteralCommandBuilder<TSource> : CommandNodeBuilder<TSource>
    where TSource : ICommandSource
{
    internal LiteralCommandBuilder(string literal)
    {
        Literal = literal;
    }

    /// <summary>The literal keyword this node matches.</summary>
    public string Literal { get; }

    internal override CommandNode<TSource> Build(
        Dictionary<CommandNodeBuilder<TSource>, CommandNode<TSource>> resolved)
    {
        if (resolved.TryGetValue(this, out var existing))
            return existing;

        Brigadier.NET.Command<TSource>? command = Execution is { } exec
            ? context => ExecutionBridge<TSource>.Invoke(exec, context)
            : null;

        CommandNode<TSource>? redirect = RedirectTargetBuilder?.Build(resolved);

        var node = new LiteralCommandNode<TSource>(
            Literal,
            command,
            RequirementPredicate ?? (_ => true),
            redirect,
            modifier: null,
            forks: false);

        resolved[this] = node;
        ApplyChildren(node, resolved);
        return node;
    }
}
