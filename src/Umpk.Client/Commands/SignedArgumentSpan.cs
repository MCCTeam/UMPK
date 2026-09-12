namespace Umpk.Client.Commands;

/// <summary>One command argument whose value must be signed for secure chat. Produced by <see cref="ServerCommandTree.GetSignedArguments"/> for a fully-typed command line. Mirrors <c>the corresponding game type</c>: the argument name and the exact substring of the command line it covers, plus that substring's span for the signing payload.</summary>
/// <param name="Name">The argument node name (e.g. <c>message</c>).</param>
/// <param name="Value">The raw argument text as it appears in the command line.</param>
/// <param name="Start">The inclusive start index of the value within the command line.</param>
/// <param name="Length">The length of the value substring.</param>
public readonly record struct SignedArgumentSpan(string Name, string Value, int Start, int Length);
