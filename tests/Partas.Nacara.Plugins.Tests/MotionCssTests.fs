module Partas.Nacara.Plugins.Tests.MotionCss

open System.IO
open System.Text.RegularExpressions
open Expecto
open Nacara.Plugins

let private document =
    match Path.Combine(__SOURCE_DIRECTORY__, "motion-sample.json") |> File.ReadAllText |> MotionDocument.parse with
    | Ok document -> document
    | Error message -> failwith message

let private hover name =
    document.Hovers |> List.find (fun hover -> hover.Name = name)

let private small = hover "card[size=small]"
let private smallSelector = "[data-motion=\"card\"][data-size=\"small\"]"

let private bare selector =
    { small with
        Selector = selector
        Changes = [ { Layer = None; Part = None; Property = "color"; From = Literal "#000"; To = Literal "#fff" } ] }

[<Tests>]
let tests =
    testList
        "motion css"
        [
            test "a token becomes its custom property" {
                Expect.equal (MotionCss.value (Token("nacara.color.bg-subtle", None))) "var(--nacara-bg-subtle)" "color"
                Expect.equal (MotionCss.value (Token("nacara.shadow.shadow", None))) "var(--nacara-shadow)" "shadow"
                Expect.equal (MotionCss.value (Literal "#f7f7ff")) "#f7f7ff" "literal"
            }

            test "a token with a fallback falls back to Figma's value" {
                Expect.equal
                    (MotionCss.value (Token("nacara.color.bg-subtle", Some "#f6f7f9")))
                    "var(--nacara-bg-subtle, #f6f7f9)"
                    "colour"

                Expect.equal
                    (MotionCss.value (Token("nacara.shadow.shadow", Some "0px 1px 2px 0px #1018280f, 0px 4px 12px 0px #1018280f")))
                    "var(--nacara-shadow, 0px 1px 2px 0px #1018280f, 0px 4px 12px 0px #1018280f)"
                    "shadow"
            }

            test "named curves become cubic-bezier" {
                Expect.equal
                    (MotionCss.easing (CubicBezier(0.0, 0.0, 0.58, 1.0)))
                    "cubic-bezier(0, 0, 0.58, 1)"
                    "ease out"
            }

            test "a spring becomes linear()" {
                let css = MotionCss.easing small.Easing
                Expect.isTrue (css.StartsWith "linear(0, ") css
            }

            test "a preset plays over 1200ms" {
                let h = { small with Easing = SpringPreset "GENTLE"; DurationMs = 300 }
                Expect.equal (MotionCss.duration h) 1200 "preset"
            }

            test "a custom spring plays over its settling time" {
                let parameters = { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }
                let h = { small with Easing = Spring parameters }
                Expect.equal (MotionCss.duration h) (Spring.settle parameters) "settle"
            }

            test "hover writes only the changed properties" {
                let css = MotionCss.hover small
                Expect.stringContains css $"{smallSelector}:hover, {smallSelector}:focus-visible {{" "pair"
                Expect.stringContains css "background-color: #f7f7ff;" "value"
                Expect.stringContains css "transition: background-color" "transition"

                let rule = css.Split('\n') |> Array.find (fun line -> line.Contains ":hover, ")
                Expect.isFalse (Regex.IsMatch(rule, "(^|[^-])color:")) "no color on the root"
            }

            test "a part gets its own rules" {
                let css = MotionCss.hover small
                Expect.stringContains css ":hover [data-motion-part=\"accent\"]" "hovered part"
                Expect.stringContains css "width: 80px;" "width"
            }

            test "a list selector is wrapped in :is()" {
                Expect.stringContains (MotionCss.hover (bare "a, b")) ":is(a, b):hover" "wrapped"
                let single = MotionCss.hover (bare ":is([data-active=\"true\"],[aria-current])")
                Expect.isFalse (single.Contains ":is(:is(") "one selector is not wrapped again"
            }

            test "Instant zeroes the transition under reduced motion" {
                let css = MotionCss.stylesheet Instant document.Hovers
                Expect.stringContains css "@media (prefers-reduced-motion: reduce)" "media"
                Expect.stringContains css "transition-duration: 0s" "zero"
            }

            test "Off keeps hovers out of reduced motion" {
                let css = MotionCss.stylesheet Off document.Hovers
                let media = "@media (prefers-reduced-motion: no-preference)"
                let hovers = Regex.Matches(css, ":hover") |> Seq.map (fun m -> m.Index) |> Seq.toList
                Expect.isNonEmpty hovers "has hovers"
                Expect.isTrue (hovers |> List.forall (fun i -> css.LastIndexOf(media, i) >= 0)) "every hover follows the media query"
            }

            test "Keep adds no media query" {
                Expect.isFalse ((MotionCss.stylesheet Keep document.Hovers).Contains "@media") "no media"
            }
        ]
