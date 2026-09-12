---
title: "Umpk.Commands"
description: "A Brigadier-backed command tree with scoped registration, typed arguments, requirements and completions."
sidebar:
  order: 12
---

`Umpk.Commands` is a command dispatcher for commands your own program owns: the ones a bot exposes to its operator, or a plugin registers for other plugins to call. It wraps `Brigadier.NET` behind a small UMPK-shaped API, with scoped registration so a plugin can add commands and take them away again.

To be clear about what it is not: this does not send commands to a Minecraft server. That is `client.Actions.Chat.SendCommandAsync`, and the server's own command tree is `Umpk.Client.Commands.ServerCommandTree`. `Umpk.Commands` is the local side.

## Its place in the stack

`Umpk.Commands` depends on [Umpk.Core](umpk-core.md) and [Umpk.Text](umpk-text.md), and on the `Brigadier.NET` package. [Umpk.Client](umpk-client.md) uses it: `client.Commands` is a `CommandService<ClientCommandSource>`, and a plugin gets its own registration scope through `ClientPluginContext.Commands`.

Brigadier is wrapped, not exposed. No public member of this package mentions a Brigadier type, and there is a test that fails the build if one ever does. That is deliberate: the dispatcher underneath is a pre-release dependency, and swapping it must not break consumers.

## Main entry points

`CommandService<TSource>` is the dispatcher, generic over whatever your commands run against. `ExecuteAsync(input, source)` runs a command line and returns a `CommandResult`. `CompleteAsync(input, cursor, source)` returns a `CompletionResult`.

`CreateScope(ownerId)` returns an `ICommandRegistrationScope<TSource>`. Register through the scope, and dispose the scope to remove everything it registered.

`CommandBuilder<TSource>.Literal(name, configure)` starts a command. `CommandNodeBuilder<TSource>` builds the tree: `ThenLiteral`, `ThenArgument`, `Executes`, `Requires` and `RedirectTo`. An `Executes` body is a `CommandExecution<TSource>`, returning `ValueTask<int>`.

`Arguments` holds the argument types: `Bool`, `Integer`, `Long`, `Float`, `Double` (each with an optional min and max), `Word`, `QuotableString`, `GreedyString` and `Identifier`. They produce `IArgumentType<T>`, and `ICommandContext<TSource>.GetArgument<T>(name)` reads the parsed value back.

`ArgumentCommandBuilder<TSource, T>.Suggests` attaches an `ArgumentSuggestionProvider<TSource>`, which writes into an `ISuggestionSink` (`Suggest(text)` or `Suggest(text, tooltip)`, with `Remaining` holding the already-typed text of the token being completed). Candidates replace the whole token; the dispatcher computes the range.

`ICommandSource` is an optional interface for your source type: `ReplyAsync(Component)` and `GetService<T>()`.

## Example

Adapted from `tests/Umpk.Commands.Tests`.

```csharp
using Umpk.Commands;

var service = new CommandService<MySource>();
using ICommandRegistrationScope<MySource> scope = service.CreateScope("my-plugin");

scope.Register(b => b.Literal("warp", root =>
{
    root.ThenArgument("name", Arguments.Word(), name =>
    {
        name.Suggests((context, sink) =>
        {
            foreach (string known in KnownWarps)
            {
                sink.Suggest(known);
            }

            return ValueTask.CompletedTask;
        });

        name.Executes(context =>
        {
            string warp = context.GetArgument<string>("name");
            Console.WriteLine($"Warping to {warp}.");
            return new ValueTask<int>(1);
        });
    });

    root.ThenLiteral("list", list => list
        .Requires(source => source.IsOperator)
        .Executes(_ => new ValueTask<int>(KnownWarps.Count)));
}));

CommandResult result = await service.ExecuteAsync("warp spawn", new MySource());
if (!result.Success)
{
    Console.WriteLine($"{result.ErrorMessage} at {result.ErrorPosition}");
}

CompletionResult completions = await service.CompleteAsync("warp sp", cursor: 7, new MySource());
foreach (CompletionSuggestion suggestion in completions.Suggestions)
{
    Console.WriteLine(suggestion.Text);
}
```

## Things that catch people out

Registration is scope-owned, and disposing a scope really does remove its commands. Two scopes can each register commands into the same service; dispose one and only its literals disappear, including any redirects it set up. That is the whole reason scopes exist, and it is why a plugin must hold its scope for as long as it wants its commands to work.

`Arguments` covers primitives and `Identifier`, and nothing else. There is no entity selector, no block position, no item predicate. If you want a Minecraft-shaped argument, build it out of a `Word` or a `GreedyString` and parse it yourself. Do not look for `Arguments.BlockPos`. It does not exist.

An `Executes` body returns `ValueTask<int>`, which is Brigadier's result code, not a success flag. Return a positive number. A command that "worked" and returned 0 will still report `Success = true` through `CommandResult`, but the integer is what a redirect or a chained caller sees.

`Requires` is a `Predicate<TSource>` evaluated during parsing, so a node the source does not meet is not merely unexecutable, it is invisible to completion as well. That is the vanilla behavior and it is usually what you want, but it means a failing requirement shows up as "unknown command", not "no permission". Say so in your own error text if that distinction matters.

`CommandResult.ErrorPosition` is a character index into the input, and it is `-1` when there is no position. Do not use it as an offset without checking.

`CompletionResult` carries `RangeStart` and `RangeEnd`, which is the span of the input the suggestions replace. Rendering a suggestion means splicing it into that range, not appending it to the cursor.

`CommandService<TSource>` rebuilds its merged tree on every `Register` and on every scope disposal. Registering a thousand commands one call at a time is a thousand rebuilds. Batch the registrations into one `Register` call.
