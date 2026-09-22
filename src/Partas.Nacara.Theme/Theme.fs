// This file is a derivative work of Nacara.Theme.Default, from the Nacara project:
//   Copyright Maxime Mangel - https://github.com/MangelMaxime/Nacara
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the
// root of this repository. This file was modified from its original form; the
// nature of the changes is described in NOTICE.

namespace Partas.Nacara.Theme

open System.IO
open System.Reflection
open Feliz.ViewEngine
open Nacara.Core

/// <summary>The default theme: a documentation layout and the assets it needs.</summary>
[<RequireQualifiedAccess>]
module Theme =

    let private readResource = Resource.text (Assembly.GetExecutingAssembly())

    /// <summary>The parts the theme's own stylesheet is built from, in cascade order.</summary>
    /// <remarks>
    /// Tokens first: everything after reads them. The order here is the order of the cascade
    /// layers, so a part later in the list wins a tie against one before it.
    /// </remarks>
    let defaultStyles =
        lazy
            ([
                 "base"
                 "navbar"
                 "layout"
                 "components"
                 "code"
                 "responsive"
             ]
             |> List.map (fun name ->
                 {
                     Name = name
                     Css = readResource $"css.%s{name}.css"
                 }
             )
             // Rendered rather than read: the tokens are a record, so that a site changes one
             // of them by name instead of by restating the stylesheet that declares them.
             |> List.insertAt
                 0
                 {
                     Name = "tokens"
                     Css = Tokens.render Tokens.defaults
                 })

    let defaults =
        {
            Navbar = []
            NavbarEnd = []
            Menus = Map.empty
            EditUrlBase = None
            HeadExtra = []
            Css = []
            Styles = defaultStyles.Value
            Tokens = Tokens.defaults
            Footer = None
            FavIcon = None
            MenuGroupLimit = 150
        }

    /// <summary>How many pages a menu group lists before it points at its own page instead.</summary>
    /// <param name="value">The value to use. <c>0</c> lists them all.</param>
    /// <param name="options">The options so far.</param>
    let menuGroupLimit value (options: ThemeOptions) =
        { options with
            MenuGroupLimit = value
        }

    /// <summary>The items on the left of the navbar, after the site's title.</summary>
    /// <param name="value">The items, in the order they are shown.</param>
    /// <param name="options">The options so far.</param>
    let navbar value (options: ThemeOptions) =
        { options with
            Navbar = value
        }

    /// <summary>The items on the right of the navbar - search, a repository link, the theme toggle.</summary>
    /// <param name="value">The items, in the order they are shown.</param>
    /// <param name="options">The options so far.</param>
    let navbarEnd value (options: ThemeOptions) =
        { options with
            NavbarEnd = value
        }

    /// <summary>The menu for one section, replacing the one its pages would have produced.</summary>
    /// <remarks>
    /// Additive: call it once per section. A section you say nothing about gets a menu built from
    /// its pages, or the one a plugin offered.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults
    /// |> Theme.menu "guide" [ Menu.page "guide/getting-started.md" ]
    /// |> Theme.menu "plugins" [ Menu.page "plugins/overview.md" ]
    /// </code>
    /// </example>
    /// <param name="section">The first segment of the routes it covers, such as <c>guide</c>.</param>
    /// <param name="items">What the menu holds.</param>
    /// <param name="options">The options so far.</param>
    let menu section items (options: ThemeOptions) =
        { options with
            Menus = Map.add section items options.Menus
        }

    /// <summary>Every menu at once, replacing any set before.</summary>
    /// <param name="value">The menus, keyed by section.</param>
    /// <param name="options">The options so far.</param>
    let menus value (options: ThemeOptions) =
        { options with
            Menus = value
        }

    /// <summary>Base URL for the "edit this page" link.</summary>
    /// <param name="value">Where an edit starts, such as
    /// <c>https://github.com/owner/repo/edit/main/docs</c>.</param>
    /// <param name="options">The options so far.</param>
    let editUrl value (options: ThemeOptions) =
        { options with
            EditUrlBase = Some value
        }

    /// <summary>Markup added at the end of <c>&lt;head&gt;</c> on every page.</summary>
    /// <param name="value">What to add - an analytics snippet, a font, a meta tag.</param>
    /// <param name="options">The options so far.</param>
    let headExtra value (options: ThemeOptions) =
        { options with
            HeadExtra = value
        }

    /// <summary>CSS added to every page, after the theme's own.</summary>
    /// <remarks>
    /// For a rule or two - a token to override, one section to treat differently. Anything larger
    /// belongs in a file of its own, shipped as a static asset and linked with
    /// <see cref="M:Partas.Nacara.Theme.Theme.headExtra" />.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults
    /// |> Theme.css """[data-section="reference"] { --nacara-sidebar-width: 20rem; }"""
    /// </code>
    /// </example>
    /// <param name="value">The rules, as they would be written in a stylesheet.</param>
    /// <param name="options">The options so far.</param>
    let css value (options: ThemeOptions) =
        { options with
            Css = options.Css @ [ value ]
        }

    let private layerIndex (name: string) (options: ThemeOptions) =
        match options.Styles |> List.tryFindIndex (fun layer -> layer.Name = name) with
        | Some index -> index
        | None ->
            let known = options.Styles |> List.map _.Name |> String.concat ", "

            failwith
                $"The theme has no style layer called '%s{name}'. It has: %s{known}. \
                   Add one with Theme.layerAfter rather than naming a layer that is not there."

    /// <summary>Every style layer at once, replacing the theme's own.</summary>
    /// <remarks>
    /// The whole stylesheet, yours. <c>Theme.replaceLayer</c> and <c>Theme.dropLayer</c> are the
    /// usual way in: they leave the parts you did not mention alone.
    /// </remarks>
    /// <param name="value">The parts, in cascade order.</param>
    /// <param name="options">The options so far.</param>
    let styles value (options: ThemeOptions) =
        { options with
            Styles = value
        }

    /// <summary>One part of the theme's stylesheet, written your way.</summary>
    /// <remarks>
    /// The layer keeps its place in the cascade, so what reads it still does. Naming a layer the
    /// theme does not have fails the build.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults |> Theme.replaceLayer "components" (File.ReadAllText "css/components.css")
    /// </code>
    /// </example>
    /// <param name="name">Which part - <c>tokens</c>, <c>base</c>, <c>navbar</c>, <c>layout</c>,
    /// <c>components</c>, <c>code</c> or <c>responsive</c>.</param>
    /// <param name="value">The rules that take its place.</param>
    /// <param name="options">The options so far.</param>
    let replaceLayer (name: string) (value: string) (options: ThemeOptions) =
        let index = layerIndex name options

        { options with
            Styles =
                options.Styles
                |> List.mapi (fun position layer ->
                    if position = index then
                        { layer with
                            Css = value
                        }
                    else
                        layer
                )
        }

    /// <summary>A part of the theme's stylesheet the site does without.</summary>
    /// <remarks>
    /// For a part something else already provides - dropping <c>code</c> where a highlighting
    /// plugin brings its own. Dropping <c>tokens</c> means providing every custom property the
    /// rest of the theme reads.
    /// </remarks>
    /// <param name="name">Which part to leave out.</param>
    /// <param name="options">The options so far.</param>
    let dropLayer (name: string) (options: ThemeOptions) =
        layerIndex name options |> ignore

        { options with
            Styles = options.Styles |> List.filter (fun layer -> layer.Name <> name)
        }

    /// <summary>A layer of your own, after one of the theme's.</summary>
    /// <remarks>Later in the cascade, so it wins a tie against the layer it follows.</remarks>
    /// <param name="anchor">The layer it goes after.</param>
    /// <param name="name">What yours is called. It becomes <c>nacara.&lt;name&gt;</c>.</param>
    /// <param name="value">The rules.</param>
    /// <param name="options">The options so far.</param>
    let layerAfter (anchor: string) (name: string) (value: string) (options: ThemeOptions) =
        let index = layerIndex anchor options

        let added =
            {
                Name = name
                Css = value
            }

        { options with
            Styles = List.insertAt (index + 1) added options.Styles
        }

    /// <summary>A layer of your own, before one of the theme's.</summary>
    /// <remarks>Earlier in the cascade, so the layer it precedes wins a tie against it.</remarks>
    /// <param name="anchor">The layer it goes before.</param>
    /// <param name="name">What yours is called. It becomes <c>nacara.&lt;name&gt;</c>.</param>
    /// <param name="value">The rules.</param>
    /// <param name="options">The options so far.</param>
    let layerBefore (anchor: string) (name: string) (value: string) (options: ThemeOptions) =
        let index = layerIndex anchor options

        let added =
            {
                Name = name
                Css = value
            }

        { options with
            Styles = List.insertAt index added options.Styles
        }

    /// <summary>The theme's design tokens, changed by name.</summary>
    /// <remarks>
    /// <para>
    /// The <c>tokens</c> layer is rewritten from the result, so a token changed here is a token
    /// changed everywhere the theme reads it. The four narrower functions -
    /// <see cref="M:Partas.Nacara.Theme.Theme.lightTokens" />,
    /// <see cref="M:Partas.Nacara.Theme.Theme.darkTokens" />,
    /// <see cref="M:Partas.Nacara.Theme.Theme.lightSyntax" /> and
    /// <see cref="M:Partas.Nacara.Theme.Theme.darkSyntax" /> - reach one record without naming
    /// the other three, which is usually what you want.
    /// </para>
    /// <para>
    /// This fails if the <c>tokens</c> layer has been dropped, since there is then nothing to
    /// write the properties into.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults
    /// |> Theme.tokens (fun tokens ->
    ///     { tokens with
    ///         Light = { tokens.Light with Primary = "#c0392b" }
    ///         Dark = { tokens.Dark with Primary = "#e74c3c" } })
    /// </code>
    /// </example>
    /// <param name="mapping">The tokens so far, and what they become.</param>
    /// <param name="options">The options so far.</param>
    let tokens (mapping: ThemeTokens -> ThemeTokens) (options: ThemeOptions) =
        let changed = mapping options.Tokens

        { options with
            Tokens = changed
        }
        |> replaceLayer "tokens" (Tokens.render changed)

    /// <summary>The <c>--nacara-*</c> properties as the light scheme has them.</summary>
    /// <remarks>These are the defaults the dark scheme overrides, so a property left out of
    /// <see cref="M:Partas.Nacara.Theme.Theme.darkTokens" /> is whatever this says.</remarks>
    /// <example>
    /// <code lang="fsharp">
    /// Theme.defaults |> Theme.lightTokens (fun t -> { t with Primary = "#c0392b" })
    /// </code>
    /// </example>
    /// <param name="mapping">The light tokens so far, and what they become.</param>
    /// <param name="options">The options so far.</param>
    let lightTokens (mapping: Tokens -> Tokens) (options: ThemeOptions) =
        options |> tokens (fun value -> { value with Light = mapping value.Light })

    /// <summary>The <c>--nacara-*</c> properties as the dark scheme has them.</summary>
    /// <remarks>Only what differs from the light value is written, so restating a light value
    /// here costs nothing.</remarks>
    /// <param name="mapping">The dark tokens so far, and what they become.</param>
    /// <param name="options">The options so far.</param>
    let darkTokens (mapping: Tokens -> Tokens) (options: ThemeOptions) =
        options |> tokens (fun value -> { value with Dark = mapping value.Dark })

    /// <summary>The <c>--tok-*</c> highlighting colours as the light scheme has them.</summary>
    /// <param name="mapping">The light syntax colours so far, and what they become.</param>
    /// <param name="options">The options so far.</param>
    let lightSyntax (mapping: SyntaxTokens -> SyntaxTokens) (options: ThemeOptions) =
        options |> tokens (fun value -> { value with SyntaxLight = mapping value.SyntaxLight })

    /// <summary>The <c>--tok-*</c> highlighting colours as the dark scheme has them.</summary>
    /// <param name="mapping">The dark syntax colours so far, and what they become.</param>
    /// <param name="options">The options so far.</param>
    let darkSyntax (mapping: SyntaxTokens -> SyntaxTokens) (options: ThemeOptions) =
        options |> tokens (fun value -> { value with SyntaxDark = mapping value.SyntaxDark })

    /// <summary>What every page ends with.</summary>
    /// <param name="value">The footer's markup.</param>
    /// <param name="options">The options so far.</param>
    let footer value (options: ThemeOptions) =
        { options with
            Footer = Some value
        }

    /// <summary>The site's favicon.</summary>
    /// <param name="value">Its path, relative to the site root.</param>
    /// <param name="options">The options so far.</param>
    let favIcon value (options: ThemeOptions) =
        { options with
            FavIcon = Some value
        }

    /// Assets carry a content hash in their name, so a deployed site can cache them forever and
    /// still pick up a change immediately.
    let private fingerprinted (name: string) (extension: string) =
        lazy
            (let content = readResource (name + extension)
             let hash = BuildCache.Hash(content).Substring(0, 8).ToLowerInvariant()
             $"assets/%s{name}.%s{hash}%s{extension}", content)

    let private entryStyleSheet = "nacara.css"

    /// <summary>The site's own layers, and then whatever plugins offered.</summary>
    /// <remarks>
    /// Offered layers go last, so a plugin wins a tie against the theme rather than the other way
    /// round. Asked for in both places the stylesheet is spoken of - where it is bundled and where
    /// a page links it - so the name a page points at is the file that was written.
    /// </remarks>
    let private effectiveLayers (options: ThemeOptions) =
        let offered =
            PluginLayers.all ()
            |> List.filter (fun layer ->
                options.Styles |> List.exists (fun own -> own.Name = layer.Name) |> not
            )

        options.Styles @ offered

    /// <summary>The entry the bundler is given: the cascade order, then the parts in it.</summary>
    /// <remarks>
    /// The order is declared outright rather than left to the order the parts happen to arrive in,
    /// so a stylesheet written elsewhere can place itself against the theme by name.
    /// </remarks>
    let private entryOf (layers: StyleLayer list) =
        let order =
            layers
            |> List.map (fun layer -> $"nacara.%s{layer.Name}")
            |> String.concat ", "

        [
            "/* Generated. The theme's parts, each in a cascade layer of its own. */"
            if not layers.IsEmpty then
                $"@layer %s{order};"
            for layer in layers do
                $"@import \"%s{layer.Name}.css\";"
        ]
        |> String.concat "\n"

    /// A part is wrapped where it is bundled rather than in its source file, so the sources stay
    /// ordinary stylesheets an editor can still make sense of.
    let private wrapped (layer: StyleLayer) =
        $"@layer nacara.%s{layer.Name} {{\n%s{layer.Css}\n}}\n"

    /// <summary>The stylesheet's parts, and where the bundle is served from.</summary>
    /// <remarks>
    /// Memoised on the parts themselves: <c>shell</c> asks for the path on every page, and hashing
    /// the whole stylesheet each time would be work done once per page for an answer that only
    /// changes when the parts do.
    /// </remarks>
    let private stylesheetOf =
        let cache = System.Collections.Concurrent.ConcurrentDictionary<StyleLayer list, (string * string) list * string>()

        fun (layers: StyleLayer list) ->
            cache.GetOrAdd(
                layers,
                fun layers ->
                    let parts =
                        (entryStyleSheet, entryOf layers)
                        :: [ for layer in layers -> $"%s{layer.Name}.css", wrapped layer ]

                    let hash =
                        parts
                        |> List.map snd
                        |> String.concat "\n"
                        |> BuildCache.Hash
                        |> fun value -> value.Substring(0, 8).ToLowerInvariant()

                    parts, $"assets/css/nacara.%s{hash}.css"
            )

    let private script = fingerprinted "nacara" ".js"

    /// <summary>Settles the colour scheme before first paint, so nobody sees the wrong one flash.</summary>
    let private themeBootstrap = lazy readResource "theme-bootstrap.js"

    let private rawHtml (markup: string) =
        Html.span [ prop.dangerouslySetInnerHTML markup ]

    /// <summary>Render a full page with the theme's chrome.</summary>
    /// <param name="options">The theme's own configuration: navbar, menus, footer.</param>
    /// <param name="doc">What the theme needs to know about this page - its title, and which
    /// parts of the chrome it wants.</param>
    /// <param name="context">The page and the site around it, whatever front-matter type the site
    /// declared.</param>
    /// <remarks>
    /// Takes a <see cref="T:Partas.Nacara.Theme.DocPage" /> rather than reading front matter itself, so a
    /// site with its own front-matter type uses the same layout by mapping onto that record.
    /// </remarks>
    let shell (options: ThemeOptions) (doc: DocPage) (context: PageContext<'FrontMatter>) =
        let _, cssPath = stylesheetOf (effectiveLayers options)
        let scriptPath, _ = script.Value

        let layoutClass =
            if not doc.Styled then
                "nacara-layout nacara-layout--bare"
            elif not doc.ShowMenu then
                "nacara-layout nacara-layout--splash"
            elif not doc.ShowToc then
                "nacara-layout nacara-layout--no-toc"
            else
                "nacara-layout"

        Html.html
            [
                prop.lang context.Page.Locale.Code
                prop.custom ("dir", context.Page.Locale.Direction.HtmlValue)
                prop.children
                    [
                        Html.head
                            [
                                Html.meta [ prop.charset.utf8 ]
                                Html.meta
                                    [
                                        prop.name "viewport"
                                        prop.content "width=device-width, initial-scale=1"
                                    ]
                                Html.title (
                                    if doc.Title = context.Site.Title then
                                        doc.Title
                                    else
                                        $"%s{doc.Title} · %s{context.Site.Title}"
                                )
                                match doc.Description |> Option.orElse context.Site.Description with
                                | Some description ->
                                    Html.meta
                                        [
                                            prop.name "description"
                                            prop.content description
                                        ]
                                | None -> Html.none
                                match context.Site.AbsoluteUrlOf context.Page.Route with
                                | Some url ->
                                    Html.link
                                        [
                                            prop.rel "canonical"
                                            prop.href url
                                        ]

                                    Html.meta
                                        [
                                            prop.custom ("property", "og:url")
                                            prop.content url
                                        ]
                                | None -> Html.none
                                Html.meta
                                    [
                                        prop.custom ("property", "og:site_name")
                                        prop.content context.Site.Title
                                    ]
                                Html.meta
                                    [
                                        prop.custom ("property", "og:locale")
                                        prop.content context.Page.Locale.Code
                                    ]
                                Html.meta
                                    [
                                        prop.name "twitter:card"
                                        prop.content "summary_large_image"
                                    ]
                                match doc.Description |> Option.orElse context.Site.Description with
                                | Some description ->
                                    Html.meta
                                        [
                                            prop.custom ("property", "og:description")
                                            prop.content description
                                        ]
                                | None -> Html.none
                                Html.meta
                                    [
                                        prop.custom ("property", "og:title")
                                        prop.content doc.Title
                                    ]
                                Html.meta
                                    [
                                        prop.custom ("property", "og:type")
                                        prop.content "article"
                                    ]
                                match options.FavIcon with
                                | Some icon ->
                                    Html.link
                                        [
                                            prop.rel "icon"
                                            prop.href (context.Site.UrlOfAsset icon)
                                        ]
                                | None -> Html.none
                                Html.link
                                    [
                                        prop.rel "stylesheet"
                                        prop.href (context.Site.UrlOfAsset cssPath)
                                    ]
                                Html.script [ prop.dangerouslySetInnerHTML themeBootstrap.Value ]
                                for asset in context.Site.PageAssets do
                                    match asset with
                                    | Stylesheet path ->
                                        Html.link
                                            [
                                                prop.rel "stylesheet"
                                                prop.href (context.Site.UrlOfAsset path)
                                            ]
                                    | Script _
                                    | InlineScript _ -> Html.none
                                yield! options.HeadExtra

                                if not options.Css.IsEmpty then
                                    Html.style
                                        [
                                            prop.dangerouslySetInnerHTML (
                                                String.concat "\n" options.Css
                                            )
                                        ]
                            ]
                        Html.body
                            [
                                prop.custom ("data-section", Components.sectionOf context.Page)
                                prop.children
                                    [
                                        Html.a
                                            [
                                                prop.className "nacara-skip-link"
                                                prop.href "#nacara-content"
                                                prop.text "Skip to content"
                                            ]
                                        Components.navbar options context
                                        Html.div
                                            [
                                                prop.className layoutClass
                                                prop.children
                                                    [
                                                        // Rendered even with no menu to show: on a
                                                        // narrow screen it is what the navbar's
                                                        // sections fold into.
                                                        Components.sidebar options doc context
                                                        Html.main
                                                            [
                                                                for name, value in
                                                                    doc.MainAttributes do
                                                                    prop.custom (name, value)
                                                                prop.id "nacara-content"
                                                                prop.tabIndex -1

                                                                if doc.Styled then
                                                                    prop.className "nacara-content"
                                                                match context.Page.Source with
                                                                | Generated origin ->
                                                                    prop.custom (
                                                                        "data-nacara-generated-by",
                                                                        origin
                                                                    )
                                                                | FromFile _ -> ()
                                                                prop.children
                                                                    [
                                                                        match
                                                                            context.Page.TryData<
                                                                                string
                                                                             >
                                                                                PageData
                                                                                    .UntranslatedFrom
                                                                        with
                                                                        | Some source ->
                                                                            Html.aside
                                                                                [
                                                                                    prop.className
                                                                                        "nacara-callout"
                                                                                    prop.custom (
                                                                                        "data-kind",
                                                                                        "note"
                                                                                    )
                                                                                    prop.custom (
                                                                                        "data-title",
                                                                                        "Not translated yet"
                                                                                    )
                                                                                    prop.children
                                                                                        [
                                                                                            Html.p
                                                                                                $"This page has not been translated into %s{context.Page.Locale.Label} yet - you are reading the '%s{source}' version."
                                                                                        ]
                                                                                ]
                                                                        | None -> Html.none
                                                                        if doc.ShowTitle then
                                                                            Html.h1 doc.Title
                                                                        Html.div
                                                                            [
                                                                                prop
                                                                                    .dangerouslySetInnerHTML
                                                                                    context.Content
                                                                            ]
                                                                        if doc.ShowEditLink then
                                                                            Components.editLink
                                                                                options
                                                                                context
                                                                        if doc.ShowPageNav then
                                                                            Components.pageNav
                                                                                options
                                                                                context
                                                                    ]
                                                            ]
                                                        if
                                                            doc.ShowMenu
                                                            && doc.ShowToc
                                                            && not (
                                                                List.isEmpty context.Page.Headings
                                                            )
                                                        then
                                                            Components.toc context
                                                    ]
                                            ]
                                        match options.Footer with
                                        | Some footer ->
                                            Html.footer
                                                [
                                                    prop.className "nacara-footer"
                                                    prop.children [ footer ]
                                                ]
                                        | None -> Html.none
                                        Html.script
                                            [
                                                prop.src (context.Site.UrlOfAsset scriptPath)
                                                prop.custom ("defer", "defer")
                                            ]
                                        for asset in context.Site.PageAssets do
                                            match asset with
                                            | Script(path, defer) ->
                                                Html.script
                                                    [
                                                        prop.src (context.Site.UrlOfAsset path)
                                                        if defer then
                                                            prop.custom ("defer", "defer")
                                                    ]
                                            | InlineScript code ->
                                                Html.script [ prop.dangerouslySetInnerHTML code ]
                                            | Stylesheet _ -> Html.none
                                    ]
                            ]
                    ]
            ]

    /// <summary>The layout for pages using the theme's own front matter.</summary>
    /// <param name="options">The theme's own configuration.</param>
    /// <param name="context">The page and the site around it. Its front matter is read for the
    /// title, the description and which parts of the chrome to leave out.</param>
    let layout (options: ThemeOptions) (context: PageContext<DocFrontMatter>) =
        shell options (DocFrontMatter.toDocPage context.FrontMatter) context

    /// <summary>
    /// A documentation collection using the theme's front matter and layout.
    /// </summary>
    /// <example>
    /// <code lang="fsharp">
    /// Site.create "Nacara" |> Site.collection (Theme.docs options "docs")
    /// </code>
    /// </example>
    /// <param name="options">The theme's own configuration, which its layout is given.</param>
    /// <param name="name">What the collection is called, which is also the directory its content
    /// is read from.</param>
    let docs (options: ThemeOptions) (name: string) =
        Collection.create name DocFrontMatter.decoder
        |> Collection.sourceAll name
        |> Collection.title _.Title
        |> Collection.order (fun frontMatter -> frontMatter.Order |> Option.defaultValue 0)
        |> Collection.toc (fun frontMatter ->
            match frontMatter.Toc with
            | Some(TocLevels range) -> Some range
            | Some TocOff
            | None -> None
        )
        |> Collection.layout (layout options)

    /// <summary>
    /// The page a reader gets when a url matches nothing, unless the site writes its own.
    /// </summary>
    let private writeDefaultNotFound (options: ThemeOptions) (context: HookContext) =
        let occupied =
            context.Pages
            |> List.map (fun page -> RelativePath.value (Url.outputPath page.Route))
            |> Set.ofList

        for locale in context.Site.Locales do
            let route = Route.file locale "404.html"
            let path = RelativePath.value (Url.outputPath route)

            if not (occupied.Contains path) then
                let frontMatter =
                    {
                        Title = "Page not found"
                        Description = Some "That page does not exist"
                        Order = None
                        Layout = Some "bare"
                        PageNav = None
                        MenuFilter = None
                        MenuMemory = None
                        Toc = None
                        Main = []
                    }

                let body =
                    [
                        "<p>"
                        "The page you asked for is not here. It may have been renamed, or the link "
                        "that brought you here may be out of date."
                        "</p>"
                        "<p>"
                        $"""<a href="%s{context.Site.UrlOf(Route.home locale)}">Back to the start of the site</a>"""
                        "</p>"
                    ]
                    |> String.concat ""

                let page =
                    {
                        Id = $"theme.default:404:%s{locale.Code}"
                        Collection = "theme.default"
                        Source = Generated "theme.default"
                        ProjectPath = None
                        Format = ".html"
                        Locale = locale
                        Route = route
                        BodyLine = 1
                        Title = frontMatter.Title
                        Order = 0
                        Body = body
                        Html = body
                        Headings = []
                        FrontMatter = box frontMatter
                        Dependencies = []
                        Data = Map.empty
                    }

                let html =
                    layout
                        options
                        {
                            Page = page
                            FrontMatter = frontMatter
                            Site = context.Site
                            Pages = context.Pages
                            Content = body
                        }
                    |> Render.htmlDocument

                context.Write path html |> ignore

    type private ThemePlugin(options: ThemeOptions) =
        interface IPlugin with
            member _.Name = "theme.default"

            member _.Configure registry =
                let layers = effectiveLayers options
                let parts, cssPath = stylesheetOf layers
                let scriptPath, javascript = script.Value

                OfferedMenus.remember (Registry.extras<MenuOutline> registry)

                // Said here rather than in the registry: a CSS tool reads this while bundling, and
                // it may well have been registered before the theme was.
                ThemeLayers.declare [ for layer in layers -> $"nacara.%s{layer.Name}" ]

                registry
                |> Registry.asset (Bundle(parts, entryStyleSheet, RelativePath.create cssPath))
                |> Registry.asset (WriteText(javascript, RelativePath.create scriptPath))
                |> Registry.extra (NacaraCodeBlockRenderer() :> ICodeBlockRenderer)
                |> Registry.onBuildComplete (writeDefaultNotFound options)

    /// <summary>The theme's assets. Add it to the site alongside the layout you use.</summary>
    let create (options: ThemeOptions) = ThemePlugin(options) :> IPlugin

    /// <summary>Add the theme's assets to a site.</summary>
    /// <remarks>
    /// Registers what the theme needs of the site. The layout is chosen per collection, with
    /// <see cref="M:Partas.Nacara.Theme.Theme.docs" /> or <see cref="M:Partas.Nacara.Theme.Theme.layout" />.
    /// </remarks>
    /// <param name="options">The theme's options: your navbar, your footer, your menus.</param>
    /// <param name="site">The site you are describing.</param>
    let register (options: ThemeOptions) (site: Site) = Site.plugin (create options) site
