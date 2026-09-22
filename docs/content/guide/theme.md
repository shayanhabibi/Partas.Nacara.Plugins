---
title: Theme
order: 3
---

`Partas.Nacara.Theme` is the documentation theme this site is built with: layout,
navbar, design tokens and the web components behind them. It is configured as a
pipeline, the same shape as the site itself.

```fsharp
open Partas.Nacara.Theme

let theme =
    Theme.defaults
    |> Theme.navbar [ NavbarSection("Guide", "guide", "/guide/introduction/") ]
    |> Theme.editUrl "https://github.com/you/repo/edit/main/docs"
    |> Theme.footer (Html.p [ Html.text "Built with Nacara" ])

let site =
    Site.create "My site"
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

`Theme.register` installs it; `Theme.docs` builds the collection that renders
your Markdown with it.

## Style layers

The theme's stylesheet is not one file. It is a list of named parts, each written
into a CSS cascade layer of its own, in this order:

| Layer | Holds |
|---|---|
| `tokens` | the custom properties every other layer reads |
| `base` | element defaults |
| `navbar` | the top bar |
| `layout` | page structure |
| `components` | callouts, cards, tables |
| `code` | code blocks and highlighting |
| `responsive` | breakpoints |

Because they are cascade layers, order decides ties rather than specificity. A
later layer wins against an earlier one whatever its selectors look like, so
restyling the theme does not mean out-specifying it.

### Replacing a layer

```fsharp
Theme.defaults
|> Theme.replaceLayer "components" (File.ReadAllText "css/components.css")
```

The layer keeps its place in the cascade, so everything that depended on its
position still works.

### Dropping a layer

```fsharp
Theme.defaults |> Theme.dropLayer "code"
```

For a part something else already provides — dropping `code` where a highlighting
plugin brings its own.

:::warning
Do not drop `tokens` unless you are replacing every custom property it defines.
The other six layers read over three hundred `var(--nacara-*)` references; with
`tokens` gone they resolve to nothing and the page renders wrong without saying
so. Everything else is safe to drop.
:::

### Adding a layer

```fsharp
Theme.defaults
|> Theme.layerAfter "components" "widgets" widgetCss
|> Theme.layerBefore "navbar" "banner" bannerCss
```

`layerAfter` puts yours later in the cascade, so it wins a tie against the layer
it follows. `layerBefore` puts it earlier, so the layer it precedes wins.

Naming a layer the theme does not have fails the build, with the known names in
the message. That is deliberate: a typo in a layer name is otherwise a silent
no-op.

### Replacing everything

```fsharp
Theme.defaults |> Theme.styles myLayers
```

The whole stylesheet, yours, as a `StyleLayer list` in cascade order.
`replaceLayer` and `dropLayer` are the usual way in — they leave the parts you
did not mention alone.

## Plugins with styles of their own

A plugin does not reach into the theme's layers. It offers one, and the theme
bundles it. See [Plugin style layers](styling.md).

## Licensing

This project is a derivative work of `Nacara.Theme.Default` and is licensed under
Apache-2.0 rather than the MIT that covers the rest of the repository. See
`LICENSE` and `NOTICE`.
