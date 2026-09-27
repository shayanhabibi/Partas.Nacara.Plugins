namespace Nacara.Plugins.Internal

// Compiled into more than one plugin as a linked file, so it stays internal: two assemblies
// exposing the same public module would be ambiguous to anyone referencing both.

open System
open System.IO
open Nacara.Core
open YamlDotNet.RepresentationModel

/// <summary>What a plugin reads out of a page's front matter without knowing its type.</summary>
/// <remarks>
/// A collection decodes front matter into a type of the site's choosing, which a plugin cannot
/// name. The YAML itself is still in the source file, and the registry knows how each format
/// carries it, so a plugin reads the keys it cares about from there.
/// </remarks>
module internal RawFrontMatter =

    /// <summary>The front matter of a page as a YAML mapping, when it has one.</summary>
    /// <remarks>A generated page, a page with no block, and a block that does not parse all
    /// answer <c>None</c>: the collection has already reported anything malformed.</remarks>
    let read (formats: FrontMatterFormat list) (page: Page) : YamlMappingNode option =
        match page.SourceFile with
        | None -> None
        | Some file ->
            let path = AbsolutePath.value file

            match formats |> List.tryFindBack (fun format -> List.contains page.Format format.Extensions) with
            | None -> None
            | Some _ when not (File.Exists path) -> None
            | Some format ->
                match FrontMatter.extract format (File.ReadAllText path) with
                | Error _ -> None
                | Ok block ->
                    match Yaml.parse block.Yaml with
                    | Ok(Some(:? YamlMappingNode as mapping)) -> Some mapping
                    | _ -> None

    /// <summary>The node under the first of <c>keys</c> the mapping has.</summary>
    let tryFind (keys: string list) (mapping: YamlMappingNode) =
        keys
        |> List.tryPick (fun key ->
            match mapping.Children.TryGetValue(YamlScalarNode key) with
            | true, node -> Some node
            | _ -> None
        )

    /// <summary>A scalar's text, when the node is one and is not blank.</summary>
    let scalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as scalar when not (String.IsNullOrWhiteSpace scalar.Value) ->
            Some(scalar.Value.Trim())
        | _ -> None

    /// <summary>The text under the first of <c>keys</c>, when it is a non-blank scalar.</summary>
    let tryString (keys: string list) (mapping: YamlMappingNode) =
        tryFind keys mapping |> Option.bind scalar

/// <summary>Living with being a catch-all content transform.</summary>
module internal CatchAll =

    /// <summary>The page as the engine would have left it had this transform not claimed it.</summary>
    /// <remarks>
    /// A page no transform claims has its body taken as its html. A transform naming no
    /// extensions claims every page, so a page of a format nothing renders would otherwise
    /// render empty. The engine's own rule is applied here instead, when only catch-alls claim it.
    /// </remarks>
    let passThrough (registry: Registry) (page: Page) =
        let rendered =
            registry.Transforms
            |> List.exists (fun transform -> List.contains page.Format transform.Extensions)

        if rendered || not (String.IsNullOrEmpty page.Html) then
            page
        else
            { page with Html = page.Body }

/// <summary>Adding to a rendered page's head after the layout has had its say.</summary>
/// <remarks>
/// The theme writes the head, and a plugin that wants a tag there without depending on which
/// theme is in use adds it on the way to the output instead.
/// </remarks>
module internal Head =

    /// <summary>Text safe inside a double-quoted attribute.</summary>
    let attribute (value: string) =
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")

    /// <summary>Whether the head already carries a tag naming <c>name</c>, quoted or not.</summary>
    /// <remarks>An HTML minifier drops the quotes around attribute values it does not need, so
    /// both spellings are looked for.</remarks>
    let declares (name: string) (html: string) =
        let head =
            match html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase) with
            | -1 -> html
            | index -> html.Substring(0, index)

        head.Contains($"\"%s{name}\"", StringComparison.OrdinalIgnoreCase)
        || head.Contains($"=%s{name}>", StringComparison.OrdinalIgnoreCase)
        || head.Contains($"=%s{name} ", StringComparison.OrdinalIgnoreCase)

    /// <summary>The document with <c>tags</c> placed just before its head closes.</summary>
    /// <remarks>A document with no <c>&lt;/head&gt;</c> is returned as it was.</remarks>
    let inject (tags: string list) (html: string) =
        match tags, html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase) with
        | [], _
        | _, -1 -> html
        | tags, index -> html.Insert(index, String.concat "" tags)

/// <summary>Where a route's page is written, keyed the way an asset transform sees it.</summary>
module internal OutputKey =

    let ofRoute (route: Route) =
        RelativePath.value (Url.outputPath route)

    let ofPath (path: RelativePath) = RelativePath.value path
