namespace Nacara.Plugins

open System.Diagnostics
open Nacara.Core
open Nacara.Plugins.Internal

[<Struct>]
type TailwindCssOptions = {
    Binary: TailwindCssBinary.Strategy
}

[<RequireQualifiedAccess>]
module TailwindCss =
    open System.IO
    let defaults() =
        {
            Binary = TailwindCssBinary.Implicit(TailwindCssBinary.Version(4,3,3), TailwindCssBinary.Platform.Auto)
        }

    let private isNacaraStyleSheet path  =
        AbsolutePath.fileName path = "nacara.css"
    let private isTailwindStyleSheet path =
        use reader = File.OpenText(AbsolutePath.value path)
        let mutable isTailwind = false
        let mutable line = 0
        while not isTailwind && not reader.EndOfStream && line < 10 do
            if reader.ReadLine().Contains("tailwindcss") then isTailwind <- true
            line <- line + 1
        isTailwind

    let private modifyNacaraStyleSheet (path: AbsolutePath) =
        if not <| isNacaraStyleSheet path || isTailwindStyleSheet path then () else
        let lines = File.ReadAllLines(AbsolutePath.value path)
        File.WriteAllLines(AbsolutePath.value path, [|
            // "@import \"tailwindcss\";"
            "@layer theme, base, components, utilities;"
            "@import \"tailwindcss/theme.css\" layer(theme);"
            "@import \"tailwindcss/utilities.css\" layer(utilities);"
            for line in lines do
                if (not <| line.Contains("@import") || line.Contains("tailwindcss")) then line else
                line.Replace("@import \"", "@import \"" + (AbsolutePath.directory path |> AbsolutePath.value) + "/")
        |])



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
        let mutable reported = false
        interface IPlugin with
            member _.Name = "TailwindCss"
            member _.Configure(registry) =
                registry
                |> Registry.assetBundler {
                    AssetBundler.Name = "tailwindcss"
                    Extensions = [ ".css" ]
                    Bundle = fun context ->
                        binary.Value
                        |> Result.bind (fun binary ->
                                modifyNacaraStyleSheet context.Entry
                                let output = Path.GetTempFileName()
                                run binary options (AbsolutePath.value context.Entry) output
                            )
                }
                |> Registry.assetTransform {
                    AssetTransform.Name = "tailwindcss"
                    Extensions = [ ".css" ]
                    Transform = fun context ->
                        let report message =
                            if not reported then
                                reported <- true
                                context.Diagnostics.Add(
                                    Diagnostic.warning
                                        "tailwindcss-error"
                                        $"TailwindCss error: %s{message}"
                                    )
                        binary.Value
                        |> Result.mapError report
                        |> Result.toOption
                        |> Option.bind (fun binary ->
                            let tempIn = Path.GetTempFileName()
                            let tempOut = Path.GetTempFileName()
                            File.WriteAllText(tempIn, context.Content)
                            AbsolutePath.create tempIn
                            |> modifyNacaraStyleSheet
                            run binary options tempIn tempOut
                            |> Result.mapError report
                            |> Result.toOption
                            )
                        |> Option.defaultValue context.Content
                }

    let binaryStrategy value (options: TailwindCssOptions) =
        { options with Binary = value }

    let create () = TailwindCssPlugin(defaults()) :> IPlugin

    let createWith (configure: TailwindCssOptions -> TailwindCssOptions) = TailwindCssPlugin(configure <| defaults()) :> IPlugin

    let register (site: Site) = Site.plugin (create ()) site

    let registerWith (configure: TailwindCssOptions -> TailwindCssOptions) (site: Site) = Site.plugin (createWith configure) site

