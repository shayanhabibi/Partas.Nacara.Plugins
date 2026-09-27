namespace Nacara.Plugins

open System
open System.Collections.Concurrent
open System.IO
open Nacara.Core
open Nacara.Plugins.Internal
open YamlDotNet.RepresentationModel

/// <summary>The image a page is shared with: what a link preview shows.</summary>
type OgImage =
    {
        /// <summary>An absolute url, or a path in the output such as <c>/images/card.png</c>.</summary>
        /// <remarks>A path is made absolute with the site's origin, since a crawler reading the
        /// tag has no page to resolve it against.</remarks>
        Url: string
        /// What the image shows, for a reader who cannot see it.
        Alt: string option
        /// Width in pixels, so a crawler can lay the preview out before fetching it.
        Width: int option
        /// Height in pixels.
        Height: int option
    }

/// <summary>Options of the og-image plugin.</summary>
type OgImageOptions =
    {
        /// <summary>The front matter keys a page names its image under, first match wins.</summary>
        /// <remarks>
        /// <para>The value is a path or url, or a mapping with <c>url</c>, <c>alt</c>, <c>width</c>
        /// and <c>height</c>. <c>false</c> opts the page out, fallbacks included.</para>
        /// <para>Defaults to <c>[ "og_image"; "ogImage"; "image" ]</c>.</para>
        /// </remarks>
        FrontMatterKeys: string list
        /// <summary>The front matter keys the image's alt text is read from, beside a plain path.</summary>
        /// <remarks>Defaults to <c>[ "og_image_alt"; "ogImageAlt"; "image_alt" ]</c>.</remarks>
        AltKeys: string list
        /// <summary>The image of a page whose front matter names none.</summary>
        /// <remarks>
        /// Asked before <c>Default</c>, so a collection or a section can have its own card:
        /// <c>fun page -> if page.Collection = "blog" then Some(OgImage.image "/blog-card.png") else None</c>.
        /// <para>Defaults to <c>fun _ -> None</c>.</para>
        /// </remarks>
        Fallback: Page -> OgImage option
        /// <summary>The image of every page nothing else gave one.</summary>
        /// <remarks>Defaults to <c>None</c>, which leaves such a page without an image.</remarks>
        Default: OgImage option
        /// <summary>Collections whose pages get no image at all.</summary>
        /// <remarks>Defaults to <c>[]</c>.</remarks>
        ExcludeCollections: string list
    }

