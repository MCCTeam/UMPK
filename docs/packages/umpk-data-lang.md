---
title: "Umpk.Data.Lang"
description: "Protocol-specific vanilla English translation tables for rendering translatable chat components."
sidebar:
  order: 7
---

`Umpk.Data.Lang` supplies the vanilla `en_us` translation table for each of UMPK's 49 supported protocols. It depends only on [Umpk.Text](umpk-text.md). You can use it without the network, game model, or protocol packages.

Use the table for the protocol that produced the component. Translation templates change between releases, including their argument counts, so the newest table is not a safe replacement for an older one.

```csharp
using Umpk.Data.Lang;
using Umpk.Text;

ITranslationSource translations = VanillaTranslations.ForProtocol(770);
Component component = Component.Translatable(
    "chat.type.text", Component.Text("Alex"), Component.Text("Hello"));
string text = component.ToPlainText(translations);
```

`ForProtocol` creates a table on first use and caches it. An unsupported protocol returns a source that resolves no keys, which lets `Umpk.Text` use the component fallback or the untranslated key. `Latest` returns the table for the newest supported protocol. `Protocols` lists all available tables, and `CountFor` reports a table's entry count without materializing it.

DataGen emits the packed translation pool and protocol indexes from `data/java/<protocol>/lang.json`. Do not edit the two `.g.cs` files by hand. Use the three-output generation command in [the dataset](../concepts/the-dataset.md) and confirm that Git reports no generated diff.
