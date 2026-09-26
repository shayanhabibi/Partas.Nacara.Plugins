namespace Nacara.Plugins

open System.Diagnostics
open Nacara.Core
open Nacara.Plugins.Internal
open Partas.Nacara.Theme

[<Struct>]
type TailwindCssImportStatement = {
    AbsoluteDirPath: string
    Import: string
}

[<Struct>]
type TailwindCssOptions = {
    /// <summary>
    /// The strategy to use to find the tailwindcss binary. <c>Implicit</c> strategies will download the binary to a cache using
    /// the provided version and platform parameters.
    /// </summary>
    /// <remarks>Defaults to <c>TailwindCssBinary.Implicit(TailwindCssBinary.Version(4,3,3), TailwindCssBinary.Platform.Auto)</c>.</remarks>
    Binary: TailwindCssBinary.Strategy
    /// <summary>
    /// The extensions that will be processed by the tailwindcss plugin.
    /// This intended to be used to filter tailwindcss processing for specific files using
    /// a composite extension pattern: <c>&lt;FileName&gt;.*.css</c>
    /// </summary>
    /// <remarks>Defaults to <c>[".css"]</c>.</remarks>
    TargetExtensions: string list
    /// <summary>
    /// The header that is injected into the target entry style sheet to import the tailwindcss library
    /// if it is not already imported.
    /// </summary>
    /// <remarks>
    /// If the header is empty, it will automatically inject <c>@import "tailwindcss";</c>
    /// <para>Defaults to:</para>
    /// <code lang="fsharp">
    /// [
    ///     "@layer theme, base, components, utilities;"
    ///     "@import \"tailwindcss/theme.css\" layer(theme);"
    ///     "@import \"tailwindcss/utilities.css\" layer(utilities);"
    /// ]
    /// </code>
    /// </remarks>
    TailwindEntryHeader: string list
    /// <summary>
    /// The footer is injected at the end of the entry file.
    /// </summary>
    /// <remarks>Defaults to <c>[]</c>.</remarks>
    TailwindEntryFooter: string list
    // This is required for tailwind to properly handle references.
    /// <summary>
    /// <para>A delegate that is used to modify the import statements in the entry style sheet when bundling.</para>
    /// <para>The delegate is called for each import statement found in the entry style sheet, and
    /// receives the absolute path to the entry style sheet directory and the import string to modify
    /// without quotations.</para><para>Return the final path, without quotations.</para>
    /// </summary>
    /// <remarks>
    /// If the returned string ends with <c>;</c>, then the string is injected verbatim after the <c>@import &lt;returnValue></c>
    /// statement.
    /// <para>Defaults to <c>fun { AbsoluteDirPath = d; Import = i } -> System.IO.Path.Combine(d, i) |> Ok</c>.</para>
    /// </remarks>
    ReferenceHandler: TailwindCssImportStatement -> Result<string, string>
}

