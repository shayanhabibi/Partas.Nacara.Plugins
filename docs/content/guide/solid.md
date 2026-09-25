---
title: Live Solid examples
---

`Partas.Nacara.Plugins.Solid` turns an F# fence into a working
[Partas.Solid](https://github.com/shayanhabibi/Partas.Solid) example. When the
site builds, the plugin compiles the fence with Fable and the Partas.Solid
compiler plugin. It then passes the result through the Solid JSX compiler and
mounts what it renders under the code.

Here is one:

```fsharp solid render=Counter
[<SolidComponent>]
let Counter () =
    let count, setCount = createSignal 0

    button (onClick = fun _ -> setCount (count () + 1)) {
        $"Clicked {count ()} times"
    }
```

Nothing above runs in F# in the browser. The page loads a small bundle of what
Fable and Solid made of the fence.

## Registering it

```fsharp
open Nacara.Plugins

let site =
    Site.create "My site"
    |> Markdown.register
    |> SolidExamples.registerWith (
        SolidExamples.partasVersion "3.0.0"
    )
```

The plugin needs `dotnet` and Node 22.12 or later on the `PATH`. On the first
build it restores Fable and installs `solid-js`, `@solidjs/web`,
`@solidjs/compiler` and `rolldown`. It keeps them in `.nacara/partas-solid`,
next to your site, so later builds reuse them. When no example on the site has
changed, a build skips the compilers entirely. Under `nacara watch` the plugin
keeps Fable running, so saving a page recompiles only that page's examples.

## Writing examples

Put `solid` after the language of an `fsharp` fence. Every fence on a page
compiles into one module, in page order, so a later fence can use what an
earlier one declared. `Partas.Solid`, `Fable.Core` and `Fable.Core.JsInterop` are
already open.

What the fence renders depends on what it contains:

- **An expression** renders as it stands. `Counter ()` below reuses the
  component from the top of the page.
- **Declarations** render nothing on their own. Add `render=Name` to mount the
  component called `Name`.
- **`setup`** compiles the fence but leaves it off the page. Use it for
  helpers the reader does not need to see.

```fsharp solid
div (class' = "flex gap-2") {
    Counter ()
    Counter ()
}
```

Each counter keeps its own signal, as it would in any Solid app.

```fsharp solid setup
type Todo = { Id: int; Text: string; Done: bool }
```

The fence above declared a `Todo` record. It compiled with the page but does not
appear on it. The next fence uses it and names the component to mount:

```fsharp solid render=TodoList jsx
[<SolidComponent>]
let TodoList () =
    let todos, setTodos =
        createSignal [| { Id = 1; Text = "Write docs"; Done = true }
                        { Id = 2; Text = "Ship it"; Done = false } |]
    let draft, setDraft = createSignal ""

    let add () =
        if draft () <> "" then
            setTodos (Array.append (todos ()) [| { Id = todos().Length + 1; Text = draft (); Done = false } |])
            setDraft ""

    let toggle id =
        todos ()
        |> Array.map (fun t -> if t.Id = id then { t with Done = not t.Done } else t)
        |> setTodos

    div () {
        form (onSubmit = fun e -> e.preventDefault (); add ()) {
            input (value = draft (), onInput = fun e -> setDraft !!e.currentTarget?value)
            button (type' = "submit") { "Add" }
        }
        ul () {
            For.Keyed(each = todos ()) {
                yield fun todo _ ->
                    li (
                        onClick = (fun _ -> toggle todo.Id),
                        style = (if todo.Done then "text-decoration: line-through" else "")
                    ) { todo.Text }
            }
        }
    }
```

### Choosing what shows

`show=output` keeps the code off the page and renders only the result.
`show=code` does the opposite: the fence still compiles, so a broken example
still fails the build, but nothing is mounted.

```fsharp solid show=output
p () { "Only the output of this fence is on the page." }
```

### Showing the JSX

Add `jsx` to a fence to put a collapsed **JSX** panel under it. Opening the
panel shows the JSX that Fable and the Partas.Solid plugin made of the cell,
before the Solid compiler turned it into DOM code. Use it to show readers what
Partas.Solid does with a piece of F#.

```fsharp solid jsx
let name = "Solid"

p (class' = "greeting") { $"Hello, {name}!" }
```

The `TodoList` fence above is marked `jsx` too, so its panel shows the
declared component rather than a wrapper.

The panel holds only the cell's own functions:

- For an expression, that is the wrapper component the plugin generated for it.
- For declarations, it is each name the fence declares at column zero (`let`,
  `type` and `and`). When Fable wrote none of them, the panel shows the
  `render=` wrapper instead.

Imports, and anything Fable generated beside a declaration (such as a record's
`_$reflection` function), are left out. `setup` fences never get a panel.

`SolidExamples.showJsx true` gives every fence on the site a panel. The panels
are filled from a `<key>.jsx.json` file written next to each page's bundle, and
the file is fetched when a reader first opens a panel on the page.

### Naming a cell

Cells are numbered `c1`, `c2` and so on. Add `id=name` to give one a stable
name. The name appears on the placeholder element, which is the handle for
styling one example on its own.

## When an example is wrong

Fable's errors point at the line of the markdown file they came from, not at
the generated module. `nacara build` fails on them, like it does on a broken
link. `nacara watch` reports them as warnings and keeps serving, so you can fix
the fence and save again.

## Options

| Option | Default | |
| --- | --- | --- |
| `partasVersion` | `"3.*"` | The Partas.Solid package the examples compile against |
| `feed` | none | Adds a NuGet source; a local folder works |
| `fableVersion` | `"5.13.0"` | |
| `solidVersion` | `"2.0.0-rc.9"` | `solid-js`, `@solidjs/web` and `@solidjs/compiler` |
| `fenceToken` | `"solid"` | The word that marks a fence |
| `prelude` | the three `open`s above | Lines at the top of every page's module |
| `outputPath` | `"_partas/solid"` | Where the bundles go in the output |
| `workspacePath` | `".nacara/partas-solid"` | Where the generated project lives |
| `minify` | `true` | |
| `showJsx` | `false` | Adds a JSX panel under every fence, as if each were marked `jsx` |
