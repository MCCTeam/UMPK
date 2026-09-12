using Brigadier.NET.Tree;

namespace Umpk.Commands;

/// <summary>The registration entry handed to <see cref="ICommandRegistrationScope{TSource}.Register"/>. The command tree root accepts only literal children (matching Brigadier's root), so a registration declares one or more top-level literals and configures each subtree.</summary>
/// <typeparam name="TSource">The command source type.</typeparam>
public sealed class CommandBuilder<TSource>
    where TSource : ICommandSource
{
    private readonly List<LiteralCommandBuilder<TSource>> _roots = [];

    internal CommandBuilder()
    {
    }

    /// <summary>Declares a top-level literal command and configures its subtree.</summary>
    public CommandBuilder<TSource> Literal(string name, Action<LiteralCommandBuilder<TSource>> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var root = new LiteralCommandBuilder<TSource>(name);
        configure(root);
        _roots.Add(root);
        return this;
    }

    internal IReadOnlyList<LiteralCommandNode<TSource>> BuildRoots()
    {
        var resolved = new Dictionary<CommandNodeBuilder<TSource>, CommandNode<TSource>>();
        var result = new List<LiteralCommandNode<TSource>>(_roots.Count);
        foreach (var root in _roots)
            result.Add((LiteralCommandNode<TSource>)root.Build(resolved));

        return result;
    }
}
