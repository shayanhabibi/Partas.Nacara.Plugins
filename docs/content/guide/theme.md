---
title: Cascade layers
---

`Partas.Nacara.Theme` is the documentation theme this site is built with: layout,
navbar, design tokens and the web components behind them. It is configured as a
pipeline, the same shape as the site itself.

```fsharp
open Partas.Nacara.Theme

let theme =
    Theme.defaults
    |> Theme.navbar [ NavbarSection("Guide", "guide", "/guide/getting-started/") ]
    |> Theme.editUrl "https://github.com/you/repo/edit/main/docs"
    |> Theme.footer (Html.p [ Html.text "Built with Nacara" ])

let site =
    Site.create "My site"
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

`Theme.register` installs it; `Theme.docs` builds the collection that renders
your Markdown with it.

To put a logo before the site's title in the navbar, give `Theme.brandIcon` an
image path relative to the site root, or inline SVG:

```fsharp
Theme.defaults |> Theme.brandIcon (BrandIcon.Image "logo.svg")
Theme.defaults |> Theme.brandIcon (BrandIcon.Svg """<svg viewBox="0 0 24 24">...</svg>""")
```

The icon is `1.5rem` tall. SVG drawn with `currentColor` follows the colour
scheme.

## Layouts

A page chooses its layout in its front matter:

```yaml
---
title: Partas.Nacara.Plugins
layout: splash
---
```

| Layout | What it is |
| --- | --- |
| `docs` | The ordinary page, and what a page with no `layout` gets: menu, table of contents, title, edit link, previous and next. |
| `splash` | A landing page. One column as wide as `--nacara-splash-width`, with no menu, no table of contents and no title, so the page writes its own heading. |
| `bare` | A canvas between the navbar and the footer, which the page lays out itself. |

A layout of your own is a name and what it does to the page. Start from the
built-in layout it is nearest to, and change what differs:

```fsharp
Theme.defaults
|> Theme.addLayout "reference" DocPage.withoutPageNav
|> Theme.addLayout "landing" (DocPage.splash >> fun page -> { page with ShowTitle = true })
```

Naming a built-in layout replaces it, and a page naming a layout the theme does
not have fails the build. The layout's element carries `data-layout`, so its
own CSS reaches only its pages:

```fsharp
Theme.defaults
|> Theme.css """[data-layout="landing"] { --nacara-splash-width: 80rem; }"""
```

The Figma plugin draws a template of each layout, and exports one you draw as
the `Theme.addLayout` that adds it. See `design/README.md` in the repository.

## Components

Each part of the page is a function a site can replace, and the rest of the
page stays the theme's:

| Setter | Replaces |
| --- | --- |
| `Theme.replaceNavbar` | The bar across the top, and the drawer it folds into |
| `Theme.replaceSidebar` | The section's menu |
| `Theme.replaceToc` | The page's headings, beside the content |
| `Theme.replacePageNav` | The previous and next links |
| `Theme.replaceEditLink` | The link that edits the page at its source |
| `Theme.replaceFooter` | The whole `<footer>` element |

A replacement is given the theme's options, the page's `DocPage` and its
context, and returns Feliz markup:

```fsharp
Theme.defaults
|> Theme.replaceFooter (fun options page context ->
    Html.footer [
        prop.className "nacara-footer"
        prop.text $"%s{page.Title} · %s{context.Site.Title}"
    ])
```

The theme's own parts are public, as `Components.navbar`, `Components.sidebar`,
`Components.toc`, `Components.pageNav` and `Components.editLink`, so a
replacement can wrap one instead of starting over:

```fsharp
Theme.defaults
|> Theme.replaceNavbar (fun options page context ->
    Html.div [
        Html.p [ prop.className "banner"; prop.text "2.0 is out" ]
        Components.navbar options context
    ])
```

The page still decides what is drawn: a splash page has no table of contents or
page links, whoever renders them. The sidebar is drawn on every page, since on a
narrow screen the navbar's sections fold into it; `page.ShowMenu` says whether
the page wants a menu. The context's front matter is boxed, because one theme
serves collections of more than one front-matter type: `unbox` it to the type
of your collection.

### Before and after the content

Two more places take markup of the site's, where the theme has none of its own:

| Setter | Where |
| --- | --- |
| `Theme.beforeContent` | Above the page's title, inside its `<main>`: an announcement, an old-version warning |
| `Theme.afterContent` | After the content, before the edit link and page links: comments, a feedback form |

```fsharp
Theme.defaults
|> Theme.beforeContent (fun _ page _ ->
    Html.aside [
        prop.className "nacara-callout"
        prop.custom ("data-kind", "note")
        prop.text "2.0 is out"
    ])
```

They are drawn on every page, bare ones too, unless a layout says otherwise.

### Parts for one layout

A layout can render parts its own way, and leave the rest to the site:

```fsharp
Theme.defaults
|> Theme.addLayout "landing" DocPage.splash
|> Theme.beforeContent announcement
|> Theme.layoutComponents "landing" (
    ThemeComponents.navbar heroNavbar
    >> ThemeComponents.beforeContent (fun _ _ _ -> Html.none))
