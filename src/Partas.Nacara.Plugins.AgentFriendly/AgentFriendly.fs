namespace Nacara.Plugins

open System
open System.Collections.Concurrent
open System.Text
open Nacara.Core
open Nacara.Plugins.Internal

/// <summary>Options of the agent-friendly plugin.</summary>
type AgentFriendlyOptions =
    {
        /// <summary>Source formats whose pages are published as markdown too, lowercase and dotted.</summary>
        /// <remarks>A page's body is written as it was authored, so only a format that is already
        /// markdown reads well that way. Defaults to <c>[ ".md" ]</c>.</remarks>
        Formats: string list
        /// <summary>Write a markdown copy of each page beside its html.</summary>
        /// <remarks><c>/guide/setup/</c> is copied to <c>/guide/setup.md</c>, the url an agent
        /// guesses. Defaults to <c>true</c>.</remarks>
        MarkdownCopies: bool
        /// <summary>Where the <c>llms.txt</c> index is written, or <c>None</c> for none.</summary>
        /// <remarks>Defaults to <c>Some "llms.txt"</c>.</remarks>
        LlmsTxt: string option
        /// <summary>Where every markdown page is written as one file, or <c>None</c> for none.</summary>
        /// <remarks>Defaults to <c>Some "llms-full.txt"</c>.</remarks>
        LlmsFullTxt: string option
        /// <summary>Point each page's head at its markdown copy with
        /// <c>&lt;link rel="alternate" type="text/markdown"&gt;</c>.</summary>
        /// <remarks>Defaults to <c>true</c>. Needs <c>MarkdownCopies</c>.</remarks>
        AlternateLink: bool
        /// <summary>The line quoted under the title of <c>llms.txt</c>.</summary>
        /// <remarks>Defaults to <c>None</c>, which uses the site's description.</remarks>
        Summary: string option
        /// <summary>Markdown placed after the summary in <c>llms.txt</c>: what an agent should
        /// know before it reads any page.</summary>
        /// <remarks>Defaults to <c>None</c>.</remarks>
        Details: string option
        /// <summary>The front matter keys a page's description is read from, first match wins.</summary>
        /// <remarks>Defaults to <c>[ "description"; "summary" ]</c>.</remarks>
        DescriptionKeys: string list
        /// <summary>The heading a page is listed under in <c>llms.txt</c>.</summary>
        /// <remarks>Defaults to the first segment of its route, title-cased, and <c>Pages</c>
        /// for a page at the top of the site.</remarks>
        Section: Page -> string
        /// <summary>Collections to leave out of everything.</summary>
        /// <remarks>Defaults to <c>[]</c>.</remarks>
        ExcludeCollections: string list
    }

/// <summary>One page as <c>llms.txt</c> lists it.</summary>
type AgentPage =
    {
        Page: Page
        Description: string option
        /// Where the page is read: its markdown copy when it has one, its html otherwise.
        Url: string
        /// The markdown copy's path in the output, when the page has one.
        MarkdownPath: string option
    }

