module Partas.Nacara.Plugins.Tests.MotionOptions

open System.IO
open Expecto
open Nacara.Plugins

let private document =
    match Path.Combine(__SOURCE_DIRECTORY__, "motion-sample.json") |> File.ReadAllText |> MotionDocument.parse with
    | Ok document -> document
    | Error message -> failwith message

let private compile options =
    Motion.compile (options Motion.defaults) document

let private ok result =
    match result with
    | Ok compiled -> compiled
    | Error message -> failwith message

let private small = "card[size=small]"
let private smallSelector = "[data-motion=\"card\"][data-size=\"small\"]"

[<Tests>]
let tests =
    testList
        "motion options"
        [
            test "defaults compile every hover to CSS" {
                let compiled = compile id |> ok
                Expect.isEmpty compiled.AnimeHovers "no anime"

                for hover in document.Hovers do
                    Expect.stringContains compiled.Css hover.Selector hover.Name
            }

            test "a duration override wins" {
                let compiled = compile (Motion.duration "card[size=large]" 400) |> ok
                Expect.stringContains compiled.Css "400ms" "duration"
            }

            test "a value override replaces the after value" {
                let compiled = compile (Motion.value small "Accent/width" "120px") |> ok
                Expect.stringContains compiled.Css "width: 120px;" "new"
                Expect.isFalse (compiled.Css.Contains "width: 80px;") "old"
            }

            test "a disabled hover writes nothing" {
                let compiled = compile (Motion.disable "navbar-link") |> ok
                Expect.isFalse (compiled.Css.Contains "nacara-navbar__link") "gone"
            }

            test "a hover switched to anime.js leaves the CSS" {
                let compiled = compile (Motion.hoverEngine small Anime) |> ok
                Expect.equal (compiled.AnimeHovers |> List.map (fun h -> h.Name)) [ small ] "anime"
                Expect.isFalse (compiled.Css.Contains smallSelector) "not css"
            }

            test "an unknown hover name fails naming the known ones" {
                match compile (Motion.duration "cards" 1) with
                | Error message ->
                    Expect.stringContains message "\"cards\"" "name"
                    Expect.stringContains message "\"card[size=small]\"" "known"
                | Ok _ -> failtest "expected an error"
            }

            test "an unknown value key fails naming the keys" {
                match compile (Motion.value small "Nope/width" "1px") with
                | Error message -> Expect.stringContains message "Accent/width" "keys"
                | Ok _ -> failtest "expected an error"
            }

            test "not translated and presets become warnings" {
                let warnings = (compile id |> ok).Warnings |> String.concat "\n"
                Expect.stringContains warnings "Title/text is not translated" "text"
                Expect.stringContains warnings "GENTLE spring preset is approximated" "preset"
            }

            test "missing document fails with the resolved path" {
                match Motion.load (Motion.document "nowhere/motion.json" Motion.defaults) with
                | Error message ->
                    Expect.stringContains message (Path.GetFullPath "nowhere/motion.json") "path"
                    Expect.stringContains message "Export motion" "how"
                | Ok _ -> failtest "expected an error"
            }
        ]
