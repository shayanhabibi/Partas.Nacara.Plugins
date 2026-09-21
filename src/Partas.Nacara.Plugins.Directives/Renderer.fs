namespace Nacara.Plugins

open System.IO
open Feliz.ViewEngine
open Markdig
open Markdig.Extensions.CustomContainers
open Markdig.Renderers
open Markdig.Renderers.Html

/// <summary>Renders the containers that name a declared directive.</summary>
/// <remarks>
/// Only those: a container naming anything else is refused, so Nacara's own
/// <c>:::steps</c>, <c>:::filetree</c> and <c>:::preview</c> keep falling through to
/// whatever renderer would otherwise have handled it.
/// </remarks>
type internal DirectiveRenderer(directives: Directive list, stack: DirectiveStack) =
    inherit HtmlObjectRenderer<CustomContainer>()

    let byName =
        directives |> List.map (fun directive -> directive.Name, directive) |> dict

    /// <summary>Markdig's stock container renderer, used only when nothing else in the
    /// live renderer list will take an unmatched container.</summary>
    /// <remarks>
    /// This is a last resort, not "what Nacara's built-ins are rendered by" - Nacara
    /// installs its own <c>NacaraContainerRenderer</c> into <c>ObjectRenderers</c> per page,
    /// closing over that page and its transform context, and that one is found and used
    /// instead whenever it is present. This stock renderer only fires if nothing else in
    /// the list accepts the container, so an unmatched directive still renders as
    /// something rather than nothing.
    /// </remarks>
    let fallback = HtmlCustomContainerRenderer()

    /// <summary>The renderer that would have handled this container had ours not claimed
    /// every <c>CustomContainer</c> first.</summary>
    /// <remarks>
    /// Resolved from the live <c>ObjectRenderers</c> list on every call, not cached at
    /// <c>Setup</c> time: Nacara inserts <c>NacaraContainerRenderer</c> into that list
    /// itself, per page, after the pipeline this extension configured has already been
    /// built - so at <c>Setup</c> time it is not there yet to find.
    /// </remarks>
    let resolveFallback (self: IMarkdownObjectRenderer) (renderer: HtmlRenderer) (container: CustomContainer) =
        renderer.ObjectRenderers
        |> Seq.tryFind (fun candidate ->
            not (System.Object.ReferenceEquals(candidate, self))
            && candidate.Accept(renderer, container.GetType()))
        |> Option.defaultValue (fallback :> IMarkdownObjectRenderer)

    /// <summary>Render a container's children and hand back what was written.</summary>
    /// <remarks>
    /// The writer is swapped rather than a second renderer built, so every extension and
    /// all of the renderer's state carry into the body - which is what keeps nested
    /// directives, code blocks and highlighting working inside a <c>:::</c>.
    /// </remarks>
    let capture (renderer: HtmlRenderer) (container: CustomContainer) =
        let original = renderer.Writer
        use captured = new StringWriter()
        renderer.Writer <- captured
        renderer.WriteChildren container |> ignore
        renderer.Writer <- original
        captured.ToString()

    override this.Write(renderer: HtmlRenderer, container: CustomContainer) =
        // Looked up once and bound here - `directive` is used for both `Decode` and
        // `RenderWith` below. A second lookup could in principle resolve to a different
        // entry than the first (a build that reconfigures `directives` mid-render, say),
        // pairing `Decode`'s boxed value with a `RenderWith` that unboxes for a different
        // `'T` and throwing an uncaught `InvalidCastException` instead of reporting a
        // fault.
        match byName.TryGetValue(container.Info) with
        | false, _ ->
            // Not one of ours: hand it to whichever renderer in the live list would have
            // taken it had this one not been inserted first - Nacara's own container
            // renderer if it is present, the stock one otherwise.
            let target = resolveFallback (this :> IMarkdownObjectRenderer) renderer container
            target.Write(renderer, container)
        | true, directive ->
            match Directive.decodeArguments directive container.Line container.Arguments with
            | Error reason ->
                // The arguments are unreadable, so the directive cannot be rendered - but
                // the body is the author's writing and is worth more than the wrapper.
                // There is nowhere on the page to put this - Nacara never wires a
                // diagnostic sink through to a `Registry`-scoped `IMarkdownExtension` - so
                // the degraded path is what there is: a visible error element in the
                // output, and the same message on stderr so the build log has it too.
                eprintfn $"directives: :::%s{container.Info} - %s{reason}"

                let body = capture renderer container

                Html.div [
                    prop.className "nacara-directive-error"
                    prop.children [
                        Html.p [ prop.text $":::%s{container.Info} - %s{reason}" ]
                        Html.span [ prop.dangerouslySetInnerHTML body ]
                    ]
                ]
                |> Render.htmlView
                |> renderer.Write
                |> ignore
            | Ok arguments ->
                let faults = ResizeArray<string>()

                let context =
                    {
                        Ancestors = stack.Current
                        SiblingIndex = Context.siblingIndex container
                        Nesting = stack.Depth
                        Report = faults.Add
                    }

                // `Using`, not a raw `Push`/`Pop`: this stack is shared by every page in
                // the build (one `IMarkdownExtension` instance, reused), so a render
                // function that throws must not leave its push behind for the next
                // directive - or the next page - to inherit.
                let body =
                    stack.Using(arguments, fun () -> capture renderer container)

                let element =
                    directive.RenderWith context arguments (Html.span [ prop.dangerouslySetInnerHTML body ])

                renderer.Write(Render.htmlView element) |> ignore

                for fault in faults do
                    eprintfn $"directives: :::%s{container.Info} - %s{fault}"

/// <summary>Teaches a Markdig pipeline about the declared directives.</summary>
type internal DirectiveExtension(directives: Directive list) =
    interface IMarkdownExtension with
        member _.Setup(pipeline: MarkdownPipelineBuilder) =
            // :::name is the custom container syntax, so it has to be on for any of this
            // to parse. Nacara turns it on already; saying so again is harmless.
            pipeline.UseCustomContainers() |> ignore

        member _.Setup(_pipeline: MarkdownPipeline, renderer: IMarkdownRenderer) =
            match renderer with
            | :? HtmlRenderer as html ->
                // A new stack per `HtmlRenderer`, not one held by the extension: the
                // extension is one shared instance across every page in the build, but
                // this `Setup` runs once per renderer, which is once per page. Sharing the
                // stack itself would let one page's nesting leak into the next.
                html.ObjectRenderers.Insert(0, DirectiveRenderer(directives, DirectiveStack()))
            | _ -> ()

/// <summary>Rendering markdown with directives applied, for tests.</summary>
module internal Renderer =

    /// <summary>Render a markdown string with the given directives in force.</summary>
    let toHtml (directives: Directive list) (markdown: string) =
        let pipeline =
            MarkdownPipelineBuilder()
                .UseCustomContainers()
                .Use(DirectiveExtension directives)
                .Build()

        Markdown.ToHtml(markdown, pipeline)
