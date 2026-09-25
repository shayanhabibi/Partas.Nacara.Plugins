namespace Nacara.Plugins

open System
open System.Reflection
open Nacara.Core
open Nacara.Plugins.Internal

/// <summary>
/// Live Partas.Solid examples: an <c>fsharp solid</c> fence is compiled with Fable and the
/// Partas.Solid plugin when the site builds, and what it renders is mounted under it.
/// </summary>
[<RequireQualifiedAccess>]
module SolidExamples =

    let private readResource = Resource.text (Assembly.GetExecutingAssembly())

    let private loader = lazy readResource "loader.js"

    /// What the transform leaves on a page for the compile hook to find.
    let private dataKey = "partas-solid"

    let defaults () =
        {
            PartasVersion = "3.*"
            Feeds = []
            FableVersion = "5.13.0"
            SolidVersion = "2.0.0-rc.9"
            RolldownVersion = "1.2.11"
            FenceToken = "solid"
            Prelude = [ "open Partas.Solid"; "open Fable.Core"; "open Fable.Core.JsInterop" ]
            OutputPath = "_partas/solid"
            WorkspacePath = ".nacara/partas-solid"
            Minify = true
            ShowJsx = false
            Timeout = TimeSpan.FromMinutes 5.
        }

    let partasVersion value (options: SolidExamplesOptions) = { options with PartasVersion = value }
    let feed value (options: SolidExamplesOptions) = { options with Feeds = options.Feeds @ [ value ] }
    let fableVersion value (options: SolidExamplesOptions) = { options with FableVersion = value }
    let solidVersion value (options: SolidExamplesOptions) = { options with SolidVersion = value }
    let rolldownVersion value (options: SolidExamplesOptions) = { options with RolldownVersion = value }
    let fenceToken value (options: SolidExamplesOptions) = { options with FenceToken = value }
    let prelude value (options: SolidExamplesOptions) = { options with Prelude = value }
    let outputPath value (options: SolidExamplesOptions) = { options with OutputPath = value }
    let workspacePath value (options: SolidExamplesOptions) = { options with WorkspacePath = value }
    let minify value (options: SolidExamplesOptions) = { options with Minify = value }
    let showJsx value (options: SolidExamplesOptions) = { options with ShowJsx = value }
    let timeout value (options: SolidExamplesOptions) = { options with Timeout = value }

    /// <summary>Rewrite a page's solid fences, and keep what they compile to on the page.</summary>
    let transformPage (options: SolidExamplesOptions) (context: TransformContext) (page: Page) =
        if not (page.Body.Contains options.FenceToken) then
            page
        else

        let key = SolidGenerate.pageKey page.Id
        let scan = SolidScan.scan options.FenceToken options.ShowJsx key page.Body

        if scan.Cells.IsEmpty then
            page
        else

        for line, problem in scan.Problems do
            let diagnostic = Diagnostic.error "fence" problem

            match page.SourceFile with
            | Some file -> context.Diagnostics.Add(diagnostic |> Diagnostic.at file (page.BodyLine + line - 1) 1)
            | None -> context.Diagnostics.Add diagnostic

        let code, spans = SolidGenerate.fsharp options.Prelude key scan.Cells

        let unit =
            {
                Key = key
                Source = page.SourceFile |> Option.map AbsolutePath.value
                BodyLine = page.BodyLine
                Cells = scan.Cells
                Code = code
                Spans = spans
            }

        { page with Body = scan.Body }.WithData(dataKey, unit)

    let private report (context: HookContext) (message: SolidMessage) =
        // Watch keeps serving while an example is broken: the page shows the error in its place.
        let diagnostic =
            if message.IsError && not context.IsWatch then
                Diagnostic.error "compile" message.Message
            else
                Diagnostic.warning "compile" message.Message

        let diagnostic =
            match message.Page with
            | Some { Source = Some source; BodyLine = bodyLine } ->
                let file = AbsolutePath.create source

                match message.At with
                | Some(line, column) -> diagnostic |> Diagnostic.at file (bodyLine + line - 1) column
                | None -> diagnostic |> Diagnostic.inFile file
            | _ -> diagnostic

        context.Diagnostics.Add diagnostic

    /// <summary>
    /// What the JSX tabs are filled from: the build's highlighters and code block renderers, and the
    /// JSX this build compiled.
    /// </summary>
    type private JsxTabs() =
        /// Only a transform is given the finished registry, so the first page to pass keeps it.
        member val Registry: Registry option = None with get, set
        member val Code: Map<string, Map<string, string>> = Map.empty with get, set

    let private jsxMarker =
        Text.RegularExpressions.Regex(
            """<div data-partas-jsx-page="(?<page>[^"]+)" data-partas-jsx-cell="(?<cell>[^"]+)"></div>""",
            Text.RegularExpressions.RegexOptions.Compiled
        )

    /// <summary>Put each cell's JSX in its tab, coloured as the site colours a <c>jsx</c> fence.</summary>
    let private fillJsx (tabs: JsxTabs) (context: AssetTransformContext) =
        if not (context.Content.Contains "data-partas-jsx-cell") then
            context.Content
        else
            let highlighters, renderers =
                match tabs.Registry with
                | Some registry -> Registry.extras<IHighlighter> registry, Registry.extras<ICodeBlockRenderer> registry
                | None -> [], []

            jsxMarker.Replace(
                context.Content,
                fun matched ->
                    let code =
                        tabs.Code
                        |> Map.tryFind matched.Groups["page"].Value
                        |> Option.bind (Map.tryFind matched.Groups["cell"].Value)
                        |> Option.defaultValue "// Fable wrote no JSX for this example."

                    CodeBlock.render
                        renderers
                        highlighters
                        {
                            Language = Some "jsx"
                            Code = code
                            Meta = CodeBlockMeta.empty
                        }
            )

    let private compileAll (options: SolidExamplesOptions) (tabs: JsxTabs) (context: HookContext) =
        let units =
            context.Pages
            |> List.choose (fun page -> page.TryData<SolidPageUnit> dataKey)
            |> List.sortBy _.Key

        if not units.IsEmpty then
            let started = Diagnostics.Stopwatch.StartNew()

            let compiled =
                SolidCompile.compile options (AbsolutePath.value context.ProjectRoot) context.IsWatch (fun line -> Log.info $"solid: %s{line}") units

            for name, text in compiled.Files do
                context.Write $"%s{options.OutputPath}/%s{name}" text |> ignore

            compiled.Messages |> List.iter (report context)
            tabs.Code <- SolidCompile.jsx options (AbsolutePath.value context.ProjectRoot) units

            if started.ElapsedMilliseconds > 1000L then
                Log.info $"solid: %d{units.Length} page(s) of examples in %d{started.ElapsedMilliseconds}ms"

    type private SolidExamplesPlugin(options: SolidExamplesOptions) =
        interface IPlugin with
            member _.Name = "partas-solid"

            member _.Configure registry =
                let loaderPath = $"%s{options.OutputPath}/loader.js"
                let tabs = JsxTabs()

                let registry =
                    registry
                    |> Registry.transform
                        {
                            Name = "partas-solid"
                            Extensions = [ ".md"; ".markdown" ]
                            Transform =
                                fun context page ->
                                    if tabs.Registry.IsNone then
                                        tabs.Registry <- Some context.Registry

                                    transformPage options context page
                        }

                // First in line, so the fences are rewritten before whichever plugin renders the
                // markdown sees them, however the plugins were ordered on the site.
                let transforms =
                    List.last registry.Transforms :: List.take (registry.Transforms.Length - 1) registry.Transforms

                { registry with Transforms = transforms }
                |> Registry.asset (WriteText(loader.Value, RelativePath.create loaderPath))
                |> Registry.extra (Script(loaderPath, true))
                |> Registry.onPagesRouted (compileAll options tabs)
                |> Registry.assetTransform
                    {
                        Name = "partas-solid-jsx"
                        Extensions = [ ".html" ]
                        Transform = fillJsx tabs
                    }

    let create () = SolidExamplesPlugin(defaults ()) :> IPlugin

    let createWith (configure: SolidExamplesOptions -> SolidExamplesOptions) =
        SolidExamplesPlugin(configure (defaults ())) :> IPlugin

    let register (site: Site) = Site.plugin (create ()) site

    let registerWith (configure: SolidExamplesOptions -> SolidExamplesOptions) (site: Site) =
        Site.plugin (createWith configure) site
