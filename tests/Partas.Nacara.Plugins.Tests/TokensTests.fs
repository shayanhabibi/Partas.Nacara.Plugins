module Partas.Nacara.Plugins.Tests.Tokens

open System.Text.RegularExpressions
open Expecto
open Partas.Nacara.Theme

/// <summary>The custom properties a stylesheet declares.</summary>
let private declared (css: string) =
    Regex.Matches(css, @"^\s*(--[a-z0-9-]+)\s*:", RegexOptions.Multiline)
    |> Seq.map (fun found -> found.Groups[1].Value)
    |> Set.ofSeq

/// <summary>The theme's own custom properties a stylesheet reads.</summary>
/// <remarks>
/// Only <c>--nacara-*</c> and <c>--tok-*</c>: a layer setting a property of its own and reading
/// it back in the same rule - <c>--callout-color</c> in the components layer - owes the tokens
/// nothing.
/// </remarks>
let private referenced (css: string) =
    Regex.Matches(css, @"var\(\s*(--(?:nacara|tok)-[a-z0-9-]+)")
    |> Seq.map (fun found -> found.Groups[1].Value)
    |> Set.ofSeq

let private layerNamed name =
    Theme.defaults.Styles
    |> List.find (fun layer -> layer.Name = name)
    |> fun layer -> layer.Css

let private tokensLayer = layerNamed "tokens"

[<Tests>]
let tests =
    testList
        "theme tokens"
        [
            test "every token the other layers read is declared" {
                let wanted =
                    Theme.defaults.Styles
                    |> List.filter (fun layer -> layer.Name <> "tokens")
                    |> List.map (fun layer -> referenced layer.Css)
                    |> Set.unionMany

                let missing = Set.difference wanted (declared tokensLayer)

                Expect.isEmpty
                    missing
                    $"""these are read by a layer but declared by nothing: %s{String.concat ", " missing}"""
            }

            test "the other layers read something, so the check above can fail" {
                let wanted =
                    Theme.defaults.Styles
                    |> List.filter (fun layer -> layer.Name <> "tokens")
                    |> List.map (fun layer -> referenced layer.Css)
                    |> Set.unionMany

                Expect.isGreaterThan (Set.count wanted) 30 "the layers reference the tokens by name"
            }

            test "a field's name becomes its property's name" {
                // The two shapes the spelling rule has to get right: a run of digits stays one
                // run, and an abbreviation is not one word per capital.
                Expect.stringContains tokensLayer "--nacara-space-12:" "Space12 is one digit run"
                Expect.stringContains tokensLayer "--nacara-radius-sm:" "RadiusSm is one word"
                Expect.stringContains tokensLayer "--nacara-primary-subtle:" "PrimarySubtle hyphenates"
                Expect.stringContains tokensLayer "--tok-keyword:" "the syntax colours take their own prefix"
            }

            test "the dark scheme declares only what it changes" {
                let dark = tokensLayer.Substring(tokensLayer.IndexOf ":root[data-theme=\"dark\"]")

                Expect.stringContains dark "--nacara-bg:" "a colour the dark scheme overrides"

                Expect.isFalse
                    (dark.Contains "--nacara-font-sans:")
                    "a value identical in both schemes is not written twice"
            }

            test "a changed token reaches the stylesheet" {
                let changed = Theme.defaults |> Theme.lightTokens (fun tokens -> { tokens with Primary = "#c0392b" })

                Expect.stringContains
                    (changed.Styles |> List.find (fun layer -> layer.Name = "tokens") |> _.Css)
                    "--nacara-primary: #c0392b;"
                    "the tokens layer is rewritten, not just the record"

                Expect.equal changed.Tokens.Light.Primary "#c0392b" "and the record remembers it"
            }

            test "a changed token leaves the rest of the scheme alone" {
                let changed = Theme.defaults |> Theme.darkTokens (fun tokens -> { tokens with Primary = "#e74c3c" })

                Expect.equal
                    changed.Tokens.Light.Primary
                    Theme.defaults.Tokens.Light.Primary
                    "the light scheme is untouched"

                Expect.stringContains
                    (changed.Styles |> List.find (fun layer -> layer.Name = "tokens") |> _.Css)
                    "--nacara-primary: #e74c3c;"
                    "the dark override is written"
            }
        ]
