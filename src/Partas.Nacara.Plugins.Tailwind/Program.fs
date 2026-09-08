namespace Nacara.Plugins

open System.Diagnostics
open Nacara.Core
open Nacara.Plugins.Internal

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
    /// <defaultValue><c>TailwindCssBinary.Implicit(TailwindCssBinary.Version(4,3,3), TailwindCssBinary.Platform.Auto)</c></defaultValue>
    Binary: TailwindCssBinary.Strategy
    /// <summary>
    /// The extensions that will be processed by the tailwindcss plugin.
    /// This intended to be used to filter tailwindcss processing for specific files using
    /// a composite extension pattern:<br/> <c>&lt;FileName>.*.css</c>
    /// </summary>
    /// <defaultValue><c>[".css"]</c></defaultValue>
    TargetExtensions: string list
    /// <summary>
    /// The header that is injected into the target entry style sheet to import the tailwindcss library
    /// if it is not already imported.
    /// </summary>
    /// <remarks>
    /// If the header is empty, it will automatically inject <c>@import "tailwindcss";</c>
    /// </remarks>
    /// <defaultValue>
    /// <code lang="fsharp">
    /// [
    ///     "@layer theme, base, components, utilities;"
    ///     "@import \"tailwindcss/theme.css\" layer(theme);"
    ///     "@import \"tailwindcss/utilities.css\" layer(utilities);"
    /// ]
    /// </code>
    /// </defaultValue>
    TailwindEntryHeader: string list
    /// <summary>
    /// The footer is injected at the end of the entry file.
    /// </summary>
    /// <defaultValue><c>[]</c></defaultValue>
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
    /// </remarks>
    /// <defaultValue><c>fun { AbsoluteDirPath = d; Import = i } -> System.IO.Path.Combine(d, i) |> Ok</c></defaultValue>
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
                "@layer theme, base, components, utilities;"
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
        let header =
            match header with
            | [] -> [ "@import \"tailwindcss\";" ]
            | header -> header
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
                        let rest = line.Substring(secondQuoteIdx)
                        match makeImportStatement line[firstQuoteIdx + 1..secondQuoteIdx - 1] |> referenceHandler with
                        | Ok newPath ->
                            if newPath.EndsWith(";") then $"@import {newPath}"
                            else $"{line[0..firstQuoteIdx]}{newPath}{rest}"
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
            |> Array.append (List.toArray header)
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

    let create () = TailwindCssPlugin(defaults()) :> IPlugin

    let createWith (configure: TailwindCssOptions -> TailwindCssOptions) = TailwindCssPlugin(configure <| defaults()) :> IPlugin

    let register (site: Site) = Site.plugin (create ()) site

    let registerWith (configure: TailwindCssOptions -> TailwindCssOptions) (site: Site) = Site.plugin (createWith configure) site

