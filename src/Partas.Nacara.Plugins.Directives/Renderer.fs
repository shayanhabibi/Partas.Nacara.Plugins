namespace Nacara.Plugins

open System.Collections.Generic
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
/// <param name="byName">Every declared directive, keyed by the name that selects it.</param>
/// <param name="fallback">Markdig's stock container renderer, used only when nothing else in
/// the live renderer list will take an unmatched container.</param>
/// <param name="stack">The directives currently open on the page being rendered.</param>
/// <remarks>
/// <paramref name="byName"/> and <paramref name="fallback"/> are built once by
/// <c>DirectiveExtension</c> and handed in rather than rebuilt here, because neither depends
/// on the page: the directive list is fixed when the plugin is created, and this type is
/// constructed once per page. <paramref name="stack"/> is the one thing that must be
/// per-page - see <c>DirectiveExtension</c>'s renderer <c>Setup</c>.
///
/// <paramref name="fallback"/> is a last resort, not "what Nacara's built-ins are rendered
/// by" - Nacara installs its own <c>NacaraContainerRenderer</c> into <c>ObjectRenderers</c>
/// per page, closing over that page and its transform context, and that one is found and
/// used instead whenever it is present. The stock renderer only fires if nothing else in the
/// list accepts the container, so an unmatched directive still renders as something rather
/// than nothing.
/// </remarks>
type internal DirectiveRenderer
    (byName: IDictionary<string, Directive>, fallback: HtmlCustomContainerRenderer, stack: DirectiveStack) =
    inherit HtmlObjectRenderer<CustomContainer>()

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

        try
            renderer.WriteChildren container |> ignore
        finally
            // Restored in `finally` because the body can throw: a nested directive's own
            // render function is an author's code, running inside this call. Leaving the
            // captured writer in place would send the rest of the page into a StringWriter
            // nobody reads.
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
                eprintfn $"directives: error: :::%s{container.Info} - %s{reason}"

                // Guarded, because the body can contain a nested directive whose render
                // function is an author's code and may throw - the same hazard the `Ok`
                // branch covers with its own `try`.
                //
                // Nothing is pushed for this directive, and that is the chosen behaviour
                // rather than an oversight: `Push` takes the decoded arguments, and there
                // are none. Children of a directive whose arguments did not decode
                // therefore find no ancestor for it - `TryAncestor` looks past it to the
                // grandparent - and their `Depth` counts it as absent.
                let body =
                    try
                        capture renderer container
                    with error ->
                        eprintfn $"directives: error: :::%s{container.Info} - %s{error.Message}"
                        ""

                Html.div [
                    prop.className "nacara-directive-error"
                    prop.children [
                        Html.p [ prop.text $":::%s{container.Info} - %s{reason}" ]
                        // A `div`, not a `span`: a directive body is block content, and
                        // `<span><p>…</p></span>` is not valid nesting.
                        Html.div [ prop.dangerouslySetInnerHTML body ]
                    ]
                ]
                |> Render.htmlView
                |> renderer.Write
                |> ignore
            | Ok arguments ->
                let faults = ResizeArray<FaultSeverity * string>()

                let context =
                    {
                        Ancestors = stack.Current
                        SiblingIndex = Context.siblingIndex container
                        Nesting = stack.Depth
                        Report = faults.Add
                    }

                // `Using`, not a raw `Push`/`Pop`: a render function that throws must not
                // leave its push behind for the directives after it to inherit as an
                // ancestor they do not have.
                let rendered =
                    try
                        let body =
                            stack.Using(arguments, fun () -> capture renderer container)

                        // Serialised inside the `try` as well: an element can throw on the
                        // way to a string just as a render function can throw on the way to
                        // an element, and both are the author's code failing on one
                        // directive rather than a reason to lose the page.
                        //
                        // A `div`, not a `span`: a directive body is always block content -
                        // `<p>`, `<ul>`, `<pre>` - so a `span` emits `<span><p>…</p></span>`,
                        // which is not valid inside an inline formatting context. The `span`
                        // precedent is `Components.rawHtml`, which is only ever handed inline
                        // SVG, so it does not carry over here.
                        Ok(
                            Render.htmlView (
                                directive.RenderWith context arguments (Html.div [ prop.dangerouslySetInnerHTML body ])
                            )
                        )
                    with error ->
                        // An author's render function threw. Left alone this takes the
                        // whole page down, which is a wildly disproportionate answer to
                        // one bad directive - so it degrades the same way unreadable
                        // arguments do, and for the same reason.
                        Error error.Message

                match rendered with
                | Ok html -> renderer.Write html |> ignore
                | Error message ->
                    eprintfn $"directives: error: :::%s{container.Info} - %s{message}"

                    Html.div [
                        prop.className "nacara-directive-error"
                        prop.children [ Html.p [ prop.text $":::%s{container.Info} - %s{message}" ] ]
                    ]
                    |> Render.htmlView
                    |> renderer.Write
                    |> ignore

                // stderr is the only channel a directive has, so the severity it chose has
                // to survive to the label: `ctx.Error` and `ctx.Warn` would otherwise be
                // one function wearing two names. Neither stops the build - see
                // `DirectiveContext.Error`.
                for severity, fault in faults do
                    let label =
                        match severity with
                        | FaultSeverity.Error -> "error"
                        | FaultSeverity.Warning -> "warning"

                    eprintfn $"directives: %s{label}: :::%s{container.Info} - %s{fault}"

/// <summary>Teaches a Markdig pipeline about the declared directives.</summary>
type internal DirectiveExtension(directives: Directive list) =
    // Built here rather than in `DirectiveRenderer`, which is constructed once per page:
    // neither of these depends on the page, and the directive list is fixed when the plugin
    // is created.
    let byName =
        directives |> List.map (fun directive -> directive.Name, directive) |> dict

    let fallback = HtmlCustomContainerRenderer()

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
                //
                // Position 0 is load-bearing, and only holds while this is the last thing
                // to insert there. Another `Registry.extra` extension inserting at 0 after
                // this one claims every `CustomContainer` first and every directive stops
                // rendering, with no message anywhere - so if directives have gone quiet,
                // the order of `Registry.extra` registrations is the thread to pull.
                html.ObjectRenderers.Insert(0, DirectiveRenderer(byName, fallback, DirectiveStack()))
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
