namespace Umpk.Commands;

/// <summary>One line of a command's smart-usage listing: the top-level command <paramref name="Name"/> paired with its collapsed usage rendering. See <see cref="CommandService{TSource}.GetUsage(TSource)"/>.</summary>
/// <param name="Name">The top-level command's own name.</param>
/// <param name="Usage">The collapsed usage text for the command's whole subtree.</param>
public readonly record struct CommandUsage(string Name, string Usage);
