# Partas.Nacara.Plugins

1. [Tailwind](#tailwind)
2. [DaisyUI](#daisyui)
3. [Directives](#directives)

## Tailwind

TailwindCSS in Nacara!

```bash
dotnet add package Partas.Nacara.Plugins.Tailwind
```

```fsharp
open Nacara.Core
open Nacara.Plugins
open Nacara.Theme

let site =
    Site.create "Partas.Nacara.Plugins"
    |> Site.baseUrl "/Partas.Nacara.Plugins/"
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> TailwindCss.register
    // register with options
    |> TailwindCss.registerWith (fun twOpts ->
        {
            twOpts with
                TailwindEntryFooter = [
                    "@base {"
                    " --noot: var(--black);"
                    "}"
                ]
        }
        )
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

> [!NOTE]
> Base styles are not injected by default since it will
> conflict with the base nacara styling.
> You can change this by setting `TailwindEntryHeader = []` or `TailwindEntryHeader = [ "@import \"tailwindcss\"" ]`

## DaisyUI

DaisyUI in Nacara!

```fsharp
open Nacara.Core
open Nacara.Plugins
open Nacara.Theme

let site =
    Site.create "Partas.Nacara.Plugins"
    |> Site.baseUrl "/Partas.Nacara.Plugins/"
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> DaisyUI.register
    // register with options
    |> DaisyUI.registerWith (fun twOpts ->
        {
            twOpts with
                TailwindEntryHeader =
                    // If you want to use DaisyUI-themes
                    [
                        yield! twOpts.TailwindEntryHeader
                        // The DaisyUI.Token && DaisyUI.ThemeToken
                        // are replaced with the actual install paths
                        yield $"@plugin \"{DaisyUI.ThemeToken}\" {"
                        // Theme strings here
                        yield "}"
                    ]
                TailwindEntryFooter = [
                    "@base {"
                    " --noot: var(--black);"
                    "}"
                ]
        }
        )
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

> [!NOTE]
> DaisyUI registers through the TailwindCSS plugin. DO NOT use both. Any settings
> you may have in your TailwindCSS configuration can be transferred to the DaisyUI
> configuration.

## Directives

Your own `:::` blocks in Nacara markdown.

```bash
dotnet add package Partas.Nacara.Plugins.Directives
```

A directive is a decoder for the arguments on its opening line and a function
that renders it. The decoder is Nacara's own, the one front matter already uses,
so the arguments arrive typed and a missing required argument fails by name:

```fsharp
open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins

type Note = { Title: string option; Level: int }

let note =
    Directive.create "note" (Decode.object (fun get ->
        { Title = get.Optional.Field "title" Decode.string
          Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 }))
    |> Directive.render (fun _ args body ->
        Html.aside [
            prop.className $"nacara-note nacara-note--{args.Level}"
            prop.children [
                match args.Title with
                | Some title -> Html.p [ prop.className "nacara-note__title"; prop.text title ]
                | None -> Html.none
                body
            ]
        ])

let site =
    Site.create "My site"
    |> Markdown.register
    |> Directives.register [ note ]
```

Which renders:

```markdown
:::note title="Watch out" level=2
The body is **markdown**, rendered by Markdig as usual.
:::
```

A nested directive reads the one enclosing it by type, so `:::step` can number
itself from its parent's `start` without the author counting:

```fsharp
Directive.create "step" (Decode.object (fun get ->
    {| Title = get.Required.Field "title" Decode.string |}))
|> Directive.render (fun ctx args body ->
    match ctx.TryAncestor<Steps>() with
    | None ->
        ctx.Error ":::step only means something inside :::steps"
        Html.none
    | Some steps ->
        Html.li [
            prop.custom ("data-number", steps.Start + ctx.Index)
            prop.children [ Html.h3 args.Title; body ]
        ])
```

> [!IMPORTANT]
> A nested directive's fence must be **shorter** than its parent's, so `::::steps`
> wraps `:::step`. Markdig's custom containers follow the fenced-block rule: with
> equal-length fences the first `:::` closes the outer block, and the inner
> directives become its siblings rather than its children.

> [!NOTE]
> Registering a directive under a name Nacara already renders — `steps`, `filetree`,
> `preview` — shadows it deliberately. Every other name falls through untouched.
