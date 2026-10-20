# Design

The theme's tokens, for design tools.

- `tokens/light.tokens.json`, `tokens/dark.tokens.json`: `Tokens.defaults` as
  [design tokens](https://www.designtokens.org/tr/drafts/format/) documents, one per colour
  scheme, written by `DesignTokens.write`. A test fails when they drift from the defaults.
- `figma-plugin/`: a Figma development plugin that imports those documents as variables and
  exports variables back as the same documents.

## Figma

The template file, with the variables imported and a specimen sheet for each scheme:
<https://www.figma.com/design/Pp4xHuM3EbO839BHApfmAb>.

To use the plugin in another file: in the Figma desktop app, open **Plugins → Development →
Import plugin from manifest…** and choose `figma-plugin/manifest.json`. Then run it, load the two
`.tokens.json` files and choose **Import into variables**. Running it again updates the
variables in place.

To bring Figma edits back, choose **Export from variables**, save the two documents, and read
them in the site:

```fsharp
let light = File.ReadAllText "design/light.tokens.json"
let dark = File.ReadAllText "design/dark.tokens.json"

Theme.defaults
|> Theme.lightTokens (DesignTokens.readTokens light)
|> Theme.darkTokens (DesignTokens.readTokens dark)
|> Theme.lightSyntax (DesignTokens.readSyntax light)
|> Theme.darkSyntax (DesignTokens.readSyntax dark)
```

## Naming

| Theme | CSS | Token path | Figma variable |
|---|---|---|---|
| `BgSubtle` | `--nacara-bg-subtle` | `nacara.color.bg-subtle` | `nacara/color/bg-subtle` |
| `Space4` | `--nacara-space-4` | `nacara.size.space-4` (`1rem`) | `nacara/size/space-4` (`16`) |
| `LayoutGap` | `--nacara-layout-gap` | `{nacara.size.space-8}` alias | alias to `nacara/size/space-8` |
| `ContentWidth` | `--nacara-content-width` | `nacara.css.content-width` (`75ch`) | string `nacara/css/content-width` |
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
