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
    let timeout value (options: SolidExamplesOptions) = { options with Timeout = value }

    /// <summary>Rewrite a page's solid fences, and keep what they compile to on the page.</summary>
    let transformPage (options: SolidExamplesOptions) (context: TransformContext) (page: Page) =
        if not (page.Body.Contains options.FenceToken) then
            page
        else

        let key = SolidGenerate.pageKey page.Id
        let scan = SolidScan.scan options.FenceToken key page.Body

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

    let private compileAll (options: SolidExamplesOptions) (context: HookContext) =
        let units =
            context.Pages
            |> List.choose (fun page -> page.TryData<SolidPageUnit> dataKey)
            |> List.sortBy _.Key

        if not units.IsEmpty then
            let started = Diagnostics.Stopwatch.StartNew()

            let compiled =
                SolidCompile.compile options (AbsolutePath.value context.ProjectRoot) (fun line -> Log.info $"solid: %s{line}") units

            for name, text in compiled.Files do
                context.Write $"%s{options.OutputPath}/%s{name}" text |> ignore

            compiled.Messages |> List.iter (report context)

            if started.ElapsedMilliseconds > 1000L then
                Log.info $"solid: %d{units.Length} page(s) of examples in %d{started.ElapsedMilliseconds}ms"

    type private SolidExamplesPlugin(options: SolidExamplesOptions) =
        interface IPlugin with
            member _.Name = "partas-solid"

            member _.Configure registry =
                let loaderPath = $"%s{options.OutputPath}/loader.js"

                let registry =
                    registry
                    |> Registry.transform
                        {
                            Name = "partas-solid"
                            Extensions = [ ".md"; ".markdown" ]
                            Transform = transformPage options
                        }

                // First in line, so the fences are rewritten before whichever plugin renders the
                // markdown sees them, however the plugins were ordered on the site.
                let transforms =
                    List.last registry.Transforms :: List.take (registry.Transforms.Length - 1) registry.Transforms

                { registry with Transforms = transforms }
                |> Registry.asset (WriteText(loader.Value, RelativePath.create loaderPath))
                |> Registry.extra (Script(loaderPath, true))
                |> Registry.onPagesRouted (compileAll options)

    let create () = SolidExamplesPlugin(defaults ()) :> IPlugin

    let createWith (configure: SolidExamplesOptions -> SolidExamplesOptions) =
        SolidExamplesPlugin(configure (defaults ())) :> IPlugin

    let register (site: Site) = Site.plugin (create ()) site

    let registerWith (configure: SolidExamplesOptions -> SolidExamplesOptions) (site: Site) =
        Site.plugin (createWith configure) site
