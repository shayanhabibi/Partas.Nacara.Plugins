---
title: Setting up DaisyUI
---

DaisyUI in Nacara: Tailwind's component classes, so `class="btn"` means something
in your markdown.

<button class="btn">Like this</button>

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.DaisyUI
```

```fsharp
open Nacara.Core
open Nacara.Plugins

let site =
    Site.create "My site"
    |> Markdown.register
    |> DaisyUI.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

:::warning
DaisyUI registers **through** the Tailwind plugin. Do not register both —
anything you would have configured on `TailwindCss` goes on `DaisyUI` instead,
since the options are the same ones.
:::

## Themes

DaisyUI's own themes are a `@plugin` line in the Tailwind entry. The plugin
replaces `DaisyUI.Token` and `DaisyUI.ThemeToken` with the real install paths, so
you do not have to know where the package landed:

```fsharp
|> DaisyUI.registerWith (fun options ->
    { options with
        TailwindEntryHeader = [
            yield! options.TailwindEntryHeader
            yield $"@plugin \"{DaisyUI.ThemeToken}\" {{"
            // theme strings here
            yield "}"
        ] })
```

Keep `options.TailwindEntryHeader` in the list. Dropping it drops what the plugin
put there to make DaisyUI work at all.

## Ordering against the theme

The same as Tailwind's: the theme's cascade layers are readable through
`ThemeLayers.current ()`, so a generated stylesheet can declare itself after them.
See [Plugin style layers](styling.md).
