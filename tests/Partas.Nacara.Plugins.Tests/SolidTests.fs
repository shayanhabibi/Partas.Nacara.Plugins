module Partas.Nacara.Plugins.Tests.Solid

open Expecto
open Nacara.Plugins
open Nacara.Plugins.Internal

let private fence (info: string) (code: string) = $"```fsharp %s{info}\n%s{code}\n```"

let private scan (body: string) = SolidScan.scan "solid" "pkey" body

let private cell (body: string) = List.exactlyOne (scan body).Cells

let private unitOf (cells: SolidCell list) =
    let code, spans = SolidGenerate.fsharp [ "open Partas.Solid" ] "pkey" cells

    {
        Key = "pkey"
        Source = Some "page.md"
        BodyLine = 1
        Cells = cells
        Code = code
        Spans = spans
    }

/// <summary>The generated line that holds <c>text</c>, counting from one.</summary>
let private lineOf (text: string) (code: string) =
    code.Split('\n') |> Array.findIndex (fun line -> line.Contains text) |> (+) 1

[<Tests>]
let tests =
    testList
        "solid examples"
        [
            testList
                "scan"
                [
                    test "an expression mounts" {
                        let found = cell (fence "solid" "Counter ()")
                        Expect.equal found.Kind SolidCellKind.Expression "kind"
                        Expect.isTrue found.Mounts "mounts"
                    }

                    test "declarations mount nothing without render=" {
                        let found = cell (fence "solid" "let x = 1")
                        Expect.equal found.Kind (SolidCellKind.Declarations None) "kind"
                        Expect.isFalse found.Mounts "mounts"
                    }

                    test "bindings that lead to an expression are an expression" {
                        let found = cell (fence "solid" "let x = 1\ndiv () { string x }")
                        Expect.equal found.Kind SolidCellKind.Expression "kind"
                    }

                    test "a closing bracket at column zero does not decide" {
                        let found = cell (fence "solid" "let xs = [\n    1\n]")
                        Expect.equal found.Kind (SolidCellKind.Declarations None) "kind"
                    }

                    test "render= names the component to mount" {
                        let found = cell (fence "solid render=Counter" "let Counter () = div () { }")
                        Expect.equal found.Kind (SolidCellKind.Declarations(Some "Counter")) "kind"
                        Expect.isTrue found.Mounts "mounts"
                    }

                    test "a plain fsharp fence is left alone" {
                        let body = fence "" "let x = 1"
                        let result = scan body
                        Expect.isEmpty result.Cells "cells"
                        Expect.equal result.Body body "body"
                    }

                    test "the fence keeps its code and gains a placeholder" {
                        let result = scan (fence "solid id=hello" "Counter ()")
                        Expect.stringContains result.Body "```fsharp\nCounter ()\n```" "code"
                        Expect.stringContains result.Body "data-partas-page=\"pkey\" data-partas-cell=\"hello\"" "placeholder"
                        Expect.isFalse (result.Body.Contains "```fsharp solid") "token removed"
                    }

                    test "setup leaves blank lines, so later lines keep their place" {
                        let body = fence "solid setup" "type T = { A: int }" + "\nafter"
                        let result = scan body
                        Expect.equal (result.Body.Split('\n').Length) (body.Split('\n').Length) "line count"
                        Expect.isFalse (result.Body.Contains "type T") "hidden"
                    }

                    test "show=output hides the code but mounts" {
                        let result = scan (fence "solid show=output" "p () { \"hi\" }")
                        Expect.isFalse (result.Body.Contains "```") "no fence"
                        Expect.stringContains result.Body "data-partas-cell=\"c1\"" "placeholder"
                    }

                    test "show=code compiles but does not mount" {
                        Expect.isFalse (cell (fence "solid show=code" "Counter ()")).Mounts "mounts"
                    }

                    test "cells are numbered and start on the line after the fence" {
                        let result = scan (fence "solid" "A ()" + "\n\n" + fence "solid" "B ()")
                        Expect.equal (result.Cells |> List.map _.Id) [ "c1"; "c2" ] "ids"
                        Expect.equal (result.Cells |> List.map _.Line) [ 2; 6 ] "lines"
                    }

                    test "a bad show= and a repeated id are problems" {
                        let result = scan (fence "solid show=nope id=a" "A ()" + "\n" + fence "solid id=a" "B ()")
                        Expect.equal (result.Problems |> List.map fst) [ 1; 4 ] "lines"
                    }
                ]

            testList
                "generate"
                [
                    test "an expression is wrapped in a component, indented" {
                        let unit = unitOf (scan (fence "solid" "Counter ()")).Cells
                        Expect.stringContains unit.Code "let Cell_c1 () =\n    Counter ()" "wrapper"
                    }

                    test "render= adds a wrapper that calls the component" {
                        let unit = unitOf (scan (fence "solid render=Counter" "let Counter () = 1")).Cells
                        Expect.stringContains unit.Code "let Cell_c1 () = Counter ()" "wrapper"
                    }

                    test "a line of a cell maps back to its body line and column" {
                        let unit = unitOf (scan ("text\n" + fence "solid" "div () {\n    Counter ()\n}")).Cells
                        let line = lineOf "    Counter ()" unit.Code
                        // Four columns of wrapper indent come off; the body line is the fence's third.
                        Expect.equal (SolidGenerate.locate unit.Spans line 9) (Some(4, 5)) "located"
                    }

                    test "a wrapper line is blamed on the fence line, column one" {
                        let unit = unitOf (scan (fence "solid" "Counter ()")).Cells
                        let line = lineOf "let Cell_c1" unit.Code
                        Expect.equal (SolidGenerate.locate unit.Spans line 5) (Some(1, 1)) "located"
                    }

                    test "prelude lines map nowhere" {
                        let unit = unitOf (scan (fence "solid" "Counter ()")).Cells
                        Expect.isNone (SolidGenerate.locate unit.Spans (lineOf "open Partas.Solid" unit.Code) 1) "prelude"
                    }

                    test "the entry mounts only the cells that mount" {
                        let cells = (scan (fence "solid" "A ()" + "\n" + fence "solid setup" "let x = 1")).Cells
                        let entry = SolidGenerate.entry "pkey" cells
                        Expect.stringContains entry "\"c1\": m.Cell_c1," "c1"
                        Expect.isFalse (entry.Contains "Cell_c2") "c2"
                        Expect.stringContains entry "../Pkey.fs.jsx" "module"
                    }
                ]

            testList
                "fable messages"
                [
                    let unit = unitOf (scan (fence "solid" "Counter ()")).Cells
                    let line = lineOf "    Counter ()" unit.Code
                    let file = SolidGenerate.fileName unit.Key

                    test "an error in a cell is placed on the page" {
                        let output =
                            $"C:\\ws\\%s{file}(%d{line},5): (%d{line},12) error FSHARP: The value 'Counter' is not defined. (code 39)"

                        let messages = SolidCompile.fableMessages [ unit ] output
                        let message = List.exactlyOne messages
                        Expect.isTrue message.IsError "error"
                        Expect.equal message.At (Some(2, 1)) "at"
                        Expect.equal message.Message "The value 'Counter' is not defined." "message"
                    }

                    test "a warning from a generated line is dropped" {
                        let output = $"C:\\ws\\%s{file}(1,1): (1,5) warning FSHARP: Something generated. (code 1)"
                        Expect.isEmpty (SolidCompile.fableMessages [ unit ] output) "dropped"
                    }

                    test "an error from elsewhere is kept, unplaced" {
                        let output = "C:\\ws\\Other.fs(3,1): (3,5) error FSHARP: Broken. (code 1)"
                        let message = List.exactlyOne (SolidCompile.fableMessages [ unit ] output)
                        Expect.isNone message.Page "page"
                        Expect.isNone message.At "at"
                    }
                ]
        ]
