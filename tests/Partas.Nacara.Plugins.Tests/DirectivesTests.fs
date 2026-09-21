module Partas.Nacara.Plugins.Tests.Directives

open Expecto
open Nacara.Plugins

let private mapping input = Arguments.toFlowMapping input

[<Tests>]
let tests =
    testList "directives" [
        testList "argument tokeniser" [
            test "no arguments is an empty mapping" {
                Expect.equal (mapping "") (Ok "{}") "empty"
            }

            test "whitespace only is an empty mapping" {
                Expect.equal (mapping "   ") (Ok "{}") "blank"
            }

            test "a bare pair" {
                Expect.equal (mapping "level=2") (Ok "{level: 2}") "unquoted value kept raw"
            }

            test "a quoted value may contain spaces" {
                Expect.equal
                    (mapping "title=\"Watch out\"")
                    (Ok "{title: \"Watch out\"}")
                    "quotes preserved"
            }

            test "a quoted value may contain an equals sign" {
                Expect.equal
                    (mapping "expr=\"a = b\"")
                    (Ok "{expr: \"a = b\"}")
                    "the first equals splits, later ones are data"
            }

            test "several pairs" {
                Expect.equal
                    (mapping "title=\"Watch out\" level=2")
                    (Ok "{title: \"Watch out\", level: 2}")
                    "comma separated"
            }

            test "a key with no value is a flag" {
                Expect.equal (mapping "collapsible") (Ok "{collapsible: true}") "bare flag"
            }

            test "a quote that is never closed is an error" {
                let result = mapping "title=\"Watch out"
                Expect.isError result "unterminated quote rejected"
            }

            test "a double quote inside a quoted value is escaped" {
                Expect.equal
                    (mapping "title=\"say \\\"hi\\\"\"")
                    (Ok "{title: \"say \\\"hi\\\"\"}")
                    "escape survives"
            }
        ]
    ]
