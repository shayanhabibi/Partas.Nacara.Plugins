namespace Nacara.Plugins.Internal

open System
open System.Text
open System.Text.RegularExpressions
open Nacara.Plugins

/// <summary>What scanning a page's body found.</summary>
type SolidScan =
    {
        /// The body with every solid fence rewritten to what the page shows.
        Body: string
        Cells: SolidCell list
        /// Problems with how a fence was written, by body line.
        Problems: (int * string) list
    }

/// <summary>
/// Finds solid fences in markdown and rewrites them to a plain fence and a placeholder.
/// </summary>
/// <remarks>Line based rather than a markdown parse: all it has to know is where fences open and
/// close, and it runs before the markdown plugin has seen the page.</remarks>
[<RequireQualifiedAccess>]
module SolidScan =

    let private opening =
        Regex(@"^(?<indent> {0,3})(?<fence>`{3,}|~{3,})\s*(?<info>.*)$", RegexOptions.Compiled)

    let private languages = set [ "fsharp"; "fs"; "f#" ]

    let private closes (fence: string) (line: string) =
        let trimmed = line.Trim()

        trimmed.Length >= fence.Length
        && trimmed |> Seq.forall (fun c -> c = fence[0])

    /// <summary>Whether code is declarations rather than an expression to render.</summary>
    /// <remarks>Decided by the last line at column zero, so bindings that lead up to an expression
    /// still make an expression.</remarks>
    let declares (code: string) =
        code.Split('\n')
        |> Array.map _.TrimEnd('\r')
        |> Array.filter (fun line ->
            line <> ""
            && not (Char.IsWhiteSpace line[0])
            && not (line.StartsWith "//")
            && not (line.StartsWith "(*")
            // A closing bracket at column zero ends something above it rather than starting anything.
            && not (")]}|".Contains line[0])
        )
        |> Array.tryLast
        |> Option.map (fun line ->
            [ "let "; "type "; "open "; "module "; "[<"; "#nowarn"; "exception "; "and " ]
            |> List.exists line.StartsWith
        )
        |> Option.defaultValue true

    /// <summary>The placeholder a cell mounts into.</summary>
    let placeholder (pageKey: string) (cell: SolidCell) =
        $"""<div class="partas-solid" data-partas-page="%s{pageKey}" data-partas-cell="%s{cell.Id}"></div>"""

    type private Tokens =
        {
            Setup: bool
            Render: string option
            Show: SolidShow option
            Id: string option
            Rest: string list
            Problems: string list
        }

    let private readTokens (fenceToken: string) (tokens: string list) =
        let value (token: string) =
            token.Substring(token.IndexOf '=' + 1).Trim('"', '\'')

        let start =
            {
                Setup = false
                Render = None
                Show = None
                Id = None
                Rest = []
                Problems = []
            }

        (start, tokens)
        ||> List.fold (fun state token ->
            match token with
            | token when token = fenceToken -> state
            | "setup" -> { state with Setup = true }
            | token when token.StartsWith "render=" -> { state with Render = Some(value token) }
            | token when token.StartsWith "id=" -> { state with Id = Some(value token) }
            | token when token.StartsWith "show=" ->
                match value token with
                | "both" -> { state with Show = Some SolidShow.Both }
                | "code" -> { state with Show = Some SolidShow.Code }
                | "output" -> { state with Show = Some SolidShow.Output }
                | other ->
                    { state with
                        Problems = state.Problems @ [ $"show=%s{other} is not one of both, code or output" ]
                    }
            | token -> { state with Rest = state.Rest @ [ token ] }
        )

    /// <summary>Scan a page's body.</summary>
    /// <param name="fenceToken">The word that marks a fence as a solid example.</param>
    /// <param name="pageKey">Written into each placeholder, so the loader knows which bundle to load.</param>
    /// <param name="body">The page's markdown.</param>
    let scan (fenceToken: string) (pageKey: string) (body: string) =
        let lines = body.Split('\n') |> Array.map _.TrimEnd('\r')
        let output = StringBuilder()
        let cells = ResizeArray<SolidCell>()
        let problems = ResizeArray<int * string>()
        let emit (line: string) = output.Append(line).Append('\n') |> ignore
        let mutable index = 0

        while index < lines.Length do
            let line = lines[index]
            let matched = opening.Match line

            if not matched.Success then
                emit line
                index <- index + 1
            else
                let fence = matched.Groups["fence"].Value
                let info = matched.Groups["info"].Value.Trim()

                let words =
                    info.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

                let closing =
                    seq { index + 1 .. lines.Length - 1 }
                    |> Seq.tryFind (fun at -> closes fence lines[at])
                    |> Option.defaultValue lines.Length

                let isSolid =
                    match words with
                    | language :: rest ->
                        languages.Contains(language.ToLowerInvariant()) && List.contains fenceToken rest
                    | [] -> false

                if not isSolid then
                    for at in index .. min closing (lines.Length - 1) do
                        emit lines[at]

                    index <- closing + 1
                else
                    let language = List.head words
                    let tokens = readTokens fenceToken (List.tail words)
                    let code = lines[index + 1 .. closing - 1] |> String.concat "\n"
                    let bodyLine = index + 2

                    for problem in tokens.Problems do
                        problems.Add(index + 1, problem)

                    let kind =
                        match tokens.Setup, tokens.Render with
                        | true, _ -> SolidCellKind.Setup
                        | false, Some name -> SolidCellKind.Declarations(Some name)
                        | false, None when declares code -> SolidCellKind.Declarations None
                        | false, None -> SolidCellKind.Expression

                    let id =
                        tokens.Id |> Option.defaultValue $"c%d{cells.Count + 1}"

                    if cells |> Seq.exists (fun cell -> cell.Id = id) then
                        problems.Add(index + 1, $"id=%s{id} is already used on this page")

                    let cell =
                        {
                            Id = id
                            Kind = kind
                            Show = tokens.Show |> Option.defaultValue SolidShow.Both
                            Code = code
                            Line = bodyLine
                        }

                    cells.Add cell

                    let shown =
                        match cell.Kind, cell.Show with
                        | SolidCellKind.Setup, _
                        | _, SolidShow.Output -> false
                        | _ -> true

                    if shown then
                        emit (
                            matched.Groups["indent"].Value
                            + fence
                            + String.concat " " (language :: tokens.Rest)
                        )

                        for at in index + 1 .. closing - 1 do
                            emit lines[at]

                        emit (matched.Groups["indent"].Value + fence)

                    if cell.Mounts then
                        emit ""
                        emit (placeholder pageKey cell)
                        emit ""
                    elif not shown then
                        // A hidden fence leaves blank lines behind, so what follows stays on the line
                        // it was written on.
                        for _ in index .. min closing (lines.Length - 1) do
                            emit ""

                    index <- closing + 1

        {
            Body = output.ToString().TrimEnd('\n') + (if body.EndsWith "\n" then "\n" else "")
            Cells = List.ofSeq cells
            Problems = List.ofSeq problems
        }
