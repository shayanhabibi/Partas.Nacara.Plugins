module Partas.Nacara.Plugins.Tests.Directives

open Expecto
open Nacara.Plugins
open Feliz.ViewEngine
open Nacara.Core
open Markdig
open Markdig.Syntax
open Markdig.Extensions.CustomContainers
open Markdig.Renderers
open Markdig.Renderers.Html

let private mapping input = Arguments.toFlowMapping input

/// <summary>A stand-in for the renderer Nacara installs for its own built-in containers,
/// so a test does not depend on the stock <c>HtmlCustomContainerRenderer</c> being the one
/// that answers for an unmatched directive.</summary>
type private SentinelRenderer(marker: string) =
    inherit HtmlObjectRenderer<CustomContainer>()
    override _.Write(renderer: HtmlRenderer, container: CustomContainer) =
        renderer.Write($"<!--%s{marker}:%s{container.Info}-->") |> ignore

type private SentinelExtension(marker: string) =
    interface IMarkdownExtension with
        member _.Setup(_pipeline: MarkdownPipelineBuilder) = ()

        member _.Setup(_pipeline: MarkdownPipeline, renderer: IMarkdownRenderer) =
            match renderer with
            | :? HtmlRenderer as html -> html.ObjectRenderers.Insert(0, SentinelRenderer marker)
            | _ -> ()

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

        testList "nesting" [
            test "an ancestor is found by its type" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})

                let context =
                    { Ancestors = stack.Current
                      SiblingIndex = 0
                      Nesting = stack.Depth
                      Report = ignore }

                Expect.equal (context.TryAncestor<{| Start: int |}>()) (Some {| Start = 1 |}) "found through one level"
            }

            test "the parent is only the nearest" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})

                let context =
                    { Ancestors = stack.Current
                      SiblingIndex = 0
                      Nesting = stack.Depth
                      Report = ignore }

                Expect.isNone (context.TryParent<{| Start: int |}>()) "the grandparent is not the parent"
                Expect.isSome (context.TryParent<{| Title: string |}>()) "the parent is"
            }

            test "popping restores what was there before" {
                let stack = DirectiveStack()
                stack.Push(box {| Start = 1 |})
                stack.Push(box {| Title = "Install" |})
                stack.Pop()

                Expect.equal stack.Depth 1 "one left"
                Expect.isSome
                    ({ Ancestors = stack.Current; SiblingIndex = 0; Nesting = 1; Report = ignore }
                        .TryParent<{| Start: int |}>())
                    "the outer one is the parent again"
            }

            test "siblings of the same name are numbered in order" {
                let pipeline = Markdig.MarkdownPipelineBuilder().UseCustomContainers().Build()
                let source = "::::steps\n:::step\nfirst\n:::\n:::step\nsecond\n:::\n::::\n"
                let document = Markdig.Markdown.Parse(source, pipeline)

                let steps =
                    document.Descendants<Markdig.Extensions.CustomContainers.CustomContainer>()
                    |> Seq.filter (fun c -> c.Info = "step")
                    |> Seq.toList

                Expect.equal (steps |> List.map Context.siblingIndex) [ 0; 1 ] "numbered from zero"
            }
        ]

        testList "rendering" [
            let note =
                Directive.create "note" (Decode.object (fun get ->
                    {| Title = get.Optional.Field "title" Decode.string |}))
                |> Directive.render (fun _ args body ->
                    Html.aside [
                        prop.className "nacara-note"
                        prop.children [
                            match args.Title with
                            | Some title -> Html.p [ prop.className "nacara-note__title"; prop.text title ]
                            | None -> Html.none
                            body
                        ]
                    ])

            test "a registered directive renders through its function" {
                let html = Renderer.toHtml [ note ] ":::note title=\"Careful\"\nbody text\n:::\n"
                Expect.stringContains html "nacara-note" "the class is there"
                Expect.stringContains html "Careful" "the argument was read"
            }

            test "the body is rendered as markdown, not as text" {
                let html = Renderer.toHtml [ note ] ":::note\nsome *emphasis*\n:::\n"
                Expect.stringContains html "<em>emphasis</em>" "markdown in the body still works"
            }

            test "an unregistered directive is left alone" {
                let html = Renderer.toHtml [ note ] ":::unknown\nbody\n:::\n"
                Expect.isFalse (html.Contains "nacara-note") "our renderer did not claim it"
            }

            test "an unregistered directive still reaches whatever else would have rendered it" {
                // Proves the fallback resolves the live renderer list rather than a
                // fixed stock renderer: with a stand-in for Nacara's own container
                // renderer present, an unmatched name reaches it, and a matched one does
                // not - so this directive extension is not quietly swallowing directives
                // it was never declared to own.
                let pipeline =
                    MarkdownPipelineBuilder()
                        .UseCustomContainers()
                        .Use(SentinelExtension "OTHER")
                        .Use(DirectiveExtension [ note ])
                        .Build()

                let html =
                    Markdown.ToHtml(
                        ":::unknown\nbody\n:::\n:::note title=\"x\"\nbody\n:::\n",
                        pipeline
                    )

                Expect.stringContains html "OTHER:unknown" "the unregistered directive reached the other renderer"
                Expect.isFalse (html.Contains "OTHER:note") "the registered directive did not"
            }

            test "a render function that throws costs its own directive, not the page" {
                let exploding =
                    Directive.create "boom" (Decode.succeed ())
                    |> Directive.render (fun _ _ _ -> failwith "author bug")

                let html =
                    Renderer.toHtml
                        [ exploding; note ]
                        ":::boom\nbody\n:::\n:::note title=\"Careful\"\nafter\n:::\n"

                Expect.stringContains html "nacara-directive-error" "the thrown directive degraded visibly"
                Expect.stringContains html "author bug" "the reason is on the page"
                Expect.stringContains html "nacara-note" "the directive after it still rendered"
                Expect.stringContains html "Careful" "and so did its arguments"
            }

            test "a nested directive that throws leaves the writer usable" {
                // The body is rendered by swapping the renderer's writer. If a nested
                // directive throws and the swap is not undone, the rest of the page is
                // written into a discarded StringWriter and silently disappears.
                let exploding =
                    Directive.create "boom" (Decode.succeed ())
                    |> Directive.render (fun _ _ _ -> failwith "author bug")

                let html =
                    Renderer.toHtml
                        [ exploding; note ]
                        "::::note title=\"Outer\"\n:::boom\nbody\n:::\n::::\n\nafterwards\n"

                Expect.stringContains html "nacara-note" "the enclosing directive rendered"
                Expect.stringContains html "afterwards" "and the page carried on past it"
            }

            test "directives nest" {
                let steps =
                    Directive.create "steps" (Decode.succeed {| Start = 1 |})
                    |> Directive.render (fun _ _ body -> Html.ol [ prop.children [ body ] ])

                let step =
                    Directive.create "step" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        Html.li [
                            prop.custom ("data-step", string ctx.Index)
                            prop.children [ body ]
                        ])

                let html =
                    Renderer.toHtml
                        [ steps; step ]
                        "::::steps\n:::step\nfirst\n:::\n:::step\nsecond\n:::\n::::\n"

                Expect.stringContains html "data-step=\"0\"" "first is zero"
                Expect.stringContains html "data-step=\"1\"" "second is one"
            }

            test "a child can read its parent's arguments" {
                let steps =
                    Directive.create "steps" (Decode.object (fun get ->
                        {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ _ body -> Html.ol [ prop.children [ body ] ])

                let step =
                    Directive.create "step" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        match ctx.TryAncestor<{| Start: int |}>() with
                        | Some parent ->
                            Html.li [
                                prop.custom ("data-number", string (parent.Start + ctx.Index))
                                prop.children [ body ]
                            ]
                        | None -> Html.li [ prop.text "orphan" ])

                let html = Renderer.toHtml [ steps; step ] "::::steps start=5\n:::step\nfirst\n:::\n::::\n"
                Expect.stringContains html "data-number=\"5\"" "the parent's start was read"
            }

            test "arguments that do not decode still render the body" {
                let step =
                    Directive.create "step" (Decode.object (fun get ->
                        {| Title = get.Required.Field "title" Decode.string |}))
                    |> Directive.render (fun _ args _ -> Html.h3 args.Title)

                let html = Renderer.toHtml [ step ] ":::step\nthe body\n:::\n"
                Expect.stringContains html "the body" "content is not lost when arguments fail"
            }
        ]

        testList "registration" [
            test "two directives of one name are refused" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.equal (Directives.duplicates [ one; two ]) [ "note" ] "the clash is named"
            }

            test "distinct names are accepted" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "tip" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isEmpty (Directives.duplicates [ one; two ]) "no clash"
            }

            test "creating a plugin with a duplicate name throws at once" {
                let one = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)
                let two = Directive.create "note" (Decode.succeed ()) |> Directive.render (fun _ _ _ -> Html.none)

                Expect.throws (fun () -> Directives.create [ one; two ] |> ignore) "fails before any page renders"
            }
        ]
    ]
