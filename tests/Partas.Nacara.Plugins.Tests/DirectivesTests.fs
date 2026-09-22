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

/// <summary>A renderer that throws instead of writing.</summary>
/// <remarks>
/// This is the only way to get an exception to propagate out of <c>WriteChildren</c>: a
/// nested <em>directive</em> cannot, because its own <c>Write</c> catches whatever its
/// render function throws and writes a degraded element instead. Something the directive
/// renderer delegates to, on the other hand, is not guarded by anything.
/// </remarks>
type private ThrowingRenderer() =
    inherit HtmlObjectRenderer<CustomContainer>()

    override _.Write(_renderer: HtmlRenderer, container: CustomContainer) : unit =
        failwith $"the renderer for :::%s{container.Info} threw"

type private ThrowingExtension() =
    interface IMarkdownExtension with
        member _.Setup(_pipeline: MarkdownPipelineBuilder) = ()

        member _.Setup(_pipeline: MarkdownPipeline, renderer: IMarkdownRenderer) =
            match renderer with
            | :? HtmlRenderer as html -> html.ObjectRenderers.Insert(0, ThrowingRenderer())
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

            test "a bare value carrying YAML punctuation is refused by name" {
                // Left alone this is `{title: a,b: 2}` - two keys, silently.
                match mapping "title=a,b=2" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message ->
                    Expect.stringContains message "','" "the offending character is named"
                    Expect.stringContains message "title" "and so is the key it belongs to"
            }

            test "a bare value carrying a colon is refused by name" {
                match mapping "title=a:b" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message ->
                    Expect.stringContains message "':'" "the offending character is named"
                    Expect.stringContains message "title" "and so is the key it belongs to"
            }

            test "text after a closing quote is refused by name" {
                // Left alone the parser lands on `x` and reads it as a fresh flag.
                match mapping "title=\"a\"x" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message ->
                    Expect.stringContains message "'x'" "the offending character is named"
                    Expect.stringContains message "title" "and so is the key it belongs to"
            }

            test "a repeated argument is refused by name" {
                match mapping "a=1 a=2" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "'a'" "the repeated key is named"
            }

            test "an argument with no name is refused" {
                match mapping "=5" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message ->
                    Expect.stringContains message "no name" "the author hears about their line, not about YAML"
            }

            test "an argument name carrying YAML punctuation is refused by name" {
                // Left alone this is `{a,b: true}` - two keys from one flag, silently.
                match mapping "a,b" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message ->
                    Expect.stringContains message "','" "the offending character is named"
                    Expect.stringContains message "a,b" "and so is the name it came from"
            }

            test "a punctuated name cannot smuggle a repeat past the duplicate check" {
                // `a,b` reaches YAML as a second `a`, which the HashSet never saw.
                match mapping "a=1 a,b=2" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "','" "the name is refused before YAML sees it"
            }

            test "a quoted value may carry YAML punctuation" {
                // The rejection above is for BARE values and names only. Quoting is the
                // documented way to keep punctuation as text, so it has to keep working.
                match mapping "title=\"Hello, world: x\" level=2" with
                | Ok yaml ->
                    Expect.stringContains yaml "\"Hello, world: x\"" "the quoted value survives intact"
                    Expect.stringContains yaml "level: 2" "and the argument after it is still read"
                | Error message -> failtestf "expected the quoted value to be accepted, got %s" message
            }

            test "a value in brackets is handed to YAML as a collection" {
                // The brackets are the point: they reach YAML intact, so `Decode.list` reads
                // a list rather than a decoder having to split a string.
                match mapping "tags=[fsharp, dotnet]" with
                | Ok yaml -> Expect.stringContains yaml "tags: [fsharp, dotnet]" "the sequence survives intact"
                | Error message -> failtestf "expected the sequence to be accepted, got %s" message
            }

            test "a bracketed value does not end at the space inside it" {
                // A bare value stops at the first space. If a bracketed one did too, `[a,`
                // would be the value and `b]` would be read as a fresh argument.
                match mapping "tags=[a, b] level=2" with
                | Ok yaml ->
                    Expect.stringContains yaml "tags: [a, b]" "the whole sequence is one value"
                    Expect.stringContains yaml "level: 2" "and the argument after it is still read"
                | Error message -> failtestf "expected the sequence to be accepted, got %s" message
            }

            test "brackets may nest" {
                match mapping "meta={title: a, tags: [x, y]}" with
                | Ok yaml -> Expect.stringContains yaml "meta: {title: a, tags: [x, y]}" "the inner sequence does not close the outer mapping"
                | Error message -> failtestf "expected the mapping to be accepted, got %s" message
            }

            test "an empty collection is a collection" {
                match mapping "tags=[]" with
                | Ok yaml -> Expect.stringContains yaml "tags: []" "the empty sequence is read"
                | Error message -> failtestf "expected the empty sequence to be accepted, got %s" message
            }

            test "a bracket inside a quoted section does not close the collection" {
                match mapping "tags=[\"a, b]\", c]" with
                | Ok yaml -> Expect.stringContains yaml "tags: [\"a, b]\", c]" "the quoted bracket is part of the value"
                | Error message -> failtestf "expected the quoted bracket to be accepted, got %s" message
            }

            test "a collection that is never closed is refused" {
                match mapping "tags=[a, b" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "never closed" "the author hears which end is missing"
            }

            test "a collection closed by the wrong bracket is refused" {
                match mapping "tags=[a, b}" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "']'" "the expected bracket is named"
            }

            test "text after a closing bracket is refused" {
                // Same hole the closing-quote check covers: `x` would become a flag.
                match mapping "tags=[a]x" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "closing bracket" "the author hears where the value ended"
            }

            test "a stray bracket is still refused" {
                // Reading collections must not reopen the hole: this value does not open one.
                match mapping "title=a]b" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "']'" "the offending character is named"
            }

            test "a comma in a bare value is still refused" {
                // The regression that collections could have brought back: `title=a,b=2`
                // silently becoming two arguments.
                match mapping "title=a,b=2" with
                | Ok yaml -> failtestf "expected a rejection, got %s" yaml
                | Error message -> Expect.stringContains message "','" "the offending character is named"
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

            test "a bracketed argument decodes as a list, not as a string" {
                let directive =
                    Directive.create
                        "note"
                        (Decode.object (fun get -> {| Tags = get.Required.Field "tags" (Decode.list Decode.string) |}))
                    |> Directive.render (fun _ args _ -> Html.span [ prop.text (String.concat "|" args.Tags) ])

                match Directive.decodeArguments directive 0 "tags=[fsharp, dotnet]" with
                | Ok arguments ->
                    let context: DirectiveContext =
                        {
                            Ancestors = []
                            SiblingIndex = 0
                            Nesting = 0
                            Report = ignore
                        }

                    let rendered = directive.RenderWith context arguments Html.none
                    Expect.stringContains (Render.htmlView rendered) "fsharp|dotnet" "both list entries reach the render function"
                | Error message -> failtest message
            }

            test "a required field missing is an error, not an exception" {
                let directive =
                    Directive.create "note" (Decode.object (fun get -> {| Title = get.Required.Field "title" Decode.string |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                Expect.isError (Directive.decodeArguments directive 0 "") "title required"
            }

            test "a decode failure says where on the page it is" {
                let directive =
                    Directive.create "note" (Decode.object (fun get ->
                        {| Level = get.Required.Field "level" Decode.int |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                // Markdig counts `CustomContainer.Line` from zero, so 4 is the page's
                // fifth line - which is the number the author has to be given.
                match Directive.decodeArguments directive 4 "level=high" with
                | Ok _ -> failtest "'high' is not an int and should not have decoded"
                | Error message ->
                    Expect.stringContains message "line 5" "the offset is folded into the message"
                    Expect.stringContains message "level" "and the path survives alongside it"
            }

            test "a decode failure on the first line says line 1" {
                let directive =
                    Directive.create "note" (Decode.object (fun get ->
                        {| Level = get.Required.Field "level" Decode.int |}))
                    |> Directive.render (fun _ _ _ -> Html.none)

                match Directive.decodeArguments directive 0 "level=high" with
                | Ok _ -> failtest "'high' is not an int and should not have decoded"
                | Error message ->
                    Expect.stringContains message "line 1" "no off-by-one at the top of the page"
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

            test "an exception through WriteChildren leaves the writer usable" {
                // The body is rendered by swapping the renderer's writer. If something in
                // the body throws and the swap is not undone, the rest of the page is
                // written into a discarded StringWriter and silently disappears.
                //
                // A nested *directive* cannot demonstrate this: its own `Write` catches
                // what its render function throws, so nothing ever propagates out of the
                // enclosing `capture`. The throw has to come from a renderer the directive
                // renderer delegates to, which nothing guards. Delete the `try/finally` in
                // `capture` and "afterwards" below stops reaching the output.
                let pipeline =
                    MarkdownPipelineBuilder()
                        .UseCustomContainers()
                        .Use(ThrowingExtension())
                        .Use(DirectiveExtension [ note ])
                        .Build()

                let html =
                    Markdown.ToHtml(
                        "::::note title=\"Outer\"\n:::boom\nbody\n:::\n::::\n\nafterwards\n",
                        pipeline
                    )

                Expect.stringContains html "nacara-directive-error" "the enclosing directive degraded visibly"
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

            test "a grandchild reaches its grandparent's arguments" {
                let steps =
                    Directive.create "steps" (Decode.object (fun get ->
                        {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
                    |> Directive.render (fun _ _ body -> Html.ol [ prop.children [ body ] ])

                let group =
                    Directive.create "group" (Decode.succeed {| Label = "middle" |})
                    |> Directive.render (fun _ _ body -> Html.div [ prop.children [ body ] ])

                let step =
                    Directive.create "step" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        Html.li [
                            prop.custom ("data-depth", string ctx.Depth)
                            prop.custom (
                                "data-number",
                                match ctx.TryAncestor<{| Start: int |}>() with
                                | Some outermost -> string (outermost.Start + ctx.Index)
                                | None -> "orphan"
                            )
                            prop.children [ body ]
                        ])

                // Each fence is shorter than the one enclosing it, which is the rule
                // Markdig's fenced blocks impose: with equal fences the first `:::` would
                // close the outermost block and these would be siblings.
                let html =
                    Renderer.toHtml
                        [ steps; group; step ]
                        ":::::steps start=5\n::::group\n:::step\nfirst\n:::\n::::\n:::::\n"

                Expect.stringContains
                    html
                    "data-number=\"5\""
                    "TryAncestor looked past the group to the grandparent's type"

                Expect.stringContains html "data-depth=\"2\"" "two directives enclose it"
            }

            testSequenced
            <| test "a reported fault reaches stderr under its own severity" {
                // Sequenced because it swaps `Console.Error`, which is process-wide.
                let speaking =
                    Directive.create "speak" (Decode.succeed ())
                    |> Directive.render (fun ctx _ body ->
                        ctx.Error "this one could not be done"
                        ctx.Warn "this one is only worth saying"
                        body)

                let original = System.Console.Error
                use captured = new System.IO.StringWriter()
                System.Console.SetError captured

                try
                    Renderer.toHtml [ speaking ] ":::speak\nbody\n:::\n" |> ignore
                finally
                    System.Console.SetError original

                let reported = captured.ToString()

                Expect.stringContains
                    reported
                    "directives: error: :::speak - this one could not be done"
                    "ctx.Error reached the drain under the error label"

                Expect.stringContains
                    reported
                    "directives: warning: :::speak - this one is only worth saying"
                    "and ctx.Warn is distinguishable from it in the output"
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
