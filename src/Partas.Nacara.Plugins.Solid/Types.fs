namespace Nacara.Plugins

open System

/// <summary>What a solid fence puts on the page.</summary>
[<RequireQualifiedAccess>]
type SolidShow =
    /// <summary>The code, then what it renders.</summary>
    | Both
    /// <summary>The code alone, although it still compiles with the page.</summary>
    | Code
    /// <summary>What it renders, without the code.</summary>
    | Output
    /// <summary>What it renders, without the code or the box around it, as though it were part of the page.</summary>
    | Inline

/// <summary>How the code of a solid fence is read.</summary>
[<RequireQualifiedAccess>]
type SolidCellKind =
    /// <summary>
    /// Module-level declarations. Nothing renders unless the fence names a component with
    /// <c>render=</c>.
    /// </summary>
    | Declarations of render: string option
    /// <summary>A single expression, rendered as the body of a component.</summary>
    | Expression
    /// <summary>Declarations the page needs but the reader does not: compiled, never shown.</summary>
    | Setup
    /// <summary>An expression written in the prose as a code span, rendered where it stands.</summary>
    | Use of column: int

/// <summary>One solid fence, as the scanner found it.</summary>
type SolidCell =
    {
        /// <summary>Unique within its page: <c>c1</c>, <c>c2</c>, or what the fence named with <c>id=</c>.</summary>
        Id: string
        Kind: SolidCellKind
        Show: SolidShow
        Code: string
        /// <summary>The line of the body the code starts on, counting from one.</summary>
        Line: int
        /// <summary>Whether the page shows the JSX Fable made of the cell, under it.</summary>
        Jsx: bool
    }

    /// <summary>Whether a placeholder is mounted for it.</summary>
    member this.Mounts =
        match this.Kind with
        | SolidCellKind.Expression
        | SolidCellKind.Declarations(Some _) -> this.Show <> SolidShow.Code
        | SolidCellKind.Use _ -> true
        | SolidCellKind.Declarations None
        | SolidCellKind.Setup -> false

/// <summary>A run of generated lines that came from a page's body.</summary>
type SolidLineSpan =
    {
        /// <summary>First generated line, counting from one.</summary>
        Generated: int
        Length: int
        /// <summary>The body line the first generated line came from.</summary>
        Body: int
        /// <summary>Columns the generator moved the code right by: negative when it moved left.</summary>
        Indent: int
    }

/// <summary>Everything the compile step needs of one page.</summary>
type SolidPageUnit =
    {
        /// <summary>Names the module, the entry and the bundle: <c>p1a2b3c4d</c>.</summary>
        Key: string
        /// <summary>The page's source, which diagnostics point at.</summary>
        Source: string option
        /// <summary>Added to a body line to reach the line of the file.</summary>
        BodyLine: int
        Cells: SolidCell list
        /// <summary>The generated F# module.</summary>
        Code: string
        Spans: SolidLineSpan list
    }

/// <summary>Configures the solid examples plugin.</summary>
type SolidExamplesOptions =
    {
        /// <summary>The Partas.Solid package the examples compile against.</summary>
        /// <remarks>Defaults to <c>"3.*"</c>.</remarks>
        PartasVersion: string
        /// <summary>Extra NuGet sources, tried alongside nuget.org. A local folder feed works.</summary>
        /// <remarks>Defaults to <c>[]</c>.</remarks>
        Feeds: string list
        /// <summary>The Fable the examples compile with.</summary>
        /// <remarks>Defaults to <c>"5.13.0"</c>.</remarks>
        FableVersion: string
        /// <summary>The version of <c>solid-js</c>, <c>@solidjs/web</c> and <c>@solidjs/compiler</c>.</summary>
        /// <remarks>Defaults to <c>"2.0.0-rc.9"</c>.</remarks>
        SolidVersion: string
        /// <summary>The version of <c>rolldown</c>, which bundles each page's examples.</summary>
        /// <remarks>Defaults to <c>"1.2.11"</c>.</remarks>
        RolldownVersion: string
        /// <summary>The word in a fence's info string that makes it a solid example.</summary>
        /// <remarks>Defaults to <c>"solid"</c>.</remarks>
        FenceToken: string
        /// <summary>Lines at the top of every page's module.</summary>
        /// <remarks>Defaults to <c>[ "open Partas.Solid"; "open Fable.Core"; "open Fable.Core.JsInterop" ]</c>.</remarks>
        Prelude: string list
        /// <summary>Where the bundles are written, relative to the output directory.</summary>
        /// <remarks>Defaults to <c>"_partas/solid"</c>.</remarks>
        OutputPath: string
        /// <summary>Where the generated project lives, relative to the project root.</summary>
        /// <remarks>
        /// A dotted directory, so watch mode does not rebuild when the plugin writes it. Defaults to <c>".nacara/partas-solid"</c>.
        /// </remarks>
        WorkspacePath: string
        /// <summary>Whether the bundles are minified.</summary>
        /// <remarks>Defaults to <c>true</c>.</remarks>
        Minify: bool
        /// <summary>
        /// Show the JSX Fable made of every example, not only of fences marked <c>jsx</c>.
        /// </summary>
        /// <remarks>Defaults to <c>false</c>.</remarks>
        ShowJsx: bool
        /// <summary>How long one tool run may take before it is abandoned.</summary>
        /// <remarks>Defaults to five minutes.</remarks>
        Timeout: TimeSpan
    }
