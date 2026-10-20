module Partas.Nacara.Plugins.Tests.MotionOptions

open System.IO
open Expecto
open Nacara.Core
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
                let compiled = compile (Motion.duration "card[size=large]" 437) |> ok
                Expect.stringContains compiled.Css "437ms" "duration"
            }

            test "a duration on a spring hover fails" {
                match compile (Motion.duration small 300) with
                | Error message ->
                    Expect.stringContains message $"\"%s{small}\"" "name"
                    Expect.stringContains message "settling time" "why"
                    Expect.stringContains message "Motion.easing" "fix"
                | Ok _ -> failtest "expected an error"
            }

            test "a duration on a preset hover fails" {
                match compile (Motion.duration "sidebar-link" 300) with
                | Error message ->
                    Expect.stringContains message "\"sidebar-link\"" "name"
                    Expect.stringContains message "1200ms" "why"
                | Ok _ -> failtest "expected an error"
            }

            test "a duration on a spring hover eased by a bezier wins" {
                let compiled =
                    compile (Motion.easing small (CubicBezier(0.2, 0.0, 0.0, 1.0)) >> Motion.duration small 333) |> ok

                Expect.stringContains compiled.Css "333ms" "duration"
            }

            test "a duration on a bezier hover eased by a spring fails" {
                let spring = Easing.Spring { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }

                match compile (Motion.easing "card[size=large]" spring >> Motion.duration "card[size=large]" 300) with
                | Error message -> Expect.stringContains message "settling time" "why"
                | Ok _ -> failtest "expected an error"
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

            test "a misspelt spring preset fails naming the four" {
                match compile (Motion.easing small (SpringPreset "GENTEL")) with
                | Error message ->
                    Expect.stringContains message $"\"%s{small}\"" "name"
                    Expect.stringContains message "\"GENTEL\"" "the misspelling"

                    for preset in [ "GENTLE"; "QUICK"; "BOUNCY"; "SLOW" ] do
                        Expect.stringContains message $"\"%s{preset}\"" preset
                | Ok _ -> failtest "expected an error"
            }

            test "a document with two hovers of one name fails naming it" {
                let twice = { document with Hovers = document.Hovers @ [ document.Hovers.Head ] }

                match Motion.compile Motion.defaults twice with
                | Error message -> Expect.stringContains message $"2 hovers named \"%s{document.Hovers.Head.Name}\"" "name"
                | Ok _ -> failtest "expected an error"
            }

            test "missing document fails with the resolved path" {
                match Motion.load (Motion.document "nowhere/motion.json" Motion.defaults) with
                | Error message ->
                    Expect.stringContains message (Path.GetFullPath "nowhere/motion.json") "path"
                    Expect.stringContains message "Export motion" "how"
                | Ok _ -> failtest "expected an error"
            }
        ]

let private hover easing changes =
    {
        Name = "t"
        Selector = "[data-motion=\"t\"]"
        Trigger = "ON_HOVER"
        DurationMs = 200
        Easing = easing
        Changes = changes
        NotTranslated = []
    }

let private widen to' =
    {
        Layer = None
        Part = None
        Property = "width"
        From = Literal "40px"
        To = to'
    }

