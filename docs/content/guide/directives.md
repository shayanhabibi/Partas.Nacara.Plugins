---
title: Directives
order: 2
---

A directive is a fenced block Markdown knows nothing about and your site renders
itself:

```markdown
:::note level=2
Everything between the fences is the body.
:::
```

`Partas.Nacara.Plugins.Directives` turns one of those into a typed F# function.
You write a decoder for the opening line and a render function for the result;
the plugin does the parsing, the nesting and the error reporting.

## Declaring one

A directive is a name, a decoder, and a render function.

```fsharp
open Nacara.Plugins

let note =
    Directive.create
        "note"
        (Decode.object (fun get ->
            {| Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 |}))
    |> Directive.render (fun ctx args body ->
        Html.aside [
            prop.className "note"
            prop.custom ("data-level", string args.Level)
            prop.children [ body ]
        ])
```

Register it on the site, and `:::note` is yours:

```fsharp
let site =
    Site.create "My site"
    |> Markdown.register
    |> Directives.register [ note ]
```

Registering directives does not take Nacara's built-in ones away. A name the
plugin does not recognise is handed back to whoever rendered it before.

## Arguments

Everything after the directive's name is read as a YAML flow mapping, then given
to your decoder — the same decoders that read front matter. That is why types
arrive as types rather than as strings:

```markdown
:::note level=2 title="Read me first" collapsed tags=[fsharp, dotnet]
```

| Written | Decoded with | Arrives as |
|---|---|---|
| `level=2` | `Decode.int` | `2` |
| `title="Read me first"` | `Decode.string` | `Read me first` |
| `collapsed` | `Decode.bool` | `true` |
| `tags=[fsharp, dotnet]` | `Decode.list Decode.string` | `["fsharp"; "dotnet"]` |
| `meta={author: you}` | `Decode.object` | a nested record |

An argument with no `=` is a flag, which is how you would write it anyway. A
value in brackets is read to its matching close, so spaces and nesting inside it
belong to the value:

```markdown
:::note tags=[fsharp, dotnet] meta={author: you, since: 2024}
```

### What is refused

The line is checked before YAML sees it, so a mistake on the directive's line
reports the directive's line rather than a YAML document you never wrote:

- an argument given twice
- an argument with no name before its `=`
- YAML punctuation — `, { } [ ] :` — in an argument's name
- YAML punctuation in a value that is not a balanced collection, so
  `title=a,b=2` cannot silently become two arguments

To keep punctuation as text, quote it: `title="Hello, world"`.

## Nesting

A nested directive's fence must be **shorter** than its parent's. Four colons
wrap three:

```markdown
::::steps
:::step title="Install"
Add the package.
:::
::::
```

Equal-length fences do not nest — the second one closes the first.

Here is that running on this page:

::::steps start=1
:::step title="Install"
Add the package.
:::
:::step title="Register"
Add the plugin to your site.
:::
::::

The two directives behind it:

```fsharp
let steps =
    Directive.create
        "steps"
        (Decode.object (fun get ->
            {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
    |> Directive.render (fun _ _ body ->
        Html.ol [ prop.className "nacara-steps"; prop.children [ body ] ])

let step =
    Directive.create
        "step"
        (Decode.object (fun get -> {| Title = get.Required.Field "title" Decode.string |}))
    |> Directive.render (fun ctx args body ->
        match ctx.TryAncestor<{| Start: int |}>() with
        | None ->
            ctx.Error ":::step only means something inside :::steps"
            Html.none
        | Some parent ->
            Html.li [
                prop.className "nacara-step"
                prop.custom ("data-number", string (parent.Start + ctx.Index))
                prop.children [ Html.h3 args.Title; body ]
            ])
```

## What the context knows

The first argument to a render function is a `DirectiveContext`:

| Member | Answers |
|---|---|
| `ctx.TryAncestor<'T>()` | the nearest enclosing directive whose arguments are `'T` |
| `ctx.TryParent<'T>()` | the immediately enclosing one, if its arguments are `'T` |
| `ctx.Index` | which sibling this is, counting from zero |
| `ctx.Depth` | how many directives enclose it |
| `ctx.Error` / `ctx.Warn` | say that something is wrong |

Ancestors are matched by the type their decoder produced, so `:::step` finds
`::::steps` by asking for `{| Start: int |}` — no shared name, no cast.

## When something is wrong

`ctx.Error` and a failed decode both do the same two things: write the message
to stderr, and render a visible element in place of the directive so the page
shows the problem rather than hiding it.

:::tip
An error does **not** fail the build. Nacara gives a directive no way to do
that, so a broken directive is loud rather than fatal — check the build output.
:::

## Styling

A directive that needs CSS offers it to the theme rather than shipping a
stylesheet the site has to remember to link. See
[Plugin style layers](styling.md).
