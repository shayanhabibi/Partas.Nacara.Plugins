---
title: Designing in Figma
---

The theme's tokens, components and layouts are a Figma file, and a Figma plugin
carries the tokens between that file and a site. A site's theme can be designed
visually there and exported back to the site's F#, as two design tokens
documents the site reads.

## The file and the plugin

- **The file** is on
  [Figma Community](https://www.figma.com/community/file/1694279269526963076):
  the tokens as variables, the theme's components bound to them, an example docs
  page in each colour scheme, and a template of each layout.
- **The plugin**, Nacara Theme Tokens, exports the variables as the documents and
  imports them back. Its
  [Figma Community page](https://www.figma.com/community/plugin/1694284251753540121)
  opens once Figma has reviewed it.

Until then the plugin runs from the repository, in the Figma desktop app:

1. Clone [the repository](https://github.com/shayanhabibi/Partas.Nacara.Plugins),
   or download it as a ZIP from GitHub (**Code → Download ZIP**).
2. In Figma, choose **Plugins → Development → Import plugin from manifest…** and
   pick `design/figma-plugin/manifest.json`.

The plugin is then under **Plugins → Development** in every file. The browser
version of Figma cannot load a plugin this way.

## Designing the theme

1. Duplicate the Community file into your drafts.
2. Change the variables in the `Partas.Nacara.Theme` collections, Light and Dark,
   and the `Shadow` and `Shadow (Dark)` effect styles. The components and the
   example pages follow, since they are bound to the variables.
3. Run the plugin and choose **Export from variables**, then **Save
   light.tokens.json** and **Save dark.tokens.json** into a folder of the site,
   such as `design/tokens`.
4. Read them in the site:

```fsharp
Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")
```

A later change is the same export, saved over the two files.

`DesignTokens.fromDirectory` reads `light.tokens.json` and `dark.tokens.json`. A
scheme whose document is missing keeps its tokens, and a variable with no
matching token is ignored. The [theme guide](theme.md) has the rest of the
`DesignTokens` API.

## Layouts

The Layouts page has a template of each layout a page can name in its front
matter: `Layout/docs` is the ordinary page, `Layout/splash` the wide landing
page, and `Layout/bare` the canvas between the navbar and the footer.

A frame on that page named `Layout/<name>` is a layout of your own. Duplicate the
template nearest to what you want, rename it, and delete the parts it leaves out:
the `Sidebar`, `TOC`, `Page nav` and `Edit link` instances and the `Page title`
text. A table of contents is only shown beside a menu.

**Export layouts** writes each layout that is not one of the theme's as the F#
that adds it:

```fsharp
Theme.defaults
|> Theme.addLayout "reference" (fun page -> { page with ShowPageNav = false })
|> Theme.addLayout "landing" (DocPage.splash >> fun page -> { page with ShowTitle = true })
```

A page with `layout: reference` in its front matter then uses it. Only which
parts are shown comes back. Spacing and widths come from the tokens, and
anything else a layout looks like is CSS, selected by `[data-layout="<name>"]`.
[Layouts](theme.md) in the theme guide has more.

## What comes back

- **Variables:** colours, spacing, radius, layout sizes, font sizes, line heights
  and the heading weight, aliases included. A variable's name is the token path
  with slashes (`nacara/color/bg-subtle`), and its code syntax is the CSS
  property, so Dev Mode shows `var(--nacara-bg-subtle)`.
- **Shadows:** the effect styles `Shadow` (Light) and `Shadow (Dark)`, since no
  variable can hold a shadow. The plugin tags each style with its token, so
  renaming one is safe.
- **Text styles**, through the variables they are bound to: a style's size and,
  for headings, its weight. Changing `font-size-2xl` resizes `Heading/H1`, and the
  export carries it.

What does not: a text style's family (Inter and Fira Mono stand in for the
theme's font stacks), letter spacing and weights that have no token, and a
component's structure or any value not bound to a variable.

Figma has only pixels. Reading a document converts each length back to the unit
the token already uses, so `space-4` exported as `16px` reads back as `1rem`, and
a line height stays a bare number. Reading goes by the last segment of a path, so
variables regrouped in Figma still land on the right token.

| Theme | CSS | Figma |
|---|---|---|
| `BgSubtle` | `--nacara-bg-subtle` | `nacara/color/bg-subtle` |
| `Space4` | `--nacara-space-4` (`1rem`) | `nacara/size/space-4` (`16`) |
| `LayoutGap` | `--nacara-layout-gap` | alias to `nacara/size/space-8` |
| `ContentWidth` | `--nacara-content-width` (`75ch`) | string `nacara/css/content-width` |
| `LineHeight` | `--nacara-line-height` (`1.65`) | `nacara/number/line-height` |
| `Shadow` | `--nacara-shadow` | effect styles `Shadow`, `Shadow (Dark)` |
| `Keyword` | `--tok-keyword` | `tok/color/keyword` |

Each scheme is a mode of the collection `Partas.Nacara.Theme`. On a Figma plan
limited to one mode it is a collection of its own, `Partas.Nacara.Theme (Light)`
and `Partas.Nacara.Theme (Dark)`. The documents are the same either way.

## Motion

Hovers drawn in Figma come across too. A hover is a variant whose **While
hovering** interaction changes to another variant of its set with **Smart
animate**. In the plugin's Motion section, **Find hovers** lists each hover with
what comes across and what does not, and **Export motion** saves `motion.json`
for `Partas.Nacara.Plugins.Motion` to read. The [motion guide](motion.md) has
the steps, the markup a hover binds to, and the F# that configures it.

## Starting from your site's tokens

A site whose tokens are already changed in F# can write them as the two
documents and import them into any file:

```fsharp
let tokens = Tokens.defaults // or the site's own
File.WriteAllText("design/tokens/light.tokens.json", DesignTokens.write tokens.Light tokens.SyntaxLight)
File.WriteAllText("design/tokens/dark.tokens.json", DesignTokens.write tokens.Dark tokens.SyntaxDark)
```

In the plugin, load both files and choose **Import into variables**. Running it
again updates the variables in place. Then **Build components, example and
layouts** draws the theme on a Components page, an Example page and a Layouts
page. A component, text style or layout template that already exists is kept,
so building again adds only what is missing and leaves your edits alone.
