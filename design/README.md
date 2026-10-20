# Design

The theme's tokens, for design tools.

- `tokens/light.tokens.json`, `tokens/dark.tokens.json`: `Tokens.defaults` as
  [design tokens](https://www.designtokens.org/tr/drafts/format/) documents, one per colour
  scheme, written by `DesignTokens.write`. A test fails when they drift from the defaults.
- `tokens/motion.json`: the hovers this repository's docs site plays, exported from Figma by the
  plugin's **Export motion** and read by `Partas.Nacara.Plugins.Motion`. It is not generated:
  export it again after changing a hover in Figma.
- `figma-plugin/`: a Figma development plugin that imports those documents as variables and
  exports variables back as the same documents. `figma-plugin/listing/` holds its Figma
  Community listing and how to publish it.

## Figma

The template file, with the variables imported and a specimen sheet for each scheme, is on
Figma Community, to duplicate into your own drafts:
<https://www.figma.com/community/file/1694279269526963076>.

The plugin's Figma Community page is
<https://www.figma.com/community/plugin/1694284251753540121>, which opens once Figma has
reviewed it. Until then, to use the plugin in another file: in the Figma desktop app, open **Plugins → Development →
Import plugin from manifest…** and choose `figma-plugin/manifest.json`. Then run it, load the two
`.tokens.json` files and choose **Import into variables**. Running it again updates the
variables in place.

To bring Figma edits back, choose **Export from variables**, save the two documents over
`tokens/`, and read them in the site:

```fsharp
Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")
```

What comes back:

- **Variables:** colours, spacing, radius, layout sizes, font sizes, line heights and the
  heading weight, aliases included.
- **Shadows:** the effect styles `Shadow` (Light) and `Shadow (Dark)`. A shadow is a style,
  not a variable, because no variable can hold one; the plugin tags each style with its token
  (shared plugin data, namespace `nacara`), so renaming it is safe.
- **Text styles**, through the variables they are bound to: a style's size and, for headings,
  its weight. Changing `font-size-2xl` resizes `Heading/H1`, and the export carries it.

What does not: a text style's family (Inter and Fira Mono stand in for the theme's font
stacks), letter spacing and weights that have no token, and component structure or any value
not bound to a variable.

## Components, an example page and layouts

With the tokens imported, **Build components, example and layouts** draws the theme on three
pages:

- **Components**: the navbar, sidebar, table of contents and footer, and the pieces a page is
  made of. These are badges, callouts, code blocks (code and terminal frames), tables, tabs,
  details, steps, blockquotes, page navigation and the edit link. Every colour, padding, gap and
  radius is bound to a token variable, and each component's description names the CSS class it
  draws. Text uses Inter for the theme's system font stack. Code uses Fira Mono, which is Fira
  Code without ligatures: the theme turns ligatures off in code, and Figma cannot.
- **Example**: a docs page at 1440px composed from those components, once per scheme. The dark
  page is the light one with the Dark mode applied, or, where each scheme is a collection of its
  own, rebound to the Dark collection's variables.
- **Layouts**: a template of each layout a page can name in its front matter. `Layout/docs` is
  the ordinary page, `Layout/splash` is the wide landing page, and `Layout/bare` is the canvas
  between the navbar and the footer.

A component or text style that already exists by name is kept, so building again adds only what
is missing and leaves your edits alone; only the text styles' bindings are applied again. The
example pages are redrawn each time, the Dark one with the `Shadow (Dark)` style. A layout
template that already exists is kept too.

## Layouts of your own

A frame on the Layouts page named `Layout/<name>` is a layout. Duplicate the template nearest
to what you want, rename it, and delete the parts it leaves out: the `Sidebar`, `TOC`,
`Page nav` and `Edit link` instances and the `Page title` text. A table of contents is only
shown beside a menu.

**Export layouts** writes each one that is not a theme layout as it was drawn, as the F# that adds
it:

```fsharp
Theme.defaults
|> Theme.addLayout "reference" (fun page -> { page with ShowPageNav = false })
|> Theme.addLayout "landing" (DocPage.splash >> fun page -> { page with ShowTitle = true })
```

A page with `layout: reference` in its front matter then uses it. Only which parts are shown
comes back. Spacing and widths come from the tokens, and anything else a layout looks like is
CSS, selected by `[data-layout="<name>"]`.

## Motion

A hover is a variant whose **While hovering** interaction changes to another variant of its set
with **Smart animate**. In the plugin's Motion section, **Find hovers** lists each hover with what
comes across and what does not, and why. A set of your own has a **Motion name of** field, and its
hovers apply to elements with `data-motion` set to that name. **Export motion** saves
`motion.json`; save it as `tokens/motion.json`. The docs site's
[motion guide](https://shayanhabibi.github.io/Partas.Nacara.Plugins/guide/motion/) has the rest.

## Naming

| Theme | CSS | Token path | Figma variable |
|---|---|---|---|
| `BgSubtle` | `--nacara-bg-subtle` | `nacara.color.bg-subtle` | `nacara/color/bg-subtle` |
| `Space4` | `--nacara-space-4` | `nacara.size.space-4` (`1rem`) | `nacara/size/space-4` (`16`) |
| `LayoutGap` | `--nacara-layout-gap` | `{nacara.size.space-8}` alias | alias to `nacara/size/space-8` |
| `ContentWidth` | `--nacara-content-width` | `nacara.css.content-width` (`75ch`) | string `nacara/css/content-width` |
| `FontSize2xl` | `--nacara-font-size-2xl` | `nacara.size.font-size-2xl` (`2.25rem`) | `nacara/size/font-size-2xl` (`36`) |
| `LineHeight` | `--nacara-line-height` | `nacara.number.line-height` (`1.65`) | `nacara/number/line-height` |
| `Shadow` | `--nacara-shadow` | `nacara.shadow.shadow` | effect style `Shadow`, `Shadow (Dark)` |
| `Keyword` | `--tok-keyword` | `tok.color.keyword` | `tok/color/keyword` |

Each scheme is a mode of the collection `Partas.Nacara.Theme`. On a plan limited to one mode
(Figma Starter) it is a collection of its own, `Partas.Nacara.Theme (Light)` and
`Partas.Nacara.Theme (Dark)`. The documents are the same either way.

## Regenerating the documents

After changing `Tokens.defaults`, build the theme in Release, then run this as an F# script
from the repository root:

```fsharp
#r "nuget: Nacara.Core, 2.0.0-beta.6"
#r "src/Partas.Nacara.Theme/bin/Release/net10.0/Partas.Nacara.Theme.dll"
open Partas.Nacara.Theme
let d = Tokens.defaults
System.IO.File.WriteAllText("design/tokens/light.tokens.json", DesignTokens.write d.Light d.SyntaxLight)
System.IO.File.WriteAllText("design/tokens/dark.tokens.json", DesignTokens.write d.Dark d.SyntaxDark)
```
