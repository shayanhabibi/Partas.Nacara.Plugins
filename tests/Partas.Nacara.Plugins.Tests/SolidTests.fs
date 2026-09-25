module Partas.Nacara.Plugins.Tests.Solid

open Expecto
open Nacara.Plugins
open Nacara.Plugins.Internal

let private fence (info: string) (code: string) = $"```fsharp %s{info}\n%s{code}\n```"

let private scan (body: string) = SolidScan.scan "solid" false "pkey" body

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

                    test "jsx adds a panel under the placeholder, and leaves the fence" {
                        let result = scan (fence "solid jsx" "Counter ()")
                        Expect.isTrue (List.exactlyOne result.Cells).Jsx "marked"
                        Expect.stringContains result.Body "```fsharp\nCounter ()\n```" "code"
                        let placeholder = result.Body.IndexOf "data-partas-cell=\"c1\""
                        let panel = result.Body.IndexOf "data-partas-jsx-page=\"pkey\" data-partas-jsx-cell=\"c1\""
                        Expect.isGreaterThan panel placeholder "panel after placeholder"
                    }

                    test "without jsx there is no panel" {
                        let result = scan (fence "solid" "Counter ()")
                        Expect.isFalse (List.exactlyOne result.Cells).Jsx "marked"
                        Expect.isFalse (result.Body.Contains "partas-solid__jsx") "panel"
                    }

                    test "showJsx marks every fence but setup" {
                        let body = fence "solid" "A ()" + "\n" + fence "solid setup" "let x = 1"
                        let result = SolidScan.scan "solid" true "pkey" body
                        Expect.equal (result.Cells |> List.map _.Jsx) [ true; false ] "marked"
                    }

                    test "show=code still gets its panel" {
                        let result = scan (fence "solid show=code jsx" "Counter ()")
                        Expect.stringContains result.Body "```\n\n<details class=\"partas-solid__jsx\"" "panel"
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
                "jsx"
                [
                    let jsx =
                        String.concat
                            "\n"
                            [
                                "import { createSignal } from \"solid-js\";"
                                ""
                                "export function Counter() {"
                                "    return <button>"
                                "        {count()}"
                                "    </button>;"
                                "}"
                                ""
                                "export class Todo extends Record {"
                                "    constructor(Id) {"
                                "        super();"
                                "    }"
                                "}"
                                ""
                                "export const limit = 3;"
                                ""
                                "export function Cell_c1() {"
                                "    return Counter();"
                                "}"
                                ""
                            ]

                    let parse (json: string) =
                        System.Text.Json.JsonSerializer.Deserialize<Map<string, string>> json

                    test "an expression shows its wrapper" {
                        Expect.equal (SolidGenerate.jsxNames (cell (fence "solid" "Counter ()"))) [ "Cell_c1" ] "names"
                    }

                    test "declarations show what they declare at column zero" {
                        let code =
                            "[<SolidComponent>]\nlet Counter () =\n    let inner = 1\n    inner\ntype private Todo = { Id: int }"

                        let names = SolidGenerate.jsxNames (cell (fence "solid render=Counter" code))
                        Expect.equal names [ "Counter"; "Todo" ] "names"
                    }

                    test "a function is cut out up to its closing brace" {
                        Expect.equal
                            (SolidGenerate.jsxDeclaration jsx "Counter")
                            (Some "export function Counter() {\n    return <button>\n        {count()}\n    </button>;\n}")
                            "Counter"
                    }

                    test "a class and a one-line const are cut out" {
                        Expect.stringStarts (Option.get (SolidGenerate.jsxDeclaration jsx "Todo")) "export class Todo" "class"
                        Expect.equal (SolidGenerate.jsxDeclaration jsx "limit") (Some "export const limit = 3;") "const"
                    }

                    test "a name is matched whole" {
                        Expect.isNone (SolidGenerate.jsxDeclaration jsx "Count") "prefix"
                    }

                    test "the JSON holds only the cells marked jsx" {
                        let cells = (scan (fence "solid jsx" "Counter ()" + "\n" + fence "solid" "B ()")).Cells
                        let parsed = parse (SolidGenerate.jsxJson cells jsx)
                        Expect.equal (parsed |> Map.keys |> List.ofSeq) [ "c1" ] "keys"
                        Expect.stringContains parsed["c1"] "return Counter();" "c1"
                    }

                    test "declarations Fable left no trace of fall back to the wrapper" {
                        let cells = (scan (fence "solid render=Counter jsx" "let private helper = 1")).Cells
                        let parsed = parse (SolidGenerate.jsxJson cells jsx)
                        Expect.stringStarts parsed["c1"] "export function Cell_c1" "wrapper"
                    }

                    test "marking a cell jsx changes the fingerprint" {
                        let plain = unitOf (scan (fence "solid" "Counter ()")).Cells
                        let marked = unitOf (scan (fence "solid jsx" "Counter ()")).Cells
                        let options = SolidExamples.defaults ()

                        Expect.notEqual
                            (SolidCompile.fingerprint options [ plain ])
                            (SolidCompile.fingerprint options [ marked ])
                            "fingerprint"
                    }
                ]

            testList
                "inline"
                [
                    test "show=inline mounts without the code, in an unboxed div" {
                        let result = scan (fence "solid show=inline" "p () { \"hi\" }")
                        let found = List.exactlyOne result.Cells
                        Expect.equal found.Show SolidShow.Inline "show"
                        Expect.isTrue found.Mounts "mounts"
                        Expect.isFalse (result.Body.Contains "```") "no fence"
                        Expect.stringContains result.Body "<div class=\"partas-solid partas-solid--inline\"" "placeholder"
                    }

                    test "a use in prose becomes a span where it stood" {
                        let result = scan "Press `solid: Kbd \"Ctrl\"` to copy."
                        let found = List.exactlyOne result.Cells
                        Expect.equal found.Kind (SolidCellKind.Use 15) "kind"
                        Expect.equal found.Code "Kbd \"Ctrl\"" "code"
                        Expect.equal found.Id "u1" "id"
                        Expect.isTrue found.Mounts "mounts"

                        Expect.equal
                            result.Body
                            "Press <span class=\"partas-solid partas-solid--inline\" data-partas-page=\"pkey\" data-partas-cell=\"u1\"></span> to copy."
                            "body"
                    }

                    test "several uses on a line are each found" {
                        let found = SolidScan.uses "solid" "`solid: A ()` and ``solid: B `x` ()`` then `solid:C()`"
                        Expect.equal (found |> List.map _.Code) [ "A ()"; "B `x` ()"; "C()" ] "codes"
                        Expect.equal (found |> List.map _.Column) [ 9; 28; 51 ] "columns"
                    }

                    test "plain code spans are left alone" {
                        let body = "Write `solid` or `Kbd \"x\"` or `solidly: x` or \`solid: x`."
                        let result = scan body
                        Expect.isEmpty result.Cells "cells"
                        Expect.equal result.Body body "body"
                    }

                    test "a space before the token shows the syntax as code" {
                        let body = "Write `` `solid: Kbd \"x\"` `` in the prose."
                        let result = scan body
                        Expect.isEmpty result.Cells "cells"
                        Expect.equal result.Body body "body"
                    }

                    test "a use inside a fence is code, not a use" {
                        let body = fence "" "let s = \"`solid: A ()`\""
                        let result = scan body
                        Expect.isEmpty result.Cells "cells"
                        Expect.equal result.Body body "body"
                    }

                    test "a use takes the token the site set" {
                        let result = SolidScan.scan "live" false "pkey" "`live: A ()` but not `solid: B ()`"
                        Expect.equal (result.Cells |> List.map _.Code) [ "A ()" ] "codes"
                    }

                    test "showJsx leaves uses without a panel" {
                        let result = SolidScan.scan "solid" true "pkey" "Press `solid: Kbd \"C\"` now."
                        Expect.isFalse (List.exactlyOne result.Cells).Jsx "jsx"
                        Expect.isFalse (result.Body.Contains "<details") "no panel"
                    }

                    test "uses do not shift the numbering of fences" {
                        let result = scan ("`solid: A ()`\n\n" + fence "solid" "B ()")
                        Expect.equal (result.Cells |> List.map _.Id) [ "u1"; "c1" ] "ids"
                    }

                    test "the token alone names the syntax and stays as written" {
                        let result = scan "x `solid:` or `solid: ` y"
                        Expect.isEmpty result.Cells "cells"
                        Expect.isEmpty result.Problems "problems"
                        Expect.equal result.Body "x `solid:` or `solid: ` y" "body"
                    }

                    test "a use is wrapped after every fence, so it can come first" {
                        let cells = (scan ("`solid: Badge ()`\n\n" + fence "solid" "let Badge () = span () { }")).Cells
                        let unit = unitOf cells
                        Expect.isLessThan (lineOf "let Badge" unit.Code) (lineOf "let Cell_u1" unit.Code) "order"
                        Expect.stringContains unit.Code "let Cell_u1 () =\n    Badge ()" "wrapper"
                        Expect.stringContains (SolidGenerate.entry "pkey" cells) "\"u1\": m.Cell_u1," "entry"
                    }

                    test "an error in a use points at its line and column" {
                        let unit = unitOf (scan "text\nSee `solid: Badge ()` here.").Cells
                        let line = lineOf "    Badge ()" unit.Code
                        // Badge starts at column 5 of the generated line and column 13 of the prose.
                        Expect.equal (SolidGenerate.locate unit.Spans line 5) (Some(2, 13)) "start"
                        Expect.equal (SolidGenerate.locate unit.Spans line 11) (Some(2, 19)) "later"
                        Expect.equal (SolidGenerate.locate unit.Spans (lineOf "let Cell_u1" unit.Code) 5) (Some(2, 1)) "wrapper"
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

            testList
                "fable watch"
                [
                    let started = "Started Fable compilation..."
                    let watching = "Watching ."
                    let first = [ "Parsing Docs.fsproj..."; started; "Fable compilation finished in 6922ms"; watching ]

                    test "a change made while idle waits for a compilation of its own" {
                        Expect.isNone (SolidFableCycle.finished first first.Length) "nothing yet"
                    }

                    test "the compilation a change started is returned once it finishes" {
                        let lines = first @ [ started; "an error"; watching ]
                        let cycle = SolidFableCycle.finished lines first.Length
                        Expect.equal cycle (Some [ started; "an error"; watching ]) "cycle"
                    }

                    test "a compilation still running is waited for" {
                        let lines = first @ [ started; "an error" ]
                        Expect.isNone (SolidFableCycle.finished lines first.Length) "running"
                    }

                    test "a change made mid-compilation may be folded into it" {
                        let mark = 2
                        let cycle = SolidFableCycle.finished first mark
                        Expect.equal cycle (Some [ "Fable compilation finished in 6922ms"; watching ]) "cycle"
                    }

                    test "a compilation queued after the first is waited for" {
                        let lines = first @ [ started ]
                        Expect.isNone (SolidFableCycle.finished lines 2) "queued"
                    }

                    test "of several finished compilations, the last is returned" {
                        let lines = first @ [ started; "old error"; watching; started; "new error"; watching ]
                        let cycle = SolidFableCycle.finished lines first.Length
                        Expect.equal cycle (Some [ started; "new error"; watching ]) "cycle"
                    }
                ]
        ]
