module Partas.Nacara.Plugins.Tests.Directives

open Expecto
open Nacara.Plugins
open Feliz.ViewEngine
open Nacara.Core

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

        testList "directive builder" [
            test "a decoder's type survives to the render function" {
                let directive =
                    Directive.create
                        "note"
                        (Decode.object (fun get ->
                            {| Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ args _ -> Html.span [ prop.text (string args.Level) ])

                Expect.equal directive.Name "note" "the builder's name carries through to the directive"
            }

            test "decoded arguments reach the render function with the right value" {
                let directive =
                    Directive.create
                        "note"
                        (Decode.object (fun get ->
                            {| Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ args _ -> Html.span [ prop.text (string args.Level) ])

                match Directive.decodeArguments directive 0 "level=2" with
                | Ok arguments ->
                    let context: DirectiveContext =
                        {
                            Ancestors = []
                            SiblingIndex = 0
                            Nesting = 0
                            Report = ignore
                        }

                    let rendered = directive.RenderWith context arguments Html.none
                    Expect.stringContains (Render.htmlView rendered) "2" "the decoded level reaches the render function"
                | Error message -> failtest message
            }

            test "a required field missing is an error, not an exception" {
                let directive =
                    Directive.create "note" (Decode.object (fun get -> {| Title = get.Required.Field "title" Decode.string |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isError (Directive.decodeArguments directive 0 "") "title required"
            }

            test "a malformed argument line is an error, not an exception" {
                let directive =
                    Directive.create "note" (Decode.succeed ())
                    |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isError
                    (Directive.decodeArguments directive 0 "title=\"never closed")
                    "tokeniser failure surfaces as Error"
            }
        ]
    ]
