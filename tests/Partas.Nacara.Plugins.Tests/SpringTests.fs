module Partas.Nacara.Plugins.Tests.SpringSolver

open System.IO
open System.Text.Json
open Expecto
open Nacara.Plugins

type private Reference =
    { Parameters: SpringParameters
      SolverDuration: float
      SettlingDuration: int
      Samples: (float * float) list }

let private references =
    let json =
        Path.Combine(__SOURCE_DIRECTORY__, "anime-spring.json")
        |> File.ReadAllText
    use document = JsonDocument.Parse json

    [ for entry in document.RootElement.EnumerateArray() ->
          { Parameters =
              { Mass = entry.GetProperty("mass").GetDouble()
                Stiffness = entry.GetProperty("stiffness").GetDouble()
                Damping = entry.GetProperty("damping").GetDouble() }
            SolverDuration = entry.GetProperty("solverDuration").GetDouble()
            SettlingDuration = entry.GetProperty("settlingDuration").GetInt32()
            Samples =
              [ for sample in entry.GetProperty("samples").EnumerateArray() ->
                    sample.GetProperty("t").GetDouble(), sample.GetProperty("solve").GetDouble() ] } ]

let private values (css: string) =
    css.Substring("linear(".Length, css.Length - "linear(".Length - 1).Split(", ")
    |> Array.map float

[<Tests>]
let tests =
    testList
        "spring"
        [
            test "settling matches anime.js" {
                Expect.equal references.Length 7 "all reference springs"

                for reference in references do
                    Expect.equal
                        (Spring.settle reference.Parameters)
                        reference.SettlingDuration
                        $"%A{reference.Parameters}"
            }

            test "the curve matches anime.js" {
                for reference in references do
                    for t, solve in reference.Samples do
                        let ours = Spring.position reference.Parameters (reference.SolverDuration * t)
                        Expect.isLessThan (abs (ours - solve)) 1e-9 $"%A{reference.Parameters} at %f{t}"
            }

            test "linear() has 32 samples from 0 to 1" {
                let css = Spring.linear { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }
                Expect.isTrue (css.StartsWith "linear(0, ") css
                Expect.isTrue (css.EndsWith ", 1)") css
                Expect.equal (values css).Length 32 "samples"
            }

            test "a bouncy spring overshoots in linear()" {
                let css = Spring.linear { Mass = 1.0; Stiffness = 600.0; Damping = 15.0 }
                Expect.isTrue (values css |> Array.exists (fun value -> value > 1.0)) css
            }

            test "presets are the stand-ins" {
                Expect.equal (Spring.preset "GENTLE") (Some { Mass = 1.0; Stiffness = 100.0; Damping = 15.0 }) "gentle"
                Expect.equal (Spring.preset "QUICK") (Some { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }) "quick"
                Expect.equal (Spring.preset "BOUNCY") (Some { Mass = 1.0; Stiffness = 600.0; Damping = 15.0 }) "bouncy"
                Expect.equal (Spring.preset "SLOW") (Some { Mass = 1.0; Stiffness = 80.0; Damping = 20.0 }) "slow"
                Expect.equal (Spring.preset "NOPE") None "unknown"
            }

            test "a light spring is rescaled for anime.js" {
                let light = { Mass = 0.5; Stiffness = 100.0; Damping = 10.0 }
                let rescaled = Spring.forAnime light
                Expect.equal rescaled { Mass = 1.0; Stiffness = 200.0; Damping = 20.0 } "rescaled"
                Expect.equal (Spring.settle rescaled) (Spring.settle light) "settle equally"
            }
        ]
