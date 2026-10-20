module Partas.Nacara.Plugins.Tests.DesignTokens

open System.IO
open System.Text.Json
open Expecto
open Partas.Nacara.Theme

let private defaults = Tokens.defaults

let private light = DesignTokens.write defaults.Light defaults.SyntaxLight

let private dark = DesignTokens.write defaults.Dark defaults.SyntaxDark

/// <summary>The checked-in documents the Figma plugin imports.</summary>
let private checkedIn scheme =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "design", "tokens", $"%s{scheme}.tokens.json")
    |> File.ReadAllText
    |> fun text -> text.ReplaceLineEndings "\n"

let private token (json: string) (path: string) =
    use document = JsonDocument.Parse json

    path.Split '.'
    |> Array.fold (fun (element: JsonElement) segment -> element.GetProperty segment) document.RootElement
    |> fun element -> element.Clone()

[<Tests>]
let tests =
    testList
        "design tokens"
        [
            test "a scheme survives writing and reading back" {
                Expect.equal (DesignTokens.readTokens dark defaults.Light) defaults.Dark "the dark tokens, read over the light ones"
                Expect.equal (DesignTokens.readTokens light defaults.Dark) defaults.Light "the light tokens, read over the dark ones"
                Expect.equal (DesignTokens.readSyntax dark defaults.SyntaxLight) defaults.SyntaxDark "the dark syntax colours"
            }

            test "a token's path is its prefix, its kind and its CSS name" {
                Expect.equal ((token light "nacara.color.bg-subtle.$value").GetString()) "#f6f7f9" "a colour"
                Expect.equal ((token light "nacara.size.space-12.$value.unit").GetString()) "rem" "a length keeps its unit"
                Expect.equal ((token light "tok.color.keyword.$value").GetString()) "#a626a4" "a syntax colour"
            }

            test "a var() of another token is an alias with that token's kind" {
                Expect.equal ((token light "nacara.size.layout-gap.$value").GetString()) "{nacara.size.space-8}" "a length alias"
                Expect.equal ((token light "nacara.color.heading.$value").GetString()) "{nacara.color.text}" "a colour alias"
                Expect.equal ((token light "nacara.shadow.shadow-floating.$value").GetString()) "{nacara.shadow.shadow}" "a shadow alias"
            }

            test "line heights and weights are bare numbers" {
                Expect.equal ((token light "nacara.number.line-height.$value").GetDecimal()) 1.65m "a line height"
                Expect.equal ((token light "nacara.number.font-weight-heading.$type").GetString()) "fontWeight" "a weight"
                Expect.equal ((token light "nacara.size.font-size-2xl.$value.unit").GetString()) "rem" "a font size is a length"
            }

            test "a box-shadow is a shadow token, one entry per layer" {
                let layers = token light "nacara.shadow.shadow.$value"
                Expect.equal (layers.GetArrayLength()) 2 "two layers"
                Expect.equal (layers[0].GetProperty("color").GetString()) "#1018280f" "the layer's colour, as hex"
                Expect.equal (layers[1].GetProperty("blur").GetProperty("value").GetDecimal()) 12m "the blur"
                Expect.equal ((token light "nacara.shadow.shadow.$type").GetString()) "shadow" "typed as a shadow"
            }

            test "a shadow in rgb() is written as hex" {
                let shadowed = { defaults.Light with Shadow = "inset 0 0 0 1px rgb(255 0 0 / 50%)" }
                let written = DesignTokens.write shadowed defaults.SyntaxLight
                let layer = (token written "nacara.shadow.shadow.$value")[0]
                Expect.equal (layer.GetProperty("color").GetString()) "#ff000080" "the colour"
                Expect.isTrue (layer.GetProperty("inset").GetBoolean()) "inset"
                Expect.equal (DesignTokens.readTokens written defaults.Light).Shadow "inset 0 0 0 1px #ff000080" "and back"
            }

            test "a shadow no design tool can draw stays CSS" {
                let shadowed = { defaults.Light with Shadow = "var(--x, 0 1px red)" }
                let written = DesignTokens.write shadowed defaults.SyntaxLight
                Expect.equal ((token written "nacara.css.shadow.$value").GetString()) "var(--x, 0 1px red)" "kept as written"
            }

            test "a value with no design-tool type is kept as written" {
                Expect.equal ((token light "nacara.css.content-width.$value").GetString()) "75ch" "a length in ch"
                Expect.equal ((token light "nacara.css.navbar-opacity.$value").GetString()) "85%" "a percentage"
            }

            test "Figma's pixels come back in the theme's own unit" {
                let fromFigma =
                    """{ "nacara": { "size": {
                          "space-4": { "$type": "number", "$value": 24 },
                          "navbar-blur": { "$type": "dimension", "$value": { "value": 0.75, "unit": "rem" } } } } }"""

                let read = DesignTokens.readTokens fromFigma defaults.Light

                Expect.equal read.Space4 "1.5rem" "a bare number is pixels, written in the rem the theme uses"
                Expect.equal read.NavbarBlur "12px" "rem becomes the px the theme uses"
            }

            test "colours may arrive in the object form" {
                let fromFigma =
                    """{ "nacara": { "brand": {
                          "primary": { "$value": { "colorSpace": "srgb", "components": [1, 0, 0], "alpha": 1, "hex": "#FF0000" } },
                          "border": { "$value": { "colorSpace": "srgb", "components": [0, 0, 1], "alpha": 0.5 } } } } }"""

                let read = DesignTokens.readTokens fromFigma defaults.Light

                Expect.equal read.Primary "#ff0000" "the hex, regardless of the group it moved to"
                Expect.equal read.Border "#0000ff80" "the components, with the alpha"
            }

            test "what a document leaves out keeps its value" {
                let read = DesignTokens.readTokens """{ "nacara": { "color": { "primary": { "$value": "#c0392b" } } } }""" defaults.Light
                Expect.equal read { defaults.Light with Primary = "#c0392b" } "only primary changes"
            }

            test "an alias reads back as var()" {
                let read = DesignTokens.readTokens """{ "nacara": { "color": { "heading": { "$value": "{nacara.color.primary}" } } } }""" defaults.Light
                Expect.equal read.Heading "var(--nacara-primary)" "the alias, as CSS"
            }

            test "what the Figma plugin exports reads back as the theme's own tokens" {
                // figma-export.json is the plugin's export of the variables it imported from
                // design/tokens: every length in px, every alias kept, the shadows from their effect styles.
                use export =
                    Path.Combine(__SOURCE_DIRECTORY__, "figma-export.json") |> File.ReadAllText |> JsonDocument.Parse

                let scheme name = export.RootElement.GetProperty(name: string).GetRawText()

                Expect.equal (DesignTokens.readTokens (scheme "Light") defaults.Dark) defaults.Light "the light tokens"
                Expect.equal (DesignTokens.readTokens (scheme "Dark") defaults.Light) defaults.Dark "the dark tokens"
                Expect.equal (DesignTokens.readSyntax (scheme "Light") defaults.SyntaxDark) defaults.SyntaxLight "the light syntax"
                Expect.equal (DesignTokens.readSyntax (scheme "Dark") defaults.SyntaxLight) defaults.SyntaxDark "the dark syntax"
            }

            test "type, line heights and shadows edited in Figma come back as CSS" {
                // What the plugin exports after the page title is set to 40px, the body's line
                // height to 1.7 and the shadow to one layer.
                let fromFigma =
                    """{ "nacara": {
                          "size": { "font-size-2xl": { "$type": "dimension", "$value": { "value": 40, "unit": "px" } } },
                          "number": { "line-height": { "$type": "number", "$value": 1.7 },
                                      "font-weight-heading": { "$type": "fontWeight", "$value": { "value": 800, "unit": "px" } } },
                          "shadow": { "shadow": { "$type": "shadow", "$value": [
                            { "color": "#1018280f", "offsetX": { "value": 0, "unit": "px" }, "offsetY": { "value": 2, "unit": "px" },
                              "blur": { "value": 6, "unit": "px" }, "spread": { "value": 0, "unit": "px" } } ] } } } }"""

                let read = DesignTokens.readTokens fromFigma defaults.Light

                Expect.equal read.FontSize2xl "2.5rem" "the size, in the theme's rem"
                Expect.equal read.LineHeight "1.7" "a line height stays unitless"
                Expect.equal read.FontWeightHeading "800" "so does a weight, even sent as a length"
                Expect.equal read.Shadow "0 2px 6px #1018280f" "the shadow, as box-shadow"
            }

            test "a directory's documents apply to both schemes" {
                let directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
                Directory.CreateDirectory directory |> ignore

                try
                    File.WriteAllText(Path.Combine(directory, "dark.tokens.json"), """{ "nacara": { "color": { "primary": { "$value": "#c0392b" } } } }""")
                    let read = DesignTokens.fromDirectory directory defaults

                    Expect.equal read.Dark { defaults.Dark with Primary = "#c0392b" } "the dark document applies"
                    Expect.equal read.Light defaults.Light "a missing document leaves its scheme alone"
                    Expect.equal (DesignTokens.fromDirectory (Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "design", "tokens")) defaults) defaults "the checked-in documents are the defaults"
                finally
                    Directory.Delete(directory, true)
            }

            test "the checked-in documents match the defaults" {
                Expect.equal (checkedIn "light") light "design/tokens/light.tokens.json is stale; regenerate it"
                Expect.equal (checkedIn "dark") dark "design/tokens/dark.tokens.json is stale; regenerate it"
            }
        ]
