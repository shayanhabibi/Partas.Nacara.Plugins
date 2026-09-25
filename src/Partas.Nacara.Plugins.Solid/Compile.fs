namespace Nacara.Plugins.Internal

open System
open System.IO
open System.Text.RegularExpressions
open Nacara.Plugins

/// <summary>Something a compile had to say, placed on a page when it can be.</summary>
type SolidMessage =
    {
        Page: SolidPageUnit option
        /// The body line and column, when the message came from a cell.
        At: (int * int) option
        IsError: bool
        Message: string
    }

/// <summary>What compiling every page's examples produced.</summary>
type SolidCompiled =
    {
        /// Output files, relative to the plugin's output path, with their text.
        Files: (string * string) list
        Messages: SolidMessage list
        /// False when the output is what an earlier build left, because this one could not run.
        Fresh: bool
    }

/// <summary>Runs Fable, then the Solid compiler and rolldown, over the generated workspace.</summary>
[<RequireQualifiedAccess>]
module SolidCompile =

    let private readResource =
        Nacara.Core.Resource.text (Reflection.Assembly.GetExecutingAssembly())

    let bundleScript = lazy readResource "bundle.mjs"

    let private fableLine =
        Regex(
            @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): \(\d+,\d+\) (?<severity>error|warning) (?<code>\w+): (?<message>.*?)(?: \(code \d+\))?$",
            RegexOptions.Compiled
        )

    /// <summary>Read Fable's diagnostics and place each on the page it came from.</summary>
    /// <remarks>Warnings from lines the generator wrote rather than a reader are dropped.</remarks>
    let fableMessages (units: SolidPageUnit list) (output: string) =
        [
            for line in output.Split('\n') do
                let matched = fableLine.Match(line.TrimEnd('\r'))

                if matched.Success then
                    // Split by hand: Path.GetFileName on Linux leaves a Windows path whole.
                    let file = matched.Groups["file"].Value.Split([| '/'; '\\' |]) |> Array.last
                    let isError = matched.Groups["severity"].Value = "error"
                    let page = units |> List.tryFind (fun unit -> SolidGenerate.fileName unit.Key = file)

                    let at =
                        page
                        |> Option.bind (fun unit ->
                            SolidGenerate.locate
                                unit.Spans
                                (int matched.Groups["line"].Value)
                                (int matched.Groups["column"].Value)
                        )

                    if isError || at.IsSome then
                        {
                            Page = page
                            At = at
                            IsError = isError
                            Message = matched.Groups["message"].Value
                        }
        ]
        |> List.distinct

    let private general isError message =
        {
            Page = None
            At = None
            IsError = isError
            Message = message
        }

    let private tail (text: string) =
        let lines = text.Trim().Split('\n')
        lines[max 0 (lines.Length - 30) ..] |> String.concat "\n"

    let private readOutput (directory: string) =
        if Directory.Exists directory then
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            |> Seq.map (fun file ->
                Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllText file
            )
            |> Seq.sortBy fst
            |> List.ofSeq
        else
            []

    /// <summary>Everything that decides the output, so an unchanged site skips the tools.</summary>
    let fingerprint (options: SolidExamplesOptions) (units: SolidPageUnit list) =
        [
            options.PartasVersion
            yield! options.Feeds
            options.FableVersion
            options.SolidVersion
            options.RolldownVersion
            string options.Minify
            bundleScript.Value
            for unit in units do
                unit.Key
                unit.Code
                SolidGenerate.entry unit.Key unit.Cells
        ]
        |> String.concat "\u0000"
        |> SolidGenerate.hash 16

    /// <summary>Bring the workspace up to date and compile it.</summary>
    /// <param name="options">The plugin's options.</param>
    /// <param name="root">The site's project root.</param>
    /// <param name="log">Told what is taking the time.</param>
    /// <param name="units">Every page with examples.</param>
    let compile (options: SolidExamplesOptions) (root: string) (log: string -> unit) (units: SolidPageUnit list) =
        let workspace = Path.GetFullPath(Path.Combine(root, options.WorkspacePath))
        let outDir = Path.Combine(workspace, "out")
        let stampFile = Path.Combine(workspace, ".stamp")
        let key = fingerprint options units
        let at (path: string) = Path.Combine(workspace, path)
        let run = SolidWorkspace.run options.Timeout workspace
        let previous () = readOutput outDir

        let failed (messages: SolidMessage list) =
            {
                Files = previous ()
                Messages = messages
                Fresh = false
            }

        if File.Exists stampFile && File.ReadAllText stampFile = key && Directory.Exists outDir then
            {
                Files = previous ()
                Messages = []
                Fresh = true
            }
        else

        Directory.CreateDirectory workspace |> ignore
        // A stamp that outlives a failed run would let the next build skip it.
        if File.Exists stampFile then
            File.Delete stampFile

        let sources = units |> List.map (fun unit -> SolidGenerate.fileName unit.Key)

        SolidWorkspace.write (at "nuget.config") (SolidWorkspace.nugetConfig options) |> ignore
        SolidWorkspace.write (at "bundle.mjs") bundleScript.Value |> ignore
        SolidWorkspace.write (at "Docs.fsproj") (SolidWorkspace.projectFile options sources) |> ignore
        let toolsChanged = SolidWorkspace.write (at ".config/dotnet-tools.json") (SolidWorkspace.toolManifest options)
        let packagesChanged = SolidWorkspace.write (at "package.json") (SolidWorkspace.packageJson options)

        for unit in units do
            SolidWorkspace.write (at (SolidGenerate.fileName unit.Key)) unit.Code |> ignore
            SolidWorkspace.write (at $"entries/%s{unit.Key}.js") (SolidGenerate.entry unit.Key unit.Cells) |> ignore

        // Pages that no longer have examples leave their module behind otherwise.
        for file in Directory.EnumerateFiles(workspace, "P*.fs*") do
            let name = Path.GetFileName file

            if not (sources |> List.exists (fun source -> name = source || name = source + ".jsx")) then
                File.Delete file

        if Directory.Exists(at "entries") then
            for file in Directory.EnumerateFiles(at "entries") do
                if not (units |> List.exists (fun unit -> Path.GetFileName file = unit.Key + ".js")) then
                    File.Delete file

        let npm =
            if packagesChanged || not (Directory.Exists(at "node_modules")) then
                log "installing solid-js, @solidjs/compiler and rolldown"
                run "npm" [ "install"; "--no-audit"; "--no-fund"; "--loglevel=error" ]
            else
                { ExitCode = 0; Output = ""; TimedOut = false }

        if npm.ExitCode <> 0 then
            failed [ general true $"npm install failed:\n%s{tail npm.Output}" ]
        else

        let tools =
            if toolsChanged || not (File.Exists(at ".config/.restored")) then
                let restored = run "dotnet" [ "tool"; "restore" ]

                if restored.ExitCode = 0 then
                    File.WriteAllText(at ".config/.restored", options.FableVersion)

                restored
            else
                { ExitCode = 0; Output = ""; TimedOut = false }

        if tools.ExitCode <> 0 then
            failed [ general true $"dotnet tool restore failed:\n%s{tail tools.Output}" ]
        else

        log $"compiling %d{units.Length} page(s) of examples with Fable"

        let fable =
            run
                "dotnet"
                [
                    "fable"
                    "Docs.fsproj"
                    "-e"
                    ".fs.jsx"
                    "-c"
                    "Release"
                    "--optimize"
                    "--exclude"
                    "Partas.Solid.FablePlugin"
                ]

        let messages = fableMessages units fable.Output

        if fable.TimedOut then
            failed [ general true $"Fable did not finish within %O{options.Timeout}" ]
        elif fable.ExitCode <> 0 then
            let messages =
                if messages |> List.exists _.IsError then
                    messages
                else
                    messages @ [ general true $"Fable failed:\n%s{tail fable.Output}" ]

            failed messages
        else

        if Directory.Exists outDir then
            Directory.Delete(outDir, true)

        let manifest =
            let entries =
                units
                |> List.map (fun unit ->
                    let path = (at $"entries/%s{unit.Key}.js").Replace('\\', '/')
                    $"\"%s{unit.Key}\": \"%s{path}\""
                )
                |> String.concat ", "

            let outPath = outDir.Replace('\\', '/')
            let minify = if options.Minify then "true" else "false"
            $"""{{ "outDir": "%s{outPath}", "minify": %s{minify}, "entries": {{ %s{entries} }} }}"""

        File.WriteAllText(at "manifest.json", manifest)
        let bundled = run "node" [ "bundle.mjs"; "manifest.json" ]

        if bundled.ExitCode <> 0 then
            failed (messages @ [ general true $"bundling failed:\n%s{tail bundled.Output}" ])
        else

        File.WriteAllText(stampFile, key)

        {
            Files = readOutput outDir
            Messages = messages
            Fresh = true
        }
