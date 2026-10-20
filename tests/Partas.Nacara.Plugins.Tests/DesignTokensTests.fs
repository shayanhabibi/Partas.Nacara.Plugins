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
                Expect.equal ((token light "nacara.css.shadow-floating.$value").GetString()) "{nacara.css.shadow}" "an alias to plain CSS"
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
                // design/tokens: every length in px, every alias kept.
                use export =
                    Path.Combine(__SOURCE_DIRECTORY__, "figma-export.json") |> File.ReadAllText |> JsonDocument.Parse

                let scheme name = export.RootElement.GetProperty(name: string).GetRawText()

                Expect.equal (DesignTokens.readTokens (scheme "Light") defaults.Dark) defaults.Light "the light tokens"
                Expect.equal (DesignTokens.readTokens (scheme "Dark") defaults.Light) defaults.Dark "the dark tokens"
                Expect.equal (DesignTokens.readSyntax (scheme "Light") defaults.SyntaxDark) defaults.SyntaxLight "the light syntax"
                Expect.equal (DesignTokens.readSyntax (scheme "Dark") defaults.SyntaxLight) defaults.SyntaxDark "the dark syntax"
            }

            test "the checked-in documents match the defaults" {
                Expect.equal (checkedIn "light") light "design/tokens/light.tokens.json is stale; regenerate it"
                Expect.equal (checkedIn "dark") dark "design/tokens/dark.tokens.json is stale; regenerate it"
            }
        ]