```

On `landing` pages the navbar is `heroNavbar` and there is no announcement;
every other page keeps the site's announcement and the theme's navbar. The
`ThemeComponents` functions are the same parts as the `Theme` setters:
`navbar`, `sidebar`, `toc`, `pageNav`, `editLink`, `footer`, `beforeContent` and
`afterContent`. The layout has to exist already, built in or added before, and
calling `layoutComponents` again for it adds to what it has.

## Design tokens

The `tokens` layer is the one layer you do not write CSS for. It is a record, and
every field in it is a custom property the rest of the theme reads:

```fsharp
Theme.defaults
|> Theme.lightTokens (fun tokens -> { tokens with Primary = "#c0392b" })
|> Theme.darkTokens (fun tokens -> { tokens with Primary = "#e74c3c" })
```

A field's name is its property's name, in kebab case, under `--nacara-`: `Primary`
is `--nacara-primary`, `BgSubtle` is `--nacara-bg-subtle`, `Space12` is
`--nacara-space-12`. So the completion list in your editor is the list of tokens,
and a token that does not exist does not compile.

Values are CSS, unparsed — a colour, a length, a `var()` reading another token.
Only the names are typed.

| Function | Reaches |
|---|---|
| `Theme.lightTokens` | the `--nacara-*` properties, light scheme |
| `Theme.darkTokens` | the same, dark scheme |
| `Theme.lightSyntax` | the `--tok-*` highlighting colours, light scheme |
| `Theme.darkSyntax` | the same, dark scheme |
| `Theme.tokens` | all four at once, for a wholesale replacement |

The dark records are read as overrides: a dark value identical to its light one
is not written twice, so restating a light value in `darkTokens` costs nothing.

Each of these rewrites the `tokens` layer, so it fails if you dropped that layer —
there is then nothing for the properties to go in.

### Tokens in Figma

`DesignTokens` writes one colour scheme as a
[design tokens](https://www.designtokens.org/tr/drafts/format/) document, and
reads documents back over a set of tokens:

```fsharp
let tokens = Tokens.defaults
File.WriteAllText("design/tokens/light.tokens.json", DesignTokens.write tokens.Light tokens.SyntaxLight)
File.WriteAllText("design/tokens/dark.tokens.json", DesignTokens.write tokens.Dark tokens.SyntaxDark)

Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")
```

`DesignTokens.fromDirectory` reads `light.tokens.json` and `dark.tokens.json`, the
names the Figma plugin saves under. A scheme whose document is missing keeps its
tokens. `DesignTokens.readTheme` does the same from two strings, and
`DesignTokens.readTokens` and `readSyntax` read one record at a time.

A token's path is its prefix, its kind and its CSS name: `--nacara-bg-subtle` is
`nacara.color.bg-subtle`, `--nacara-space-4` is `nacara.size.space-4`, `--tok-keyword`
is `tok.color.keyword`. A `var()` of another token becomes an alias to it. The kinds:

| Kind | Holds | Example |
|---|---|---|
| `color` | a hex colour | `nacara.color.primary` |
| `size` | a length in `px` or `rem`, font sizes included | `nacara.size.font-size-2xl` |
| `number` | a line height, or a `fontWeight` | `nacara.number.line-height` |
| `shadow` | a `box-shadow`, one entry per layer | `nacara.shadow.shadow` |
| `font` | a font stack | `nacara.font.font-sans` |
| `css` | anything else, kept as written | `nacara.css.content-width` (`75ch`) |

#### Designing the theme in Figma

The theme's Figma file is on
[Figma Community](https://www.figma.com/community/file/1694279269526963076): the
variables already imported, the components, and a template of each layout. To
design a site's theme there and bring it back to the site:

1. Duplicate the file into your drafts.
2. Change the variables in the `Partas.Nacara.Theme` collections, Light and Dark,
   and the `Shadow` and `Shadow (Dark)` effect styles. The components and the
   example page follow, since they are bound to the variables.
3. Run the plugin, choose **Export from variables**, and save `light.tokens.json`
   and `dark.tokens.json` into a folder of the site, such as `design/tokens`.
4. Read them in the site:

```fsharp
Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")
```

Exporting again and saving over the two files is all a later change takes. A
layout drawn on the Layouts page comes back the same way, as the
`Theme.addLayout` lines **Export layouts** writes.

The plugin is `design/figma-plugin` in the repository. Until it is on Figma
Community, run it in the Figma desktop app through **Plugins → Development →
Import plugin from manifest…**, choosing its `manifest.json`.

#### What the plugin carries

The plugin imports these documents into Figma and exports them back:

- Colours, sizes and numbers are variables. A variable's name is the token path with
  slashes (`nacara/color/bg-subtle`), and its code syntax is the CSS property, so Dev
  Mode shows `var(--nacara-bg-subtle)`. The schemes are modes of one collection, or one
  collection each on a Figma plan limited to one mode.
- A shadow is an effect style, `Shadow` for Light and `Shadow (Dark)` for Dark, since no
  variable can hold one.
- The text styles the plugin draws read their size and weight from the font-size and
  font-weight variables, so changing `font-size-2xl` resizes `Heading/H1`.

So a theme designed in Figma comes back to code through the variables and the shadow
styles. What lives only in a text style (its family, letter spacing, a weight with no
token) or in a component's structure does not.

Figma has only pixels. Reading a document converts each length back to the unit
the token already uses, so `space-4` exported as `16px` reads back as `1rem`, and a
line height stays a bare number. Reading goes by the last segment of a path, so
variables regrouped in Figma still land on the right token. A variable with no
matching token is ignored.

## Style layers

The theme's stylesheet is not one file. It is a list of named parts, each written
into a CSS cascade layer of its own, in this order:

| Layer | Holds |
|---|---|
| `tokens` | the custom properties every other layer reads, rendered from the record above |
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
so. Changing a token's value is what `Theme.lightTokens` is for, and it keeps the
layer. Everything else is safe to drop.
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