/// <summary>
/// Open Graph and Twitter image tags for every page, from front matter with fallbacks.
/// </summary>
/// <remarks>
/// <para>A page names its image in front matter; a page that does not falls back to
/// <c>Fallback</c>, then to <c>Default</c>. The tags are added to the head of the rendered page,
/// so this works under any theme.</para>
/// <para>A theme that writes <c>og:image</c> itself wins: the plugin adds nothing to a head that
/// already has one. Such a theme can read what the plugin resolved from the page, under
/// <see cref="F:Nacara.Plugins.OgImage.DataKey"/>.</para>
/// </remarks>
[<RequireQualifiedAccess>]
module OgImage =

    /// <summary>Where the resolved image is stored on a page, as an <c>OgImage option</c>.</summary>
    /// <remarks><c>None</c> means the page has no image, having opted out or found no fallback.</remarks>
    [<Literal>]
    let DataKey = "og-image"

    /// <summary>An image with nothing but its url.</summary>
    /// <param name="url">An absolute url, or a path in the output.</param>
    let image (url: string) =
        {
            Url = url
            Alt = None
            Width = None
            Height = None
        }

    /// <summary>The same image, with alt text.</summary>
    let withAlt alt (image: OgImage) = { image with Alt = Some alt }

    /// <summary>The same image, with its dimensions in pixels.</summary>
    let withSize width height (image: OgImage) =
        { image with
            Width = Some width
            Height = Some height
        }

    let defaults =
        {
            FrontMatterKeys = [ "og_image"; "ogImage"; "image" ]
            AltKeys = [ "og_image_alt"; "ogImageAlt"; "image_alt" ]
            Fallback = fun _ -> None
            Default = None
            ExcludeCollections = []
        }

    /// <summary>What a page's front matter says about its image.</summary>
    type FrontMatterImage =
        /// The front matter does not mention one, so the fallbacks decide.
        | Unspecified
        /// The page asked for no image.
        | OptedOut
        | Specified of OgImage

    let private isAbsolute (url: string) =
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("//")

    /// <summary>Read a page's image out of its front matter.</summary>
    /// <param name="options">Which keys to read.</param>
    /// <param name="mapping">The page's front matter.</param>
    let fromFrontMatter (options: OgImageOptions) (mapping: YamlMappingNode) =
        let int (node: YamlNode option) =
            node
            |> Option.bind RawFrontMatter.scalar
            |> Option.bind (fun text ->
                match Int32.TryParse text with
                | true, value -> Some value
                | _ -> None
            )

        match RawFrontMatter.tryFind options.FrontMatterKeys mapping with
        | None -> Unspecified
        | Some(:? YamlMappingNode as detail) ->
            let get key = RawFrontMatter.tryFind [ key ] detail

            match get "url" |> Option.bind RawFrontMatter.scalar with
            | None -> Unspecified
            | Some url ->
                Specified
                    {
                        Url = url
                        Alt =
                            get "alt"
                            |> Option.bind RawFrontMatter.scalar
                            |> Option.orElse (RawFrontMatter.tryString options.AltKeys mapping)
                        Width = int (get "width")
                        Height = int (get "height")
                    }
        | Some node ->
            match RawFrontMatter.scalar node with
            | None -> Unspecified
            | Some value when
                String.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                ->
                OptedOut
            | Some url ->
                Specified
                    { image url with
                        Alt = RawFrontMatter.tryString options.AltKeys mapping
                    }

    /// <summary>The image a page ends up with, front matter first and then the fallbacks.</summary>
    /// <param name="options">The fallbacks.</param>
    /// <param name="frontMatter">What the page's front matter said.</param>
    /// <param name="page">The page, handed to <c>Fallback</c>.</param>
    let resolve (options: OgImageOptions) (frontMatter: FrontMatterImage) (page: Page) =
        if List.contains page.Collection options.ExcludeCollections then
            None
        else
            match frontMatter with
            | OptedOut -> None
            | Specified image -> Some image
            | Unspecified -> options.Fallback page |> Option.orElse options.Default

    /// <summary>The absolute url a crawler is given for an image.</summary>
    /// <remarks>A path is taken from the root of the output, under the site's base url.
    /// Without an origin there is nothing to make it absolute with, so it stays
    /// root-relative.</remarks>
    let absoluteUrl (site: SiteInfo) (url: string) =
        if isAbsolute url then
            url
        else
            let path = site.UrlOfAsset(url.TrimStart('/'))

            match site.Origin with
            | Some origin -> origin.TrimEnd('/') + path
            | None -> path

    /// <summary>The output path a site-relative image is served from, when it is one.</summary>
    let localPath (url: string) =
        if isAbsolute url then
            None
        else
            Some(url.TrimStart('/').Split([| '?'; '#' |]).[0])

    /// <summary>The head tags for an image, skipping any the head already has.</summary>
    /// <param name="site">For the origin that makes a path absolute.</param>
    /// <param name="image">What to describe.</param>
    /// <param name="html">The rendered page, to see what its head already says.</param>
    let tags (site: SiteInfo) (image: OgImage) (html: string) =
        let url = Head.attribute (absoluteUrl site image.Url)
        let property name value = $"<meta property=\"%s{name}\" content=\"%s{value}\">"
        let name name value = $"<meta name=\"%s{name}\" content=\"%s{value}\">"
        let alt = image.Alt |> Option.map Head.attribute

        [
            property "og:image" url
            if url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
                property "og:image:secure_url" url
            match image.Width with
            | Some width -> property "og:image:width" (string width)
            | None -> ()
            match image.Height with
            | Some height -> property "og:image:height" (string height)
            | None -> ()
            match alt with
            | Some alt -> property "og:image:alt" alt
            | None -> ()
            if not (Head.declares "twitter:card" html) then
                name "twitter:card" "summary_large_image"
            if not (Head.declares "twitter:image" html) then
                name "twitter:image" url
            match alt with
            | Some alt when not (Head.declares "twitter:image:alt" html) -> name "twitter:image:alt" alt
            | _ -> ()
        ]

    /// <summary>A rendered page with its image described in the head.</summary>
    /// <remarks>A head that already names an <c>og:image</c> is left alone: the theme
    /// chose.</remarks>
    let apply (site: SiteInfo) (image: OgImage) (html: string) =
        if Head.declares "og:image" html then
            html
        else
            Head.inject (tags site image html) html

    type private OgImagePlugin(options: OgImageOptions) =
        // Rebuilt every time the routes are resolved, which a watch does on every change.
        let byOutput = ConcurrentDictionary<string, SiteInfo * OgImage>()

        interface IPlugin with
            member _.Name = "og-image"

            member _.Configure registry =
                registry
                |> Registry.transform
                    {
                        Name = "og-image"
                        Extensions = []
                        Transform =
                            fun context page ->
                                let frontMatter =
                                    RawFrontMatter.read context.Registry.FrontMatterFormats page
                                    |> Option.map (fromFrontMatter options)
                                    |> Option.defaultValue Unspecified

                                (CatchAll.passThrough context.Registry page)
                                    .WithData(DataKey, resolve options frontMatter page)
                    }
                |> Registry.onPagesRouted (fun context ->
                    byOutput.Clear()

                    for page in context.Pages do
                        match page.TryData<OgImage option> DataKey with
                        | Some(Some image) -> byOutput[OutputKey.ofRoute page.Route] <- (context.Site, image)
                        | _ -> ()

                    if context.Site.Origin.IsNone && byOutput.Count > 0 then
                        context.Diagnostics.Add(
                            Diagnostic.warning
                                "origin-missing"
                                "og:image is written root-relative: the site does not say where it is published, and crawlers need an absolute url"
                            |> Diagnostic.withHint "Declare it with Site.origin \"https://example.com\""
                        )
                )
                |> Registry.assetTransform
                    {
                        Name = "og-image"
                        // Every asset, filtered here: naming .html would share the extension with
                        // an html minifier and be reported as a clash, though both should run.
                        Extensions = []
                        Transform =
                            fun context ->
                                match byOutput.TryGetValue(OutputKey.ofPath context.Path) with
                                | true, (site, image) -> apply site image context.Content
                                | _ -> context.Content
                    }
                |> Registry.onBuildComplete (fun context ->
                    if context.Writes then
                        for page in context.Pages do
                            match page.TryData<OgImage option> DataKey with
                            | Some(Some image) ->
                                match localPath image.Url with
                                | Some path when
                                    not (
                                        File.Exists(
                                            AbsolutePath.value (AbsolutePath.combine context.OutputDirectory [ path ])
                                        )
                                    )
                                    ->
                                    let diagnostic =
                                        Diagnostic.warning
                                            "image-missing"
                                            $"%s{page.Title} is shared with /%s{path}, which is not in the output"
                                        |> Diagnostic.withHint
                                            "Put the image in the static directory, or give an absolute url"

                                    context.Diagnostics.Add(
                                        match page.SourceFile with
                                        | Some file -> Diagnostic.inFile file diagnostic
                                        | None -> diagnostic
                                    )
                                | _ -> ()
                            | _ -> ()
                )

    /// <summary>The front matter keys a page names its image under, first match wins.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let frontMatterKeys value (options: OgImageOptions) = { options with FrontMatterKeys = value }

    /// <summary>The front matter keys the alt text of a plain path is read from.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let altKeys value (options: OgImageOptions) = { options with AltKeys = value }

    /// <summary>The image of a page whose front matter names none, asked before the default.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let fallback value (options: OgImageOptions) = { options with Fallback = value }

    /// <summary>The image of every page nothing else gave one.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let defaultImage (value: OgImage) (options: OgImageOptions) = { options with Default = Some value }

    /// <summary>Collections whose pages get no image at all.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let excludeCollections value (options: OgImageOptions) =
        { options with ExcludeCollections = value }

    /// <summary>Og-image tags, with the default options: front matter only, no fallback.</summary>
    let create () = OgImagePlugin(defaults) :> IPlugin

    /// <summary>Og-image tags, configured.</summary>
    /// <param name="configure">Given the defaults, the options to use.</param>
    let createWith (configure: OgImageOptions -> OgImageOptions) = OgImagePlugin(configure defaults) :> IPlugin

    /// <summary>Add og-image tags to a site.</summary>
    let register (site: Site) = Site.plugin (create ()) site

    /// <summary>Add og-image tags to a site, configured.</summary>
    /// <param name="configure">Given the defaults, the options to use.</param>
    /// <param name="site">The site being described.</param>
    let registerWith (configure: OgImageOptions -> OgImageOptions) (site: Site) =
        Site.plugin (createWith configure) site
