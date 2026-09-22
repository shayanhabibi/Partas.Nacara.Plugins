---
title: Partas.Nacara.Plugins
description: Plugins and a documentation theme for the Nacara static site generator
layout: splash
---

# Partas.Nacara.Plugins

Plugins and a documentation theme for [Nacara](https://github.com/MangelMaxime/Nacara),
the F# static site generator whose site is an F# program. Each package is
independent — take the one you want.

New here? Start with [getting started](guide/getting-started.md).

## Write your own `:::` blocks

**Partas.Nacara.Plugins.Directives** turns a fenced block into a typed F#
function: a decoder for the arguments on the opening line, a render function for
the result. Arguments arrive typed, nested directives read their parents by type,
and a broken one is loud rather than silent.

```markdown
::::steps
:::step title="Install"
Add the package.
:::
::::
```

→ [Directives](guide/directives.md)

## Style the theme without fighting it

**Partas.Nacara.Theme** is the documentation theme this site is built with. Its
stylesheet is a list of named CSS cascade layers rather than one file, so
replacing, dropping or reordering a part is a line of F# and order decides ties
instead of specificity.

```fsharp
Theme.defaults
|> Theme.replaceLayer "components" myCss
|> Theme.layerAfter "components" "widgets" widgetCss
```

→ [Theme](guide/theme.md)

## Let plugins contribute styles

**Partas.Nacara.Theme.Contracts** is what a theme and a styling plugin agree on,
in a package that depends on nothing so neither side depends on the other. A
plugin offers a cascade layer; the theme bundles it.

```fsharp
PluginLayers.offer { Name = "directives"; Css = myCss }
```

→ [Plugin style layers](guide/styling.md)

## CSS tooling

**Partas.Nacara.Plugins.Tailwind** runs Tailwind over the built site and orders
its output against the theme's layers. **Partas.Nacara.Plugins.DaisyUI** adds
DaisyUI on top of it, so `class="btn"` works in your markdown:

<button class="btn">Like this</button>

Both are documented in the
[repository README](https://github.com/shayanhabibi/Partas.Nacara.Plugins).

## Licensing

Most of this repository is MIT. `Partas.Nacara.Theme` is a derivative work of
`Nacara.Theme.Default` and is Apache-2.0 — see `LICENSE` and `NOTICE` in the
repository.
