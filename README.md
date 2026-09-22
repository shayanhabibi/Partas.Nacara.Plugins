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

An argument written on its own, with no `=`, is a flag: `:::details collapsible`
decodes as `{ collapsible: true }`.

An unquoted value is handed to YAML as it stands, which is what makes `level=2` an
int rather than the string `"2"`. That puts YAML's own punctuation — `,` `{` `}`
`[` `]` `:` — out of bounds in an unquoted value, since it would end the argument
early; write `title="a, b"` and it is text. A line that breaks this is refused by
name rather than silently misread, as are a repeated argument and one with no name.

A nested directive reads the one enclosing it by type, so `:::step` can number
itself from its parent's `start` without the author counting. Lookup is by the
argument type, not by name, so an anonymous record is enough — there is no type to
declare and nothing to share between the two directives:

```fsharp
Directive.create "step" (Decode.object (fun get ->
    {| Title = get.Required.Field "title" Decode.string |}))
|> Directive.render (fun ctx args body ->
    match ctx.TryAncestor<{| Start: int |}>() with
    | None ->
        ctx.Error ":::step only means something inside :::steps"
        Html.none
    | Some steps ->
        Html.li [
            prop.custom ("data-number", steps.Start + ctx.Index)
            prop.children [ Html.h3 args.Title; body ]
        ])
```

`ctx` also offers:

| Member | What it answers |
| --- | --- |
| `ctx.TryAncestor<'T>()` | The nearest enclosing directive whose arguments are `'T`, at any depth. |
| `ctx.TryParent<'T>()` | The same, but only if it is the *immediately* enclosing one. |
| `ctx.Index` | Position among the siblings sharing this directive's name, from zero. |
| `ctx.Depth` | How many directives enclose this one. |
| `ctx.Error` / `ctx.Warn` | Report a fault — see [When a directive goes wrong](#when-a-directive-goes-wrong). |

> [!IMPORTANT]
> A nested directive's fence must be **shorter** than its parent's, so `::::steps`
> wraps `:::step`. Markdig's custom containers follow the fenced-block rule: with
> equal-length fences the first `:::` closes the outer block, and the inner
> directives become its siblings rather than its children.

> [!NOTE]
> Registering a directive under a name Nacara already renders — `steps`, `filetree`,
> `preview` — shadows it deliberately. Every other name falls through untouched.
> Names match exactly, including case: `:::Note` is not the `note` you registered,
> and falls through like any other name you did not claim.

### When a directive goes wrong

Nothing here fails the build, and nothing can: Nacara wires no diagnostic sink
through to a markdown extension, so there is no fatal error to raise. Three things
degrade instead, each of them visible:

- **Arguments that do not decode.** The wrapper is dropped and the body — the
  author's writing, which is worth more — is rendered inside a
  `<div class="nacara-directive-error">` carrying the reason and the line it is on.
  Children of that directive see no ancestor for it, because there is no decoded
  value to give them.
- **A render function that throws.** The same error element, with the exception's
  message. The page carries on; the directive after it renders normally.
- **`ctx.Error` and `ctx.Warn`.** Both write a line to stderr, under `error:` and
  `warning:` respectively, naming the directive. `ctx.Error` does *not* fail the
  build — the severity decides the label and nothing else. What the page shows is
  whatever your render function went on to return, so if you return `Html.none` the
  author's body is gone without a trace on the page.

Every case also writes its message to stderr, so a build log has them all.

### Styling

The plugin ships no CSS — the classes in your render function are yours to style.
Offer them to the theme as a cascade layer of your own:

```fsharp
open Partas.Nacara.Theme

PluginLayers.offer
    { Name = "directives"
      Css = ".nacara-steps { list-style: none; } .nacara-step { margin-block: 1rem; }" }
```

The theme bundles it into `nacara.directives`, beside its own layers rather than
fighting them.

### Registering without a site

`Directives.register` is `Directives.create` plus `Site.plugin`. When you are
assembling plugins yourself, use `create` directly:

```fsharp
let plugin = Directives.create [ note; steps; step ]
```

Both raise if two directives claim one name — a name selects exactly one directive,
so the build cannot choose. `Directives.duplicates` answers the same question
without raising, returning the names claimed more than once, which is what to call
if you are validating a list you assembled from somewhere else.

## Licensing and attribution

Most of this repository is MIT. One project is not.

`Partas.Nacara.Theme` is a derivative work of `Nacara.Theme.Default` from the
[Nacara](https://github.com/MangelMaxime/Nacara) project, copyright Maxime Mangel,
licensed under the Apache License 2.0. Its sources were copied and then modified -
the stylesheet was split into named cascade layers, the entry stylesheet is generated
rather than embedded, and the namespace and assembly were renamed so that a consumer
referencing both this and upstream does not get two assemblies claiming the same
types. That project is published as Apache-2.0, each modified file says so at its
top, and the full terms are in [LICENSE](LICENSE) with the attribution in
[NOTICE](NOTICE).

Apache-2.0 code cannot be redistributed under a more permissive label, so the theme
package keeps its own licence rather than the repository-wide MIT. The remaining
projects - `Partas.Nacara.Theme.Contracts`, `Partas.Nacara.Plugins.Directives`,
`Partas.Nacara.Plugins.Tailwind` and `Partas.Nacara.Plugins.DaisyUI` - are original
work and stay MIT.
