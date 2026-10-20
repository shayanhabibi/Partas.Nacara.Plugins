module Partas.Nacara.Plugins.Tests.MotionDocument

open System.IO
open Expecto
open Nacara.Plugins

let private sample =
    Path.Combine(__SOURCE_DIRECTORY__, "motion-sample.json") |> File.ReadAllText |> MotionDocument.parse

let private document =
    match sample with
    | Ok document -> document
    | Error message -> failwith message

let private hover name =
    document.Hovers |> List.find (fun hover -> hover.Name = name)

[<Tests>]
let tests =
    testList
        "motion document"
        [
            test "parses every hover of the sample" {
                Expect.isOk sample "the sample parses"
                Expect.equal document.Hovers.Length 5 "hovers"
                Expect.equal document.Skipped.Length 1 "skipped"
            }

            test "EASE_OUT is a cubic bezier" {
                Expect.equal (hover "navbar-link").Easing (CubicBezier(0.0, 0.0, 0.58, 1.0)) "ease out"
            }

            test "a custom spring keeps its parameters" {
                Expect.equal
                    (hover "card[size=small]").Easing
                    (Spring { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 })
                    "spring"
            }

            test "a preset keeps its name" {
                Expect.equal (hover "sidebar-link").Easing (SpringPreset "GENTLE") "preset"
            }

            test "a token and a literal are told apart" {
                let change = (hover "card[size=small]").Changes |> List.head
                Expect.equal change.From (Token "nacara.color.bg-raised") "token"
                Expect.equal change.To (Literal "#f7f7ff") "literal"
            }

            test "a part keeps its selector and layer" {
                let change = (hover "card[size=small]").Changes |> List.find (fun change -> change.Part.IsSome)
                Expect.equal change.Part (Some "[data-motion-part=\"accent\"]") "part"
                Expect.equal change.Layer (Some "Accent") "layer"
            }

            test "an unknown version is an error naming it" {
                match MotionDocument.parse """{"version":2,"hovers":[]}""" with
                | Error message ->
                    Expect.stringContains message "version 2" "names the version"
                    Expect.stringContains message "version 1" "names the supported one"
                | Ok _ -> failtest "expected an error"
            }

            test "a wrongly typed field is an error, not an exception" {
                for json in [ """{"version":1,"hovers":{}}"""; """{"version":"1","hovers":[]}"""; "[]"; """{"version":1,"hovers":[{"name":1}]}""" ] do
                    Expect.isError (MotionDocument.parse json) json
            }

            test "cubic bezier points are rounded to 3 decimals" {
                let json =
                    """{"version":1,"hovers":[{"name":"x","selector":".x","duration":100,"easing":{"type":"CUSTOM_CUBIC_BEZIER","x1":0.12345,"y1":0.5,"x2":0.6789,"y2":1},"changes":[]}]}"""

                match MotionDocument.parse json with
                | Ok document -> Expect.equal document.Hovers.Head.Easing (CubicBezier(0.123, 0.5, 0.679, 1.0)) "rounded"
                | Error message -> failtest message
            }

            test "an unknown easing is an error naming the hover" {
                let json =
                    """{"version":1,"hovers":[{"name":"x","selector":".x","trigger":"hover","duration":100,"easing":{"type":"WOBBLE"},"changes":[]}]}"""

                match MotionDocument.parse json with
                | Error message ->
                    Expect.stringContains message "x" "names the hover"
                    Expect.stringContains message "WOBBLE" "names the type"
                | Ok _ -> failtest "expected an error"
            }
        ]