[<RequireQualifiedAccess>]
module TailwindCss =
    open System.IO
    let defaults() =
        {
            Binary = TailwindCssBinary.Implicit(TailwindCssBinary.Version(4,3,3), TailwindCssBinary.Platform.Auto)
            TargetExtensions = [ ".css" ]
            TailwindEntryHeader = [
                "@import \"tailwindcss/theme.css\" layer(theme);"
                "@import \"tailwindcss/utilities.css\" layer(utilities);"
            ]
            TailwindEntryFooter = []
            ReferenceHandler = fun { AbsoluteDirPath = d; Import = i } -> Path.Combine(d, i) |> Ok
        }

    let private styleSheetBundler
        { TailwindEntryHeader = header; TailwindEntryFooter = footer; ReferenceHandler = referenceHandler }
        (path: AbsolutePath) =
        let dirPath = AbsolutePath.directory path
        let makeImportStatement (import: string) = { AbsoluteDirPath = AbsolutePath.value dirPath; Import = import }

        // Read while bundling rather than while configuring: the theme may well have been
        // registered after this plugin was, and by now it has had its say either way.
        let themeLayers = ThemeLayers.current()

        let header =
            match header with
            | [] -> [ "@import \"tailwindcss\";" ]
            | header -> header

        // A theme that names its layers is ordered against Tailwind's by name. One that does not
        // gets the whole of it swept into a single 'nacara' layer, which is the most that can be
        // said about a stylesheet that says nothing about itself.
        let layerOrder =
            let names =
                match themeLayers with
                | [] -> [ "nacara" ]
                | names -> names

            [ $"""@layer {String.concat ", " names}, theme, base, components, utilities;""" ]
        let entryPath = AbsolutePath.value path
        if not (File.Exists entryPath) then Error $"tailwindcss: entry path not found during bundling stage: {entryPath}" else
        let errors = ResizeArray()
        let lines =
            File.ReadAllLines(entryPath)
            |> Array.map (function
                | line when line.StartsWith("@import") && not (line.Contains("tailwindcss")) ->
                    try
                        let firstQuoteIdx = line.IndexOf('"')
                        let secondQuoteIdx = line.IndexOf('"', firstQuoteIdx + 1)
                        let rest = line.Substring(secondQuoteIdx).TrimEnd(';')
                        match makeImportStatement line[firstQuoteIdx + 1..secondQuoteIdx - 1] |> referenceHandler with
                        | Ok newPath ->
                            if newPath.EndsWith(";") then $"@import {newPath}"
                            elif not themeLayers.IsEmpty then
                                // The theme put each of its parts in a layer already. Wrapping the
                                // lot in one more would bury them all under it.
                                $"{line[0..firstQuoteIdx]}{newPath}{rest};"
                            else $"{line[0..firstQuoteIdx]}{newPath}{rest} layer(nacara);"
                        | Error error ->
                            $"tailwindcss: error while bundling: %s{error}"
                            |> errors.Add
                            line
                    with e ->
                        $"tailwindcss: error while bundling: unexpected css import statement '{line}'"
                        |> errors.Add
                        $"           : {e.Message}"
                        |> errors.Add
                        line
                | line -> line
                )
            |> Array.append (List.toArray (layerOrder @ header))
        File.WriteAllLines(entryPath, footer |> List.toArray |> Array.append lines)
        if errors.Count > 0 then Error (String.concat "\n" errors) else
        Ok ()

    let private binary (options: TailwindCssOptions) =
        lazy
            (
            if isNull (box options.Binary) then
                Error "TailwindCss binary strategy was not configured."
            else
                match options.Binary with
                | TailwindCssBinary.Explicit binary when File.Exists(binary) ->
                    Ok binary
                | TailwindCssBinary.Explicit binary ->
                    Error $"TailwindCss binary not found at: {binary}"
                | TailwindCssBinary.Implicit _ as strategy ->
                    TailwindCssBinary.resolveWith strategy
            )

    let private run (binary: string) (_options: TailwindCssOptions) (cssFile: string) (outputFile: string) =
        if FileInfo(cssFile).Exists |> not then Error $"CSS file not found: {cssFile}" else
        try
            let startInfo =
                ProcessStartInfo(
                    binary,
                    $"-i \"%s{cssFile}\" -o \"%s{outputFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                    )
            use tailwindcss = Process.Start startInfo
            let error = tailwindcss.StandardError.ReadToEnd()
            tailwindcss.WaitForExit()

            if tailwindcss.ExitCode = 0 && File.Exists outputFile then
                Ok(File.ReadAllText outputFile)
            else
                Error(error.Trim())
        with exn ->
            Error exn.Message


    type private TailwindCssPlugin(options: TailwindCssOptions) =
        let binary = binary options
        interface IPlugin with
            member _.Name = "TailwindCss"
            member _.Configure(registry) =
                registry
                |> Registry.assetBundler {
                    AssetBundler.Name = "tailwindcss"
                    Extensions = options.TargetExtensions
                    Bundle = fun context ->
                        // try fetch binary
                        binary.Value
                        |> Result.bind (fun binary ->
                            // try modify css
                            styleSheetBundler options context.Entry
                            |> Result.map (fun _ -> binary)
                            )
                        |> Result.bind (fun binary ->
                            // try write output
                            let output = Path.GetTempFileName()
                            run binary options (AbsolutePath.value context.Entry) output
                            )
                }

    let binaryStrategy value (options: TailwindCssOptions) =
        { options with Binary = value }
    let targetExtensions value (options: TailwindCssOptions) = { options with TargetExtensions = value }
    let header value (options: TailwindCssOptions) = { options with TailwindEntryHeader = value }
    let footer value (options: TailwindCssOptions) = { options with TailwindEntryFooter = value }
    let referenceHandler value (options: TailwindCssOptions) = { options with ReferenceHandler = value }

    let create () = TailwindCssPlugin(defaults()) :> IPlugin

    let createWith (configure: TailwindCssOptions -> TailwindCssOptions) = TailwindCssPlugin(configure <| defaults()) :> IPlugin

    let register (site: Site) = Site.plugin (create ()) site

    let registerWith (configure: TailwindCssOptions -> TailwindCssOptions) (site: Site) = Site.plugin (createWith configure) site

