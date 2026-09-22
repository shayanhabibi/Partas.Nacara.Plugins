---
title: Plugin style layers
order: 4
---

A plugin that renders markup usually needs CSS for it. `:::step` above is a `<li>`
with a class on it and means nothing without rules to match.

`Partas.Nacara.Theme.Contracts` is the package both sides agree on. It holds two
small things and no theme, so a plugin can offer styles without depending on the
theme that renders them.

```bash frame="terminal"
dotnet add package Partas.Nacara.Theme.Contracts
```

## Offering a layer

```fsharp
open Partas.Nacara.Theme

PluginLayers.offer
    {
        Name = "directives"
        Css = ".nacara-steps { list-style: none; } .nacara-step { margin-block: 1rem; }"
    }
```

That is the whole plugin side. The theme reads what was offered when it bundles
its stylesheet and writes each one into a cascade layer of its own,
`nacara.<name>`, after its own layers — so your rules win a tie against the
theme's without needing to out-specify them.

Choose a name the theme does not already use. A name it does use replaces
nothing; you get two layers with the same name.

## Why a separate package

The theme reads what plugins offered, so the theme has to configure last. A
plugin that wanted to ask the theme what it declared would therefore have to
register after the thing that registers after it. Both sides depend on this
package instead, and neither depends on the other.

## Ordering against the theme

A CSS tool that generates a stylesheet — Tailwind, say — needs to know the
theme's layer names to declare its own cascade order against them:

```fsharp
match ThemeLayers.current () with
| [] -> "@layer mine;"
| names -> $"""@layer {String.Join(", ", names)}, mine;"""
```

`ThemeLayers.current ()` returns the theme's full layer names —
`nacara.tokens`, `nacara.base`, and so on — in cascade order.

:::warning
Read it when bundling, not when configuring. A plugin that registered before the
theme sees the empty list, because the theme has not declared anything yet.
:::

:::tip
This is one build at a time, in one process. A host building two sites
concurrently would see them share it.
:::

## What the theme does with it

Nothing it is obliged to. An offer is an offer: a theme that never reads
`PluginLayers.all ()` is within its rights, and a plugin should not render
unusably without its own styles if it can help it.