[<Tests>]
let scriptTests =
    testList
        "motion script"
        [
            test "bundled entry imports only what it uses" {
                let script =
                    MotionScript.entry Bundled Instant [ hover (CubicBezier(0.4, 0.0, 0.2, 1.0)) [ widen (Literal "80px") ] ]

                Expect.stringContains script "cubicBezier" "bezier"
                Expect.stringContains script "animate" "animate"
                Expect.isFalse (script.Contains "createAnimatable") "no animatable"
                Expect.isFalse (script.Contains "spring") "no spring"
                Expect.stringContains script "from \"./anime.esm.js\"" "bundled import"
            }

            test "cdn entry has no static import" {
                let script =
                    MotionScript.entry (Cdn "https://x/anime.js") Instant [ hover Linear [ widen (Literal "80px") ] ]

                Expect.isFalse (script.Contains "import {") "no static import"
                Expect.stringContains script "await import(" "dynamic import"
                Expect.stringContains script "https://x/anime.js" "url"
            }

            test "anime script resolves tokens when the hover starts" {
                let script =
                    MotionScript.entry
                        Bundled
                        Instant
                        [ hover Linear [ widen (Token("nacara.color.bg-subtle", None)) ] ]

                Expect.stringContains script "getPropertyValue(\"--nacara-bg-subtle\")" "resolved"
                Expect.isFalse (script.Contains "var(--nacara-bg-subtle)") "not var()"
            }

            test "anime script falls back to Figma's value when the site has no such token" {
                let script =
                    MotionScript.entry Bundled Instant [ hover Linear [ widen (Token("nacara.color.bg-subtle", Some "#f6f7f9")) ] ]

                Expect.stringContains
                    script
                    "(host) => getComputedStyle(host).getPropertyValue(\"--nacara-bg-subtle\").trim() || \"#f6f7f9\""
                    "fallback"
            }

            test "a bundled script carries anime.js's licence notice" {
                let script = MotionScript.entry Bundled Instant [ hover Linear [ widen (Literal "80px") ] ]

                let copyright =
                    File.ReadAllLines(Path.Combine(__SOURCE_DIRECTORY__, "../../src/Partas.Nacara.Plugins.Motion/vendor/LICENSE.anime.md"))
                    |> Array.find (fun line -> line.StartsWith "Copyright")

                Expect.equal
                    (script.Substring(0, script.IndexOf '\n'))
                    $"/*! anime.js v4.5.0 | MIT License | %s{copyright.Trim()} */"
                    "the first line"

                let cdn = MotionScript.entry (Cdn Motion.CdnUrl) Instant [ hover Linear [ widen (Literal "80px") ] ]
                Expect.isFalse (cdn.Contains "/*!") "a CDN copy carries its own"
            }

            test "a preset plays CSS's samples over 1200ms, not anime.js's spring" {
                let script = MotionScript.entry Bundled Instant [ hover (SpringPreset "GENTLE") [ widen (Literal "80px") ] ]
                let parameters = (Spring.preset "GENTLE").Value
                let samples = (Spring.linear parameters).Substring("linear(".Length).TrimEnd(')')
                Expect.stringContains script $"ease: sampled([%s{samples}])" "samples"
                Expect.stringContains script "duration: 1200," "1200ms"
                Expect.isFalse (script.Contains "spring") "no spring"
            }

            test "rotate is tweened as the CSS property, not as a transform" {
                let turn =
                    { Layer = None; Part = None; Property = "rotate"; From = Literal "0deg"; To = Literal "10deg" }

                let script = MotionScript.entry Bundled Keep [ hover Linear [ turn ] ]
                Expect.stringContains script "rotate: \"--nacara-motion-rotate\"" "through a custom property"
                Expect.stringContains script "params[key(property)]" "keyed"
                Expect.stringContains script "\"0$1\"" "every unit is zeroed"
            }

            test "a light spring reaches anime.js rescaled" {
                let light = Easing.Spring { Mass = 0.5; Stiffness = 100.0; Damping = 10.0 }
                let script = MotionScript.entry Bundled Instant [ hover light [ widen (Literal "80px") ] ]
                Expect.stringContains script "stiffness: 200" "stiffness"
                Expect.stringContains script "damping: 20" "damping"
            }

            test "reduced motion is honoured in the script" {
                let script = MotionScript.entry Bundled Instant [ hover Linear [ widen (Literal "80px") ] ]
                Expect.stringContains script "prefers-reduced-motion: reduce" "media query"
            }

            test "the vendored anime.js is 4.5.0" {
                Expect.stringContains (MotionScript.anime ()) "v4.5.0" "version"
            }
        ]

[<Tests>]
let pluginTests =
    testList
        "motion plugin"
        [
            test "an anime hover registers a bundled script and its page asset" {
                let path = Path.Combine(Path.GetTempPath(), $"motion-{System.Guid.NewGuid():N}.json")
                File.Copy(Path.Combine(__SOURCE_DIRECTORY__, "motion-sample.json"), path)

                try
                    let plugin =
                        Motion.createWith (Motion.document path >> Motion.hoverEngine "card[size=small]" Anime)

                    let registry = plugin.Configure Registry.empty

                    let destination =
                        match registry.Assets with
                        | [ Bundle(sources, entry, destination) ] ->
                            Expect.equal (sources |> List.map fst) [ "motion.js"; "anime.esm.js" ] "sources"
                            Expect.equal entry "motion.js" "entry"
                            RelativePath.value destination
                        | other -> failtestf "expected one bundle, got %A" other

                    Expect.isTrue
                        (System.Text.RegularExpressions.Regex.IsMatch(destination, "^assets/motion\.[0-9a-f]{8}\.js$"))
                        destination

                    Expect.equal (Registry.extras<PageAsset> registry) [ Script(destination, true) ] "page script"
                finally
                    File.Delete path
            }

            test "the script plays focus-visible and gives properties back" {
                let script = MotionScript.entry Bundled Instant [ hover Linear [ widen (Literal "80px") ] ]
                Expect.stringContains script ":focus-visible" "focus-visible"
                Expect.stringContains script "removeProperty" "inline styles removed"
                Expect.stringContains script "composition: \"replace\"" "replace composition"
            }
        ]

[<Tests>]
let reducedTests =
    testList
        "motion reduced"
        [
            test "an instant duration drops a spring ease" {
                let light = Easing.Spring { Mass = 1.0; Stiffness = 100.0; Damping = 10.0 }
                let script = MotionScript.entry Bundled Instant [ hover light [ widen (Literal "80px") ] ]
                Expect.stringContains script "duration() === 0 ? \"linear\" : hover.ease" "linear when instant"
            }

            test "a border radius is tweened as four corners" {
                let script = MotionScript.entry Bundled Keep [ hover Linear [ widen (Literal "4px 8px") ] ]
                Expect.stringContains script "border-${corner}-radius" "longhands"
            }
        ]
