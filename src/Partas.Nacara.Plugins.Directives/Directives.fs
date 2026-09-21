namespace Nacara.Plugins

open Markdig
open Nacara.Core

/// <summary>Puts declared directives into a build.</summary>
[<RequireQualifiedAccess>]
module Directives =

    /// <summary>The names claimed by more than one directive.</summary>
    let duplicates (directives: Directive list) =
        directives
        |> List.countBy _.Name
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map fst

    type private DirectivesPlugin(directives: Directive list) =
        interface IPlugin with
            member _.Name = "directives"

            member _.Configure registry =
                // The markdown plugin reads every IMarkdownExtension a plugin contributed,
                // which is the whole of how this joins the pipeline.
                registry |> Registry.extra (DirectiveExtension(directives) :> IMarkdownExtension)

    /// <summary>A plugin that adds these directives to the build.</summary>
    /// <param name="directives">What an author may write as <c>:::name</c>.</param>
    /// <exception cref="System.Exception">Two directives claim one name.</exception>
    let create (directives: Directive list) =
        match duplicates directives with
        | [] -> DirectivesPlugin(directives) :> IPlugin
        | clashing ->
            // Said now rather than at render time, when it would be one page's problem
            // and whichever directive happened to be first would silently win.
            failwith
                $"""More than one directive is called %s{String.concat ", " clashing}. \
                   A name selects exactly one directive, so the build cannot choose."""

    /// <summary>Add these directives to a site.</summary>
    let register (directives: Directive list) (site: Site) = Site.plugin (create directives) site
