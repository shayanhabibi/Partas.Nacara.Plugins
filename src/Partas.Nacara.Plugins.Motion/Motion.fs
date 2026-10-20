namespace Nacara.Plugins

open System.IO
open Nacara.Core
open Partas.Nacara.Theme

/// <summary>
/// Hovers drawn in Figma, configured and compiled from F#.
/// </summary>
/// <remarks>
/// <c>compile</c> applies every override and fails on a hover or a change the document does not have.
/// </remarks>
[<RequireQualifiedAccess>]
module Motion =

    /// <summary>The anime.js module url used by <c>animeFromCdn</c>.</summary>
    [<Literal>]
    let CdnUrl =
        "https://cdn.jsdelivr.net/npm/animejs@4.5.0/dist/bundles/anime.esm.min.js"

    /// <summary>The options a site starts from.</summary>
    let defaults: MotionOptions =
        {
            Document = "design/tokens/motion.json"
            Engine = Css
            ReducedMotion = Instant
            AnimeSource = Bundled
            Hovers = Map.empty
        }

    /// <summary>Reads the motion document from a path.</summary>
    /// <remarks>Resolved against the working directory. Defaults to <c>"design/tokens/motion.json"</c>.</remarks>
    /// <param name="path">The path of the exported document.</param>
    /// <param name="options">The options to change.</param>
    let document (path: string) (options: MotionOptions) : MotionOptions = { options with Document = path }

    /// <summary>Sets the engine that plays the hovers.</summary>
    /// <remarks>Defaults to <c>Css</c>.</remarks>
    /// <param name="engine">The engine.</param>
    /// <param name="options">The options to change.</param>
    let engine (engine: MotionEngine) (options: MotionOptions) : MotionOptions = { options with Engine = engine }

    /// <summary>Sets what the stylesheet does for visitors who ask for reduced motion.</summary>
    /// <remarks>Defaults to <c>Instant</c>.</remarks>
    /// <param name="policy">The reduced-motion policy.</param>
    /// <param name="options">The options to change.</param>
    let reducedMotion (policy: ReducedMotion) (options: MotionOptions) : MotionOptions =
        { options with ReducedMotion = policy }

    /// <summary>Sets where anime.js comes from.</summary>
    /// <remarks>Defaults to <c>Bundled</c>.</remarks>
    /// <param name="source">Where anime.js comes from.</param>
    /// <param name="options">The options to change.</param>
    let animeSource (source: AnimeSource) (options: MotionOptions) : MotionOptions =
        { options with AnimeSource = source }

    /// <summary>Loads anime.js from the jsDelivr CDN instead of the bundled copy.</summary>
    /// <remarks>Defaults to the bundled copy.</remarks>
    /// <param name="options">The options to change.</param>
    let animeFromCdn (options: MotionOptions) : MotionOptions = { options with AnimeSource = Cdn CdnUrl }

    let private settings name (options: MotionOptions) =
        options.Hovers
        |> Map.tryFind name
        |> Option.defaultValue
            {
                Enabled = true
                Engine = None
                Duration = None
                Easing = None
                Values = Map.empty
            }

    let private update name (change: HoverSettings -> HoverSettings) (options: MotionOptions) =
        { options with Hovers = options.Hovers |> Map.add name (change (settings name options)) }

    /// <summary>Sets the engine of one hover.</summary>
    /// <remarks>Defaults to the plugin's engine.</remarks>
    /// <param name="name">The hover's name in the document.</param>
    /// <param name="engine">The engine that plays this hover.</param>
    /// <param name="options">The options to change.</param>
    let hoverEngine (name: string) (engine: MotionEngine) (options: MotionOptions) : MotionOptions =
        update name (fun s -> { s with Engine = Some engine }) options

    /// <summary>Sets the duration of one hover in milliseconds.</summary>
    /// <remarks>
    /// Applies to a linear or cubic-bezier easing. A spring plays over its settling time and a spring
    /// preset over 1200ms, so <c>compile</c> fails when the hover's easing, after any
    /// <c>easing</c> override, is a spring or a preset. Defaults to the document's duration.
    /// </remarks>
    /// <param name="name">The hover's name in the document.</param>
    /// <param name="ms">The duration in milliseconds.</param>
    /// <param name="options">The options to change.</param>
    let duration (name: string) (ms: int) (options: MotionOptions) : MotionOptions =
        update name (fun s -> { s with Duration = Some ms }) options

    /// <summary>Sets the easing of one hover.</summary>
    /// <remarks>
    /// A <c>SpringPreset</c> is one of <c>"GENTLE"</c>, <c>"QUICK"</c>, <c>"BOUNCY"</c> and <c>"SLOW"</c>;
    /// <c>compile</c> fails on any other name. Defaults to the document's easing.
    /// </remarks>
    /// <param name="name">The hover's name in the document.</param>
    /// <param name="easing">The easing.</param>
    /// <param name="options">The options to change.</param>
    let easing (name: string) (easing: Easing) (options: MotionOptions) : MotionOptions =
        update name (fun s -> { s with Easing = Some easing }) options

    /// <summary>Replaces the hovered value of one change with a CSS value.</summary>
    /// <remarks>Defaults to the document's value.</remarks>
    /// <param name="name">The hover's name in the document.</param>
    /// <param name="key">The property for the root element, or <c>Layer/property</c> for a part.</param>
    /// <param name="css">The CSS value.</param>
    /// <param name="options">The options to change.</param>
    let value (name: string) (key: string) (css: string) (options: MotionOptions) : MotionOptions =
        update name (fun s -> { s with Values = s.Values |> Map.add key css }) options

    /// <summary>Leaves one hover out of the output.</summary>
    /// <remarks>Defaults to every hover being written.</remarks>
    /// <param name="name">The hover's name in the document.</param>
    /// <param name="options">The options to change.</param>
    let disable (name: string) (options: MotionOptions) : MotionOptions =
        update name (fun s -> { s with Enabled = false }) options

    let private keyOf (change: Change) =
        match change.Layer with
        | Some layer -> $"%s{layer}/%s{change.Property}"
        | None -> change.Property

    let private quoted (names: string seq) =
        names |> Seq.sort |> Seq.map (fun n -> $"\"%s{n}\"") |> String.concat ", "

    let private checkNames (options: MotionOptions) (document: MotionDocument) =
        let known = document.Hovers |> List.map (fun h -> h.Name)

        options.Hovers
        |> Map.toList
        |> List.tryPick (fun (name, _) ->
            if List.contains name known then
                None
            else
                Some
                    $"The motion options name a hover \"%s{name}\" that the document does not have. Known hovers: %s{quoted known}.")

    let private checkKeys (options: MotionOptions) (document: MotionDocument) =
        document.Hovers
        |> List.tryPick (fun hover ->
            match Map.tryFind hover.Name options.Hovers with
            | None -> None
            | Some settings ->
                let keys = hover.Changes |> List.map keyOf

                settings.Values
                |> Map.toList
                |> List.tryPick (fun (key, _) ->
                    if List.contains key keys then
                        None
                    else
                        Some
                            $"The hover \"%s{hover.Name}\" has no change \"%s{key}\" to override. Known keys: %s{quoted keys}."))

    // A hover's name is how a setter picks it out, so two with one name could not be told apart.
    let private checkDuplicates (document: MotionDocument) =
        document.Hovers
        |> List.countBy (fun hover -> hover.Name)
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map (fun (name, count) ->
            $"The motion document has %d{count} hovers named \"%s{name}\". Each hover needs a name of its own: give one of the sets another motion name in the Figma plugin and export again.")

    let private checkPresets (options: MotionOptions) =
        options.Hovers
        |> Map.toList
        |> List.tryPick (fun (name, settings) ->
            match settings.Easing with
            | Some(SpringPreset preset) when (Spring.preset preset).IsNone ->
                Some
                    $"The hover \"%s{name}\" is eased by SpringPreset \"%s{preset}\", which is not a preset. The presets are \"GENTLE\", \"QUICK\", \"BOUNCY\" and \"SLOW\"."
            | _ -> None)

    let private checkDurations (options: MotionOptions) (document: MotionDocument) =
        document.Hovers
        |> List.tryPick (fun hover ->
            match Map.tryFind hover.Name options.Hovers with
            | Some { Duration = Some ms; Easing = easing } ->
                match easing |> Option.defaultValue hover.Easing with
                | Easing.Spring _ ->
                    Some
                        $"The hover \"%s{hover.Name}\" is given a duration of %d{ms}ms, but its easing is a spring, which plays over its settling time. Set Motion.easing to a cubic-bezier or linear easing to use a duration, or change the spring's mass, stiffness and damping."
                | SpringPreset preset ->
                    Some
                        $"The hover \"%s{hover.Name}\" is given a duration of %d{ms}ms, but its easing is the %s{preset} spring preset, which plays over 1200ms. Set Motion.easing to a cubic-bezier or linear easing to use a duration, or to a custom spring."
                | Linear
                | CubicBezier _ -> None
            | _ -> None)

    let private apply (options: MotionOptions) (hover: Hover) =
        match Map.tryFind hover.Name options.Hovers with
        | None -> hover
        | Some settings ->
            { hover with
                DurationMs = settings.Duration |> Option.defaultValue hover.DurationMs
                Easing = settings.Easing |> Option.defaultValue hover.Easing
                Changes =
                    hover.Changes
                    |> List.map (fun change ->
                        match Map.tryFind (keyOf change) settings.Values with
                        | Some css -> { change with To = Literal css }
                        | None -> change) }

    let private warnings (document: MotionDocument) (hovers: Hover list) =
        [
            for hover in hovers do
                for item in hover.NotTranslated do
                    let target =
                        match item.Layer with
                        | Some layer -> $"%s{layer}/%s{item.Property}"
                        | None -> item.Property

                    $"%s{hover.Name}: %s{target} is not translated: %s{item.Reason}"

                match hover.Easing with
                | SpringPreset preset ->
                    $"%s{hover.Name}: the %s{preset} spring preset is approximated; use a custom spring to match Figma exactly."
                | _ -> ()

            for skipped in document.Skipped do
                $"%s{skipped.Component} (%s{skipped.Variant}) is skipped: %s{skipped.Reason}"
        ]

    /// <summary>Applies the options to a document and splits its hovers by engine.</summary>
    /// <remarks>
    /// Fails, naming the known ones, when the options name a hover or a change the document does not have.
    /// Fails, too, when a hover with a spring or spring preset easing is given a duration, when an easing
    /// override names a spring preset that is not one, and when two hovers of the document share a name.
    /// </remarks>
    /// <param name="options">The options.</param>
    /// <param name="document">The parsed document.</param>
    let compile (options: MotionOptions) (document: MotionDocument) : Result<CompiledMotion, string> =
        match
            checkDuplicates document
            |> Option.orElseWith (fun () -> checkNames options document)
            |> Option.orElseWith (fun () -> checkPresets options)
            |> Option.orElseWith (fun () -> checkKeys options document)
            |> Option.orElseWith (fun () -> checkDurations options document)
        with
        | Some message -> Error message
        | None ->
            let enabled (hover: Hover) =
                options.Hovers |> Map.tryFind hover.Name |> Option.forall (fun s -> s.Enabled)

            let engineOf (hover: Hover) =
                options.Hovers
                |> Map.tryFind hover.Name
                |> Option.bind (fun s -> s.Engine)
                |> Option.defaultValue options.Engine

            let applied = document.Hovers |> List.filter enabled |> List.map (apply options)
            let css, anime = applied |> List.partition (fun hover -> engineOf hover = Css)

            Ok
                {
                    Css = MotionCss.stylesheet options.ReducedMotion css
                    AnimeHovers = anime
                    Warnings = warnings document applied
                }

    /// <summary>The full path of a document, a relative one taken from the project root.</summary>
    /// <remarks>An absolute path is returned as it is.</remarks>
    /// <param name="root">The site's project root.</param>
    /// <param name="path">The document's path, as the options name it.</param>
    let resolve (root: string) (path: string) = Path.GetFullPath(Path.Combine(root, path))

    /// <summary>The project root Nacara builds this site from.</summary>
    /// <remarks>
    /// The value after <c>--root</c> on the command line when there is one, otherwise the directory of
    /// the site's project file, the same lookup Nacara makes.
    /// </remarks>
    let projectRoot () =
        let arguments = System.Environment.GetCommandLineArgs() |> List.ofArray

        let rec find arguments =
            match arguments with
            | "--root" :: value :: _ -> Path.GetFullPath value
            | _ :: rest -> find rest
            | [] -> Nacara.defaultProjectRoot ()

        find arguments

    /// <summary>Reads the options' document, then compiles it.</summary>
    /// <remarks>
    /// A relative document path is taken from the project root. Fails, naming the resolved path and how
    /// to export the document, when the file is missing.
    /// </remarks>
    /// <param name="options">The options.</param>
    let load (options: MotionOptions) : Result<CompiledMotion, string> =
        let path = resolve (projectRoot ()) options.Document

        if not (File.Exists path) then
            Error
                $"The motion document \"%s{path}\" does not exist. Run Export motion in the Nacara Figma plugin and save motion.json there."
        else
            match MotionDocument.parse (File.ReadAllText path) with
            | Error message -> Error $"%s{path}: %s{message}"
            | Ok document -> compile options document

    /// <summary>The name of the style layer the CSS hovers are offered to the theme in.</summary>
    [<Literal>]
    let LayerName = "motion"

    type private MotionPlugin(options: MotionOptions) =
        let compiled =
            match load options with
            | Ok compiled -> compiled
            | Error message -> failwith $"Motion: %s{message}"

        // Offered once: the same layer registered again would be bundled twice.
        do
            let layer = { Name = LayerName; Css = compiled.Css }

            if not (PluginLayers.all () |> List.contains layer) then
                PluginLayers.offer layer

        interface IPlugin with
            member _.Name = "motion"

            member _.Configure registry =
                let registry =
                    registry
                    |> Registry.onPagesRouted (fun context ->
                        for warning in compiled.Warnings do
                            context.Diagnostics.Add(Diagnostic.warning "motion-not-translated" warning))

                if compiled.AnimeHovers.IsEmpty then
                    registry
                else
                    let script = MotionScript.entry options.AnimeSource options.ReducedMotion compiled.AnimeHovers
                    let hash = (BuildCache.Hash script).Substring(0, 8).ToLowerInvariant()
                    let destination = $"assets/motion.%s{hash}.js"
                    let path = RelativePath.create destination

                    let asset =
                        match options.AnimeSource with
                        | Bundled ->
                            Bundle([ "motion.js", script; "anime.esm.js", MotionScript.anime () ], "motion.js", path)
                        | Cdn _ -> WriteText(script, path)

                    let registry = registry |> Registry.asset asset |> Registry.extra (Script(destination, true))

                    match options.AnimeSource with
                    | Cdn _ -> registry
                    | Bundled ->
                        registry
                        |> Registry.onBuildComplete (fun context ->
                            let file = AbsolutePath.value (AbsolutePath.combine context.OutputDirectory [ destination ])

                            if context.Writes && File.Exists file && (File.ReadAllText file).Contains "\"./anime.esm.js\"" then
                                context.Diagnostics.Add(
                                    Diagnostic.error
                                        "motion-needs-esbuild"
                                        $"%s{destination} still imports ./anime.esm.js: the site has no bundler to resolve it"
                                    |> Diagnostic.withHint "Add |> Esbuild.register to the site."
                                ))

    /// <summary>The plugin, with the default options.</summary>
    /// <remarks>Loads the motion document at once and fails, naming the path, when it is missing.</remarks>
    let create () = MotionPlugin(defaults) :> IPlugin

    /// <summary>The plugin, with options changed by <c>configure</c>.</summary>
    /// <remarks>Loads the motion document at once, offers its CSS to the theme once, and fails when either cannot be done.</remarks>
    /// <param name="configure">Changes the default options.</param>
    let createWith (configure: MotionOptions -> MotionOptions) = MotionPlugin(configure defaults) :> IPlugin

    /// <summary>Adds the plugin to a site, with the default options.</summary>
    /// <param name="site">The site.</param>
    let register (site: Site) = Site.plugin (create ()) site

    /// <summary>Adds the plugin to a site, with options changed by <c>configure</c>.</summary>
    /// <param name="configure">Changes the default options.</param>
    /// <param name="site">The site.</param>
    let registerWith (configure: MotionOptions -> MotionOptions) (site: Site) =
        Site.plugin (createWith configure) site
