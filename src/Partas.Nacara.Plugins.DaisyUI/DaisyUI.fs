namespace Nacara.Plugins

open System.Diagnostics
open Nacara.Core
open Nacara.Plugins.Internal

[<Struct>]
type DaisyUIOptions = {
    /// <summary>
    /// The strategy to use to find the tailwindcss binary. <c>Implicit</c> strategies will download the binary to a cache using
    /// the provided version and platform parameters.
    /// </summary>
    /// <defaultValue><c>TailwindCssBinary.Implicit(TailwindCssBinary.Version(4,3,3), TailwindCssBinary.Platform.Auto)</c></defaultValue>
    TailwindBinary: TailwindCssBinary.Strategy
    DaisyUIVersion: TailwindCssBinary.Version
    /// <summary>
    /// The extensions that will be processed by the tailwindcss plugin.
    /// This intended to be used to filter tailwindcss processing for specific files using
    /// a composite extension pattern:<br/> <c>&lt;FileName>.*.css</c>
    /// </summary>
    /// <defaultValue><c>[".css"]</c></defaultValue>
    TailwindTargetExtensions: string list
    /// <summary>
    /// The header that is injected into the target entry style sheet to import the tailwindcss library
    /// if it is not already imported. The plugin will replace <c>&lt;DAISY_UI_MJS></c> with the actual installation.
    /// </summary>
    /// <remarks>
    /// If the header is empty, it will automatically inject <c>@import "tailwindcss";</c>
    /// and <c>@plugin "&lt;DAISY_UI_MJS>";</c>.
    /// <para>Use <c>&lt;DAISY_UI_THEME_MJS></c> if you want to use DaisyUI themes.</para>
    /// </remarks>
    /// <defaultValue>
    /// <code lang="fsharp">
    /// [
    ///     "@layer theme, base, components, utilities;"
    ///     "@import \"tailwindcss/theme.css\" layer(theme);"
    ///     "@import \"tailwindcss/utilities.css\" layer(utilities);"
    ///     "@plugin \"&lt;DAISY_UI_MJS>\";"
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
module DaisyUI =
    open System.IO
    let [<Literal>] Token = "<DAISY_UI_MJS>"
    let [<Literal>] ThemeToken = "<DAISY_UI_THEME_MJS>"
    let defaults() =
        let tailwindDefaults = TailwindCss.defaults()
        {
            ReferenceHandler = tailwindDefaults.ReferenceHandler
            TailwindTargetExtensions = tailwindDefaults.TargetExtensions
            TailwindEntryHeader = [
                yield! tailwindDefaults.TailwindEntryHeader
                yield $"@plugin \"{Token}\";"
            ]
            TailwindEntryFooter = tailwindDefaults.TailwindEntryFooter
            TailwindBinary = tailwindDefaults.Binary
            DaisyUIVersion = TailwindCssBinary.Latest
        }
    let toDaisylessTailwindCssOptions (options: DaisyUIOptions) =
        {
            TailwindCssOptions.Binary = options.TailwindBinary
            TargetExtensions = options.TailwindTargetExtensions
            TailwindEntryHeader =
                options.TailwindEntryHeader
                |> List.choose (function
                    | line when line.Contains(Token) || line.Contains(ThemeToken) -> None
                    | line -> Some line
                    )
            TailwindEntryFooter =
                options.TailwindEntryFooter
                |> List.choose (function
                    | line when line.Contains(Token) || line.Contains(ThemeToken) -> None
                    | line -> Some line
                    )
            ReferenceHandler = options.ReferenceHandler
        }
    let toTailwindCssOptions (files: DaisyUIInstall.Paths) (options: DaisyUIOptions) =
        {
            TailwindCssOptions.Binary = options.TailwindBinary
            TargetExtensions = options.TailwindTargetExtensions
            TailwindEntryHeader =
                options.TailwindEntryHeader
                |> List.map _.Replace(Token, files.``daisyui.mjs``).Replace(ThemeToken, files.``daisyui-theme.mjs``)
            TailwindEntryFooter = options.TailwindEntryFooter
            ReferenceHandler = options.ReferenceHandler
        }
    let private files (options: DaisyUIOptions) =
        lazy (
            if isNull (box options.DaisyUIVersion)
            then Error "DaisyUI version was not configured."
            else DaisyUIInstall.resolveWith options.DaisyUIVersion
            )

    type private DaisyUIPlugin(options: DaisyUIOptions) =
        let files = files options
        let tailwind files = toTailwindCssOptions files options
        interface IPlugin with
            member _.Name = "DaisyUI"
            member _.Configure(registry) =
                fun _ ->
                    files.Value
                    |> Result.mapError (printfn "%s" >> fun _ -> printfn "daisyui: daisy css not available; defaulting to tailwind-only")
                    |> Result.map tailwind
                    |> Result.toOption
                    |> Option.defaultWith (fun () -> toDaisylessTailwindCssOptions options)
                |> TailwindCss.createWith
                |> _.Configure(registry)

    let tailwindBinaryStrategy value (options: DaisyUIOptions) = { options with TailwindBinary = value }
    let version value (options: DaisyUIOptions) = { options with DaisyUIVersion = value }
    let targetExtensions value (options: DaisyUIOptions) = { options with TailwindTargetExtensions = value }
    let header value (options: DaisyUIOptions) = { options with TailwindEntryHeader = value }
    let footer value (options: DaisyUIOptions) = { options with TailwindEntryFooter = value }
    let referenceHandler value (options: DaisyUIOptions) = { options with ReferenceHandler = value }

    let create () = DaisyUIPlugin(defaults()) :> IPlugin
    let createWith (configure: DaisyUIOptions -> DaisyUIOptions) = DaisyUIPlugin(configure (defaults())) :> IPlugin
    let register (site: Site) = Site.plugin (create ()) site
    let registerWith (configure: DaisyUIOptions -> DaisyUIOptions) (site: Site) = Site.plugin (createWith configure) site
