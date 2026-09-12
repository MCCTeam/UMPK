---
title: Umpk.Text
description: Minecraft chat components: the tree model, styles, click and hover events, and JSON, NBT and legacy serializers.
sidebar:
  order: 4
---

`Umpk.Text` is the chat component model and its serializers. A component is a content node plus a style plus children, which is exactly vanilla's shape, and this package can read and write that tree as JSON, as NBT, or as a section-sign colour-coded string.

The tree itself carries no version information. Only the serializers know that click and hover events changed shape at 1.21.5, and they take that as an explicit parameter rather than sniffing a protocol number.

## Its place in the stack

`Umpk.Text` depends on [Umpk.Core](/packages/umpk-core) and [Umpk.Nbt](/packages/umpk-nbt). [Umpk.Game](/packages/umpk-game), [Umpk.Protocol.Java](/packages/umpk-protocol-java) and [Umpk.Commands](/packages/umpk-commands) all build on it. Chat, item names and lore, container titles, kick messages, scoreboard display names and command error text are all `Component` values.

## Main entry points

`Component` holds a `ComponentContent`, a `Style` and an `IReadOnlyList<Component>` of children. `Component.Text(string)` and `Component.Translatable(key, params args)` are the two shorthands you will reach for; `Component.Empty` is the shared empty one. `ToPlainText` flattens the tree the way vanilla's `Component.getString()` does and drops all styling.

`ComponentContent` is an abstract record with six subclasses: `TextContent`, `TranslatableContent`, `KeybindContent`, `ScoreContent`, `SelectorContent` and `NbtContent`. `IComponentContentVisitor<TResult>` gives you an exhaustive dispatch over all six through `ComponentContent.Accept`.

`Style` is a record with `Color`, `Bold`, `Italic`, `Underlined`, `Strikethrough`, `Obfuscated`, `Font`, `Insertion`, `ClickEvent` and `HoverEvent`. `Style.Empty` is the neutral one, `IsEmpty` tests for it, and `ApplyTo(parent)` resolves this style against an inherited one.

`TextColor` is a struct covering both the sixteen named colours (`TextColor.Gold`, `TextColor.Aqua`, and so on) and arbitrary RGB through `TextColor.FromRgb`. `Parse` accepts either form, `FromName` only the named one, and `Serialize` gives you the wire string back.

`ClickEvent` and the `HoverEvent` family (`HoverShowText`, `HoverShowItem`, `HoverShowEntity`) model the interactive parts. `ClickEventAction` covers `OpenUrl`, `OpenFile`, `RunCommand`, `SuggestCommand`, `ChangePage`, `CopyToClipboard`, `ShowDialog` and `Custom`.

`Umpk.Text.Serialization` has the three serializers. `ComponentJson` parses and writes JSON, from either a `string` or a UTF-8 span. `ComponentNbt` converts to and from `NbtTag`, which is what 1.20.3+ uses on the wire. `LegacyText` parses and emits section-sign codes, with `LegacyText.Prefix` as the section sign itself. Bad input raises `ComponentFormatException`.

## Example

```csharp
using Umpk.Text;
using Umpk.Text.Serialization;

var message = new Component(
    new TextContent("Welcome, "),
    new Style { Color = TextColor.Gray },
    [
        new Component(
            new TextContent("Steve"),
            new Style
            {
                Color = TextColor.FromRgb(0x55FFFF),
                Bold = true,
                ClickEvent = new ClickEvent(ClickEventAction.SuggestCommand, "/msg Steve "),
                HoverEvent = new HoverShowText(Component.Text("Click to message Steve")),
            }),
    ]);

Console.WriteLine(message.ToPlainText());   // Welcome, Steve

// 1.21.5 and later.
string modern = ComponentJson.ToJsonString(message, ComponentWireEra.Modern);

// 1.21.4 and earlier.
string legacy = ComponentJson.ToJsonString(message, ComponentWireEra.Legacy);

// A server MOTD is a bare string on old servers and a component tree on new ones.
// ComponentJson.Parse reads both shapes.
Component motd = ComponentJson.Parse("\"A Minecraft Server\"");

// Section-sign text, the pre-1.7 form that plugins still emit.
Component coloured = LegacyText.Parse("§cRed §lbold");
```

## Things that catch people out

`ComponentWireEra` is the whole era story, and it is a real fork, not a cosmetic one. Under `Legacy` (pre-1.21.5) a click event is a flat `action` and `value` pair, and a hover event nests its payload under `contents`. Under `Modern` (1.21.5+) a click event dispatches on `action` with a per-action field (`url`, `command`, `page`), and a hover event inlines its payload next to `action`. `show_entity` even renames its fields between the two. Serializing with the wrong era produces JSON a server will reject or misread.

The default for `ComponentJson.ToJsonString` and `ComponentNbt.To` is `ComponentWireEra.Modern`. If you are talking to anything older than 1.21.5, pass the era explicitly.

`ComponentJsonLiteralForm` decides whether a plain text component with no style collapses to a bare JSON string. The default, `Collapsed`, produces `"hi"`. `Object` produces `{"text":"hi"}`. Vanilla accepts both; some proxies and plugins parse only one.

Style booleans are `bool?`, not `bool`. That is a three-state property: null means "inherit from the parent", `false` means "explicitly off". Setting `Bold = false` on a child of a bold parent is not the same as leaving it null, and flattening the two into `bool` loses information the wire carries.

`ToPlainText` with no `ITranslationSource` does not fail on a translatable component. It falls back to the component's own `Fallback` string, or to the raw translation key. That is usually what you want for logging, and it is a trap if you assumed you would get English. Pass an `ITranslationSource` when the output is for a human.

Translation argument substitution handles `%s` and the positional `%1$s` form, and an unsupported conversion is emitted verbatim rather than throwing. Vanilla throws there. This package is deliberately more lenient, because a malformed server message should not take down a decode.

`TextColor` equality is by value, so `TextColor.Gold` and `TextColor.FromRgb(0xFFAA00)` are two different values even though they render identically. `IsNamed` and `Name` tell you which kind you have.
