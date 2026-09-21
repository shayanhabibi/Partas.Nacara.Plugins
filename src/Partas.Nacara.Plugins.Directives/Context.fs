namespace Nacara.Plugins

open Markdig.Extensions.CustomContainers
open Markdig.Syntax

/// <summary>The directives currently being rendered, innermost last.</summary>
/// <remarks>
/// Rendering is depth-first and one document at a time, so a plain stack is enough: while
/// a child renders, every directive enclosing it has been pushed and not yet popped.
/// </remarks>
type internal DirectiveStack() =
    let mutable entries: obj list = []

    /// <summary>Note that a directive's body is about to be rendered.</summary>
    member _.Push(arguments: obj) = entries <- arguments :: entries

    /// <summary>Note that it has finished.</summary>
    member _.Pop() =
        entries <-
            match entries with
            | _ :: rest -> rest
            | [] -> []

    /// <summary>The enclosing directives' arguments, nearest first.</summary>
    member _.Current = entries

    /// <summary>How many directives are open.</summary>
    member _.Depth = List.length entries

    /// <summary>Runs <paramref name="body"/> with <paramref name="arguments"/> pushed, guaranteeing
    /// the matching pop happens even if <paramref name="body"/> throws.</summary>
    /// <remarks>
    /// One instance of this stack is shared across every page in a build, so an unguarded push left
    /// on a thrown exception would leak into the next directive's rendering. Pairing push and pop
    /// here, rather than leaving callers to remember a try/finally, is what keeps that from happening.
    /// </remarks>
    member this.Using<'T>(arguments: obj, body: unit -> 'T) : 'T =
        this.Push(arguments)

        try
            body ()
        finally
            this.Pop()

/// <summary>Where a directive sits, read from the block tree.</summary>
module internal Context =

    /// <summary>Position among the siblings sharing this directive's name.</summary>
    /// <remarks>
    /// Taken from the tree rather than counted while rendering, because the tree already
    /// knows and a count would have to be reset correctly on every parent.
    /// </remarks>
    let siblingIndex (container: CustomContainer) =
        match container.Parent with
        | null -> 0
        | parent ->
            parent
            |> Seq.choose (function
                | :? CustomContainer as sibling when sibling.Info = container.Info -> Some sibling
                | _ -> None)
            |> Seq.toList
            |> List.tryFindIndex (fun sibling -> System.Object.ReferenceEquals(sibling, container))
            |> Option.defaultValue 0