/// <summary>
/// A Nacara site agents can read: an <c>llms.txt</c> index, an <c>llms-full.txt</c> of every
/// page, and a markdown copy of each page beside its html.
/// </summary>
/// <remarks>
/// Follows the <see href="https://llmstxt.org">llms.txt</see> proposal. The markdown is the page
/// as authored, front matter stripped and its title restored as a heading, which is easier for a
/// model to read than the rendered html and far shorter.
/// </remarks>
[<RequireQualifiedAccess>]
module AgentFriendly =

    /// <summary>Where a page's description is stored on it, as a <c>string option</c>.</summary>
    [<Literal>]
    let DescriptionKey = "agent-friendly.description"

    let private titleCase (segment: string) =
        let words = segment.Replace('-', ' ').Replace('_', ' ')

        if words = "" then
            words
        else
            string (Char.ToUpperInvariant words[0]) + words.Substring 1

    /// <summary>The default <c>Section</c>: the route's first segment, or <c>Pages</c>.</summary>
    let sectionOf (page: Page) =
        match page.Route.Segments with
        | first :: _ :: _ -> titleCase first
        | _ -> "Pages"

    let defaults =
        {
            Formats = [ ".md" ]
            MarkdownCopies = true
            LlmsTxt = Some "llms.txt"
            LlmsFullTxt = Some "llms-full.txt"
            AlternateLink = true
            Summary = None
            Details = None
            DescriptionKeys = [ "description"; "summary" ]
            Section = sectionOf
            ExcludeCollections = []
        }

    /// <summary>Where a page's markdown copy goes, relative to the output.</summary>
    /// <remarks><c>guide/setup/index.html</c> becomes <c>guide/setup.md</c> and the home page's
    /// <c>index.html</c> becomes <c>index.md</c>.</remarks>
    /// <param name="route">The page's route.</param>
    let markdownPath (route: Route) =
        let html = OutputKey.ofRoute route

        if html = "index.html" then "index.md"
        elif html.EndsWith "/index.html" then html.Substring(0, html.Length - "/index.html".Length) + ".md"
        elif html.EndsWith ".html" then html.Substring(0, html.Length - ".html".Length) + ".md"
        else html + ".md"

    let private urlOf (site: SiteInfo) (path: string) =
        let path = site.UrlOfAsset path
        site.Origin |> Option.map (fun origin -> origin.TrimEnd('/') + path) |> Option.defaultValue path

    let private pageUrl (site: SiteInfo) (page: Page) =
        site.AbsoluteUrlOf page.Route |> Option.defaultValue (site.UrlOf page.Route)

    /// <summary>The pages the plugin publishes, with where each is read.</summary>
    /// <param name="site">For the urls.</param>
    /// <param name="options">What to leave out, and which formats get a markdown copy.</param>
    /// <param name="pages">Every page of the build.</param>
    let agentPages (site: SiteInfo) (options: AgentFriendlyOptions) (pages: Page list) =
        pages
        |> List.filter (fun page -> not (List.contains page.Collection options.ExcludeCollections))
        |> List.map (fun page ->
            let markdown =
                if options.MarkdownCopies && List.contains page.Format options.Formats then
                    Some(markdownPath page.Route)
                else
                    None

            {
                Page = page
                Description = page.TryData<string option> DescriptionKey |> Option.flatten
                Url =
                    match markdown with
                    | Some path -> urlOf site path
                    | None -> pageUrl site page
                MarkdownPath = markdown
            }
        )

    /// <summary>A page as a markdown document of its own.</summary>
    /// <remarks>The title is restored as a heading unless the body opens with one, and the
    /// description follows it as a quote.</remarks>
    /// <param name="page">What to write.</param>
    let markdown (page: AgentPage) =
        let body = page.Page.Body.Replace("\r\n", "\n").Trim('\n')
        let builder = StringBuilder()

        if not (body.TrimStart().StartsWith "# ") then
            builder.Append($"# %s{page.Page.Title}\n\n") |> ignore

            match page.Description with
            | Some description -> builder.Append($"> %s{description}\n\n") |> ignore
            | None -> ()

        builder.Append(body).Append('\n').ToString()

    let private oneLine (text: string) =
        text.Replace("\r\n", " ").Replace('\n', ' ').Trim()

    /// <summary>Pages grouped under their headings, in the order both files list them.</summary>
    /// <remarks>Pages at the top of the site come first, as the way in to the rest; then each
    /// section by name, and within one by the pages' order and url.</remarks>
    let private sections (options: AgentFriendlyOptions) (pages: AgentPage list) =
        pages
        |> List.groupBy (fun page -> options.Section page.Page)
        |> List.sortBy (fun (section, _) -> section <> "Pages", section)
        |> List.map (fun (section, pages) -> section, pages |> List.sortBy (fun page -> page.Page.Order, page.Url))

    /// <summary>The <c>llms.txt</c> index of a site.</summary>
    /// <param name="site">Its title and description.</param>
    /// <param name="options">The summary, details and sections.</param>
    /// <param name="pages">What to list.</param>
    let llmsTxt (site: SiteInfo) (options: AgentFriendlyOptions) (pages: AgentPage list) =
        let builder = StringBuilder()
        let line (text: string) = builder.Append(text).Append('\n') |> ignore

        line $"# %s{site.Title}"
        line ""

        match options.Summary |> Option.orElse site.Description with
        | Some summary ->
            line $"> %s{oneLine summary}"
            line ""
        | None -> ()

        match options.Details with
        | Some details ->
            line (details.Trim())
            line ""
        | None -> ()

        for section, pages in sections options pages do
            line $"## %s{section}"
            line ""

            for page in pages do
                match page.Description with
                | Some description -> line $"- [%s{page.Page.Title}](%s{page.Url}): %s{oneLine description}"
                | None -> line $"- [%s{page.Page.Title}](%s{page.Url})"

            line ""

        builder.ToString().TrimEnd('\n') + "\n"

    /// <summary>Every page with a markdown copy, one after another, for an agent that wants it all.</summary>
    /// <param name="site">Its title, for the heading.</param>
    /// <param name="options">The sections, which order the pages as <c>llms.txt</c> does.</param>
    /// <param name="pages">What to include. Pages without a markdown copy are skipped.</param>
    let llmsFullTxt (site: SiteInfo) (options: AgentFriendlyOptions) (pages: AgentPage list) =
        let documents =
            sections options pages
            |> List.collect snd
            |> List.filter _.MarkdownPath.IsSome
            |> List.map (fun page -> $"<!-- Source: %s{page.Url} -->\n\n%s{markdown page}")

        $"# %s{site.Title}\n\n" + String.concat "\n---\n\n" documents

    /// <summary>The tag pointing a page's head at its markdown copy.</summary>
    let alternateLink (url: string) =
        $"<link rel=\"alternate\" type=\"text/markdown\" href=\"%s{Head.attribute url}\">"

    type private AgentFriendlyPlugin(options: AgentFriendlyOptions) =
        // The markdown url of each page, by the path its html is written to. Rebuilt whenever the
        // routes are.
        let alternates = ConcurrentDictionary<string, string>()

        interface IPlugin with
            member _.Name = "agent-friendly"

            member _.Configure registry =
                registry
                |> Registry.transform
                    {
                        Name = "agent-friendly"
                        Extensions = []
                        Transform =
                            fun context page ->
                                let description =
                                    RawFrontMatter.read context.Registry.FrontMatterFormats page
                                    |> Option.bind (RawFrontMatter.tryString options.DescriptionKeys)

                                (CatchAll.passThrough context.Registry page).WithData(DescriptionKey, description)
                    }
                |> Registry.onPagesRouted (fun context ->
                    alternates.Clear()

                    if options.AlternateLink then
                        for page in agentPages context.Site options context.Pages do
                            match page.MarkdownPath with
                            | Some _ -> alternates[OutputKey.ofRoute page.Page.Route] <- page.Url
                            | None -> ()
                )
                |> Registry.assetTransform
                    {
                        Name = "agent-friendly"
                        // Every asset, filtered here, so as not to be reported as clashing with an
                        // html minifier over the extension.
                        Extensions = []
                        Transform =
                            fun context ->
                                match alternates.TryGetValue(OutputKey.ofPath context.Path) with
                                | true, url when not (context.Content.Contains "type=\"text/markdown\"") ->
                                    Head.inject [ alternateLink url ] context.Content
                                | _ -> context.Content
                    }
                |> Registry.onBuildComplete (fun context ->
                    if context.Writes then
                        let pages = agentPages context.Site options context.Pages

                        for page in pages do
                            match page.MarkdownPath with
                            | Some path -> context.Write path (markdown page) |> ignore
                            | None -> ()

                        match options.LlmsTxt with
                        | Some path -> context.Write path (llmsTxt context.Site options pages) |> ignore
                        | None -> ()

                        match options.LlmsFullTxt with
                        | Some path -> context.Write path (llmsFullTxt context.Site options pages) |> ignore
                        | None -> ()
                )

    /// <summary>Source formats whose pages are published as markdown too.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let formats value (options: AgentFriendlyOptions) = { options with Formats = value }

    /// <summary>Write a markdown copy of each page beside its html.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let markdownCopies value (options: AgentFriendlyOptions) = { options with MarkdownCopies = value }

    /// <summary>Where the <c>llms.txt</c> index is written, or <c>None</c> for none.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let llmsTxtPath value (options: AgentFriendlyOptions) = { options with LlmsTxt = value }

    /// <summary>Where <c>llms-full.txt</c> is written, or <c>None</c> for none.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let llmsFullTxtPath value (options: AgentFriendlyOptions) = { options with LlmsFullTxt = value }

    /// <summary>Point each page's head at its markdown copy.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let alternate value (options: AgentFriendlyOptions) = { options with AlternateLink = value }

    /// <summary>The line quoted under the title of <c>llms.txt</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let summary value (options: AgentFriendlyOptions) = { options with Summary = Some value }

    /// <summary>Markdown placed after the summary in <c>llms.txt</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let details value (options: AgentFriendlyOptions) = { options with Details = Some value }

    /// <summary>The front matter keys a page's description is read from.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let descriptionKeys value (options: AgentFriendlyOptions) = { options with DescriptionKeys = value }

    /// <summary>The heading a page is listed under in <c>llms.txt</c>.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let section value (options: AgentFriendlyOptions) = { options with Section = value }

    /// <summary>Collections to leave out of everything.</summary>
    /// <param name="value">The value to use.</param>
    /// <param name="options">The options so far.</param>
    let excludeCollections value (options: AgentFriendlyOptions) =
        { options with ExcludeCollections = value }

    /// <summary>An agent-friendly site, with the default options.</summary>
    let create () = AgentFriendlyPlugin(defaults) :> IPlugin

    /// <summary>An agent-friendly site, configured.</summary>
    /// <param name="configure">Given the defaults, the options to use.</param>
    let createWith (configure: AgentFriendlyOptions -> AgentFriendlyOptions) =
        AgentFriendlyPlugin(configure defaults) :> IPlugin

    /// <summary>Make a site agent-friendly.</summary>
    let register (site: Site) = Site.plugin (create ()) site

    /// <summary>Make a site agent-friendly, configured.</summary>
    /// <param name="configure">Given the defaults, the options to use.</param>
    /// <param name="site">The site being described.</param>
    let registerWith (configure: AgentFriendlyOptions -> AgentFriendlyOptions) (site: Site) =
        Site.plugin (createWith configure) site
