using Brigadier.NET.Tree;

namespace Umpk.Commands;

/// <summary>Base for the literal and argument node builders. Carries the shared node configuration (children, executes handler, requires predicate, redirect) and produces the underlying node when the dispatcher compiles a scope. The produced node type is never exposed.</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
public abstract class CommandNodeBuilder<TSource>
    where TSource : ICommandSource
{
    private protected CommandNodeBuilder()
    {
    }

    private protected List<CommandNodeBuilder<TSource>> ChildBuilders { get; } = [];

    private protected CommandExecution<TSource>? Execution { get; private set; }

    private protected Predicate<TSource>? RequirementPredicate { get; private set; }

    private protected CommandNodeBuilder<TSource>? RedirectTargetBuilder { get; private set; }

    /// <summary>Sets the command body invoked when parsing ends on this node.</summary>
    public CommandNodeBuilder<TSource> Executes(CommandExecution<TSource> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        Execution = execution;
        return this;
    }

    /// <summary>Sets a synchronous command body. <see cref="Internal.ExecutionBridge{TSource}"/> already treats every body as synchronous first, taking a completed <see cref="ValueTask{TResult}"/>'s result without awaiting; this overload skips the wrapping that synchronous command bodies would otherwise write by hand.</summary>
    public CommandNodeBuilder<TSource> Executes(Func<ICommandContext<TSource>, int> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        Execution = ctx => new ValueTask<int>(execution(ctx));
        return this;
    }

    /// <summary>Restricts this node to sources satisfying the predicate.</summary>
    public CommandNodeBuilder<TSource> Requires(Predicate<TSource> requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        RequirementPredicate = requirement;
        return this;
    }

    /// <summary>Adds a literal child node and configures it through <paramref name="configure"/>.</summary>
    public CommandNodeBuilder<TSource> ThenLiteral(string name, Action<CommandNodeBuilder<TSource>> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var child = new LiteralCommandBuilder<TSource>(name);
        configure(child);
        ChildBuilders.Add(child);
        return this;
    }

    /// <summary>Adds an argument child node of the given type and configures it.</summary>
    public CommandNodeBuilder<TSource> ThenArgument<T>(
        string name,
        IArgumentType<T> type,
        Action<ArgumentCommandBuilder<TSource, T>> configure)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(configure);
        var child = new ArgumentCommandBuilder<TSource, T>(name, type);
        configure(child);
        ChildBuilders.Add(child);
        return this;
    }

    /// <summary>Redirects parsing that reaches this node to the target node's subtree.</summary>
    public CommandNodeBuilder<TSource> RedirectTo(CommandNodeBuilder<TSource> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RedirectTargetBuilder = target;
        return this;
    }

    internal abstract CommandNode<TSource> Build(Dictionary<CommandNodeBuilder<TSource>, CommandNode<TSource>> resolved);

    private protected void ApplyChildren(
        CommandNode<TSource> node,
        Dictionary<CommandNodeBuilder<TSource>, CommandNode<TSource>> resolved)
    {
        foreach (var childBuilder in ChildBuilders)
            node.AddChild(childBuilder.Build(resolved));

    }
}
