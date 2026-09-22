---
title: Getting started
---

Every package here is independent. Add the one you want to a Nacara site — there
is no umbrella package to install first.

| Package | What it does |
|---|---|
| `Partas.Nacara.Plugins.Directives` | Your own `:::` blocks, as typed F# functions |
| `Partas.Nacara.Plugins.Tailwind` | Tailwind over the built site |
| `Partas.Nacara.Plugins.DaisyUI` | DaisyUI, through the Tailwind plugin |
| `Partas.Nacara.Theme` | The documentation theme, in replaceable cascade layers |
| `Partas.Nacara.Theme.Contracts` | What a theme and a styling plugin agree on |

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.Directives
```

A Nacara site is an F# program, so a plugin is a function in the pipeline that
builds it:

```fsharp
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let site =
    Site.create "My site"
    |> Site.baseUrl "/"
    |> Site.output "output"
    |> Markdown.register
    |> Directives.register [ note ]
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")

[<EntryPoint>]
let main argv = Nacara.run site argv
```

Order matters in one direction only: the theme reads what plugins offered it, so
register it last.

:::tip
Editing the site? Serve it with live reload from the repository root:

`dotnet fsi build.fsx -- docs --watch`

The page reloads as you type.
:::

## Where to next

- [Directives](directives.md) — the plugin most of this documentation is about
- [Theme](theme.md) — restyling without out-specifying anything
- [Tailwind](tailwind.md) and [DaisyUI](daisyui.md) — if you would rather bring
  your own CSS framework
