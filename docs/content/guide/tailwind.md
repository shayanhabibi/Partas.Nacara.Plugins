---
title: Setting up Tailwind
---

Tailwind CSS in Nacara. The plugin runs Tailwind over the built site and writes
the result as an asset, so utility classes written in your markdown and in your
F# render functions both end up in the stylesheet.

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.Tailwind
```

```fsharp
open Nacara.Core
open Nacara.Plugins

let site =
    Site.create "My site"
    |> Markdown.register
    |> TailwindCss.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

## Configuring it

`TailwindCss.registerWith` takes the options instead of the defaults:

```fsharp
|> TailwindCss.registerWith (fun options ->
    { options with
        TailwindEntryFooter = [
            "@base {"
            "  --noot: var(--black);"
            "}"
        ] })
```

`TailwindEntryHeader` and `TailwindEntryFooter` are the lines written before and
after the generated utilities, which is where an `@import`, a `@plugin` or a rule
of your own goes.

:::warning
Base styles are **not** injected by default, because Tailwind's reset fights the
theme's own base layer. If you want them, say so explicitly:
`TailwindEntryHeader = [ "@import \"tailwindcss\"" ]`.
:::

## Ordering against the theme

The theme writes its stylesheet as named CSS cascade layers. A generated
stylesheet that declares its own layer after them wins ties without needing to
out-specify anything — see [Plugin style layers](styling.md) for
`ThemeLayers.current ()`, which answers what the theme declared.

:::tip
Using DaisyUI? Register that instead, not both. See [DaisyUI](daisyui.md).
:::
