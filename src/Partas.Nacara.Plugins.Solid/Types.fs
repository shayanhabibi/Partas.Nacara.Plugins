namespace Nacara.Plugins

open System

/// <summary>What a solid fence puts on the page.</summary>
[<RequireQualifiedAccess>]
type SolidShow =
    /// The code, then what it renders.
    | Both
    /// The code alone, although it still compiles with the page.
    | Code
    /// What it renders, without the code.
    | Output
    /// What it renders, without the code or the box around it, as though it were part of the page.
    | Inline

/// <summary>How the code of a solid fence is read.</summary>
[<RequireQualifiedAccess>]
type SolidCellKind =
    /// Module-level declarations. Nothing renders unless the fence names a component with
    /// <c>render=</c>.
    | Declarations of render: string option
    /// A single expression, rendered as the body of a component.
    | Expression
    /// Declarations the page needs but the reader does not: compiled, never shown.
    | Setup
    /// An expression written in the prose as a code span, rendered where it stands.
    | Use of column: int

/// <summary>One solid fence, as the scanner found it.</summary>
type SolidCell =
    {
        /// Unique within its page: <c>c1</c>, <c>c2</c>, or what the fence named with <c>id=</c>.
        Id: string
        Kind: SolidCellKind
        Show: SolidShow
        Code: string
        /// The line of the body the code starts on, counting from one.
        Line: int
        /// Whether the page shows the JSX Fable made of the cell, under it.
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
        /// First generated line, counting from one.
        Generated: int
        Length: int
        /// The body line the first generated line came from.
        Body: int
        /// Columns the generator moved the code right by: negative when it moved left.
        Indent: int
    }

/// <summary>Everything the compile step needs of one page.</summary>
type SolidPageUnit =
    {
        /// Names the module, the entry and the bundle: <c>p1a2b3c4d</c>.
        Key: string
        /// The page's source, which diagnostics point at.
        Source: string option
        /// Added to a body line to reach the line of the file.
        BodyLine: int
        Cells: SolidCell list
        /// The generated F# module.
        Code: string
        Spans: SolidLineSpan list
    }

/// <summary>Configures the solid examples plugin.</summary>
type SolidExamplesOptions =
    {
        /// <summary>The Partas.Solid package the examples compile against.</summary>
        /// <defaultValue><c>"3.*"</c></defaultValue>
        PartasVersion: string
        /// <summary>Extra NuGet sources, tried alongside nuget.org. A local folder feed works.</summary>
        /// <defaultValue><c>[]</c></defaultValue>
        Feeds: string list
        /// <summary>
        /// More NuGet packages the examples compile against, as (id, version): F# bindings, say.
        /// </summary>
        /// <remarks>An entry for <c>Partas.Solid</c> is ignored: <c>PartasVersion</c> sets it.</remarks>
        /// <defaultValue><c>[]</c></defaultValue>
        NuGetPackages: (string * string) list
        /// <summary>
        /// More npm packages the examples can import, as (name, version): a framework-free library
        /// such as <c>animejs</c>, say.
        /// </summary>
        /// <remarks>
        /// Entries for the packages the plugin installs itself are ignored. <c>solid-js</c> and
        /// <c>@solidjs/web</c> stay pinned to <c>SolidVersion</c> for every package, so a library
        /// built on Solid 1 cannot bring a second, older Solid with it, and does not work.
        /// </remarks>
        /// <defaultValue><c>[]</c></defaultValue>
        NpmPackages: (string * string) list
        /// <defaultValue><c>"5.13.0"</c></defaultValue>
        FableVersion: string
        /// <summary>The version of <c>solid-js</c>, <c>@solidjs/web</c> and <c>@solidjs/compiler</c>.</summary>
        /// <defaultValue><c>"2.0.0-rc.9"</c></defaultValue>
        SolidVersion: string
        /// <defaultValue><c>"1.2.11"</c></defaultValue>
        RolldownVersion: string
        /// <summary>The word in a fence's info string that makes it a solid example.</summary>
        /// <defaultValue><c>"solid"</c></defaultValue>
        FenceToken: string
        /// <summary>Lines at the top of every page's module.</summary>
        /// <defaultValue><c>[ "open Partas.Solid"; "open Fable.Core"; "open Fable.Core.JsInterop" ]</c></defaultValue>
        Prelude: string list
        /// <summary>Where the bundles are written, relative to the output directory.</summary>
        /// <defaultValue><c>"_partas/solid"</c></defaultValue>
        OutputPath: string
        /// <summary>Where the generated project lives, relative to the project root.</summary>
        /// <remarks>A dotted directory, so watch mode does not rebuild when the plugin writes it.</remarks>
        /// <defaultValue><c>".nacara/partas-solid"</c></defaultValue>
        WorkspacePath: string
        /// <defaultValue><c>true</c></defaultValue>
        Minify: bool
        /// <summary>
        /// Show the JSX Fable made of every example, not only of fences marked <c>jsx</c>.
        /// </summary>
        /// <defaultValue><c>false</c></defaultValue>
        ShowJsx: bool
        /// <summary>How long one tool run may take before it is abandoned.</summary>
        /// <defaultValue>Five minutes.</defaultValue>
        Timeout: TimeSpan
    }
