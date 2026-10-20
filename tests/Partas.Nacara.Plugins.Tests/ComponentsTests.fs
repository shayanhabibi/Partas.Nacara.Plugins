module Partas.Nacara.Plugins.Tests.Components

open Expecto
open Feliz.ViewEngine
open Nacara.Core
open Partas.Nacara.Theme

let private locale = Locale.root "en"

let private site: SiteInfo =
    {
        Title = "Demo"
        Description = None
        Url = SiteUrl.create "/"
        Origin = None
        Locales = [ locale ]
        RootLocale = locale
        PageAssets = []
    }

let private page segments title : Page =
    {
        Id = "docs:" + String.concat "/" segments
        Collection = "docs"
        Source = PageSource.Generated "test"
        ProjectPath = Some(RelativePath.create ("docs/" + String.concat "/" segments + ".md"))
        Format = ".md"
        Locale = locale
        Route = Route.create locale segments
        Title = title
        Order = 0
        Body = ""
        BodyLine = 1
        Html = ""
        Headings =
            [
                {
                    Level = 2
                    Text = "Usage"
                    Anchor = "usage"
                }
            ]
        FrontMatter = null
        Dependencies = []
        Data = Map.empty
    }

let private first = page [ "guide"; "first" ] "First"
let private second = page [ "guide"; "second" ] "Second"

/// The second of two pages, so it has a previous page and the theme draws its page nav.
let private contextOf (frontMatter: 'FrontMatter) : PageContext<'FrontMatter> =
    {
        Page = second
        FrontMatter = frontMatter
        Site = site
        Pages = [ first; second ]
        Content = "<p>Body</p>"
    }

let private render (options: ThemeOptions) (doc: DocPage) =
    Theme.shell options doc (contextOf "theirs") |> Render.htmlView

let private everything =
    Theme.defaults
    |> Theme.editUrl "https://example.com/edit/"
    |> Theme.footer (Html.p "Theirs")

let private marker name : ThemeComponent =
    fun _ _ _ -> Html.div [ prop.custom ("data-site", name) ]

[<Tests>]
let tests =
    testList
        "theme components"
        [
            test "left alone, every part is the theme's" {
                let html = render everything (DocPage.create "Second")

                for theirs in
                    [
                        "nacara-navbar"
                        "nacara-sidebar"
                        "nacara-toc"
                        "nacara-page-nav"
                        "nacara-edit-link"
                        "nacara-footer"
                    ] do
                    Expect.stringContains html theirs $"the theme's %s{theirs}"
            }

            test "each part the site replaces is the site's, and only that one" {
                let cases =
                    [
                        "navbar", Theme.replaceNavbar, "class=\"nacara-navbar\""
                        "sidebar", Theme.replaceSidebar, "class=\"nacara-sidebar\""
                        "toc", Theme.replaceToc, "class=\"nacara-toc\""
                        "page-nav", Theme.replacePageNav, "class=\"nacara-page-nav\""
                        "edit-link", Theme.replaceEditLink, "class=\"nacara-edit-link\""
                        "footer", Theme.replaceFooter, "class=\"nacara-footer\""
                    ]

                for name, replace, theirs in cases do
                    let html = render (everything |> replace (marker name)) (DocPage.create "Second")
                    Expect.stringContains html $"data-site=\"%s{name}\"" $"the site's %s{name}"
                    Expect.isFalse (html.Contains theirs) $"not the theme's %s{name}"

                    for other, _, theirsToo in cases do
                        if other <> name then
                            Expect.stringContains html theirsToo $"replacing %s{name} keeps %s{other}"
            }

            test "the page still decides whether a part is drawn" {
                let options =
                    everything
                    |> Theme.replaceToc (marker "toc")
                    |> Theme.replacePageNav (marker "page-nav")
                    |> Theme.replaceEditLink (marker "edit-link")

                let html = render options (DocPage.create "Second" |> DocPage.splash)

                for name in [ "toc"; "page-nav"; "edit-link" ] do
                    Expect.isFalse (html.Contains $"data-site=\"%s{name}\"") $"a splash page has no %s{name}"

                let withMenu = render options (DocPage.create "Second" |> DocPage.withoutToc)
                Expect.stringContains withMenu "class=\"nacara-sidebar\"" "the page keeps its menu"
                Expect.isFalse (withMenu.Contains "data-site=\"toc\"") "and has no table of contents"
            }

            test "a replacement is given the page and the front matter" {
                let options =
                    everything
                    |> Theme.replaceFooter (fun _ doc context ->
                        Html.footer [ prop.text $"%s{doc.Title} / %s{unbox<string> context.FrontMatter}" ]
                    )

                let html = render options (DocPage.create "Second")
                Expect.stringContains html "Second / theirs" "both reach it"
            }

            test "a replacement can wrap the theme's own part" {
                let options =
                    everything
                    |> Theme.replaceNavbar (fun options _ context ->
                        Html.div
                            [
                                prop.custom ("data-site", "banner")
                                prop.children [ Components.navbar options context ]
                            ]
                    )

                let html = render options (DocPage.create "Second")
                Expect.stringContains html "data-site=\"banner\"" "the site's wrapper"
                Expect.stringContains html "class=\"nacara-navbar\"" "around the theme's navbar"
            }

            test "markup before and after the content sits around it, inside main" {
                let options =
                    everything
                    |> Theme.beforeContent (marker "before")
                    |> Theme.afterContent (marker "after")

                let html = render options (DocPage.create "Second")

                let at (text: string) =
                    let index = html.IndexOf text
                    Expect.isGreaterThanOrEqual index 0 $"%s{text} is there"
                    index

                let order =
                    [
                        "id=\"nacara-content\""
                        "data-site=\"before\""
                        "<h1>Second</h1>"
                        "<p>Body</p>"
                        "data-site=\"after\""
                        "class=\"nacara-edit-link\""
                        "class=\"nacara-page-nav\""
                    ]
                    |> List.map at

                Expect.equal order (List.sort order) "main, before, title, content, after, edit link, page links"
            }

            test "the theme puts nothing before or after the content" {
                let plain = render everything (DocPage.create "Second")
                Expect.isFalse (plain.Contains "data-site") "nothing of the site's"
            }

            test "a layout's parts win on its pages, and the site's fill the rest" {
                let options =
                    everything
                    |> Theme.addLayout "landing" DocPage.splash
                    |> Theme.replaceNavbar (marker "site-navbar")
                    |> Theme.replaceFooter (marker "site-footer")
                    |> Theme.layoutComponents "landing" (ThemeComponents.navbar (marker "landing-navbar"))

                let landing = render options (DocPage.create "Second" |> DocPage.splash |> DocPage.layout "landing")
                Expect.stringContains landing "data-site=\"landing-navbar\"" "the layout's navbar"
                Expect.isFalse (landing.Contains "data-site=\"site-navbar\"") "not the site's"
                Expect.stringContains landing "data-site=\"site-footer\"" "the site's footer, which it left alone"

                let ordinary = render options (DocPage.create "Second" |> DocPage.layout "docs")
                Expect.stringContains ordinary "data-site=\"site-navbar\"" "other layouts keep the site's"
                Expect.isFalse (ordinary.Contains "landing-navbar") "and never see the layout's"
            }

            test "a layout leaves out the site's banner by rendering nothing there" {
                let options =
                    everything
                    |> Theme.beforeContent (marker "banner")
                    |> Theme.layoutComponents "bare" (ThemeComponents.beforeContent (fun _ _ _ -> Html.none))

                let bare = render options (DocPage.create "Second" |> DocPage.bare |> DocPage.layout "bare")
                Expect.isFalse (bare.Contains "data-site=\"banner\"") "the bare layout has none"

                let ordinary = render options (DocPage.create "Second")
                Expect.stringContains ordinary "data-site=\"banner\"" "every other page does"
            }

            test "a layout's parts build up over more than one call" {
                let options =
                    everything
                    |> Theme.layoutComponents "splash" (ThemeComponents.navbar (marker "one"))
                    |> Theme.layoutComponents "splash" (ThemeComponents.footer (marker "two"))

                let html = render options (DocPage.create "Second" |> DocPage.splash |> DocPage.layout "splash")
                Expect.stringContains html "data-site=\"one\"" "the first call's navbar"
                Expect.stringContains html "data-site=\"two\"" "and the second's footer"
            }

            test "laid over another, each part is the upper one's, or the lower one's where it has none" {
                let parts: (string * (ThemeComponent -> ThemeComponents -> ThemeComponents) * (ThemeComponents -> ThemeComponent option)) list =
                    [
                        "navbar", ThemeComponents.navbar, _.Navbar
                        "sidebar", ThemeComponents.sidebar, _.Sidebar
                        "toc", ThemeComponents.toc, _.Toc
                        "page-nav", ThemeComponents.pageNav, _.PageNav
                        "edit-link", ThemeComponents.editLink, _.EditLink
                        "footer", ThemeComponents.footer, _.Footer
                        "before", ThemeComponents.beforeContent, _.BeforeContent
                        "after", ThemeComponents.afterContent, _.AfterContent
                    ]

                let drawn (part: ThemeComponent option) =
                    match part with
                    | Some render -> render everything (DocPage.create "Second") (contextOf (box ())) |> Render.htmlView
                    | None -> "none"

                for name, set, get in parts do
                    let under = ThemeComponents.none |> set (marker "under")
                    let over = ThemeComponents.none |> set (marker "over")

                    Expect.stringContains
                        (drawn (get (ThemeComponents.layer under over)))
                        "\"over\""
                        $"%s{name}: the upper one wins"

                    Expect.stringContains
                        (drawn (get (ThemeComponents.layer under ThemeComponents.none)))
                        "\"under\""
                        $"%s{name}: the lower one fills in"

                    Expect.equal
                        (drawn (get (ThemeComponents.layer ThemeComponents.none ThemeComponents.none)))
                        "none"
                        $"%s{name}: with neither, the theme's"
            }

            test "markup before the content is drawn on bare pages too" {
                let options = everything |> Theme.beforeContent (marker "banner")
                let bare = render options (DocPage.create "Second" |> DocPage.bare)
                Expect.stringContains bare "data-site=\"banner\"" "a canvas still has it"
            }

            test "a page in a layout says the layout's name, for stylesheets" {
                let named = render everything (DocPage.create "Second" |> DocPage.layout "landing")
                Expect.stringContains named "data-layout=\"landing\"" "on the layout's element"

                let plain = render everything (DocPage.create "Second")
                Expect.isFalse (plain.Contains "data-layout") "and nothing on a page with no layout"
            }

            test "the theme's layout renders a page in a layout the site added" {
                let options =
                    everything
                    |> Theme.addLayout "landing" DocPage.splash
                    |> Theme.layoutComponents "landing" (ThemeComponents.navbar (marker "landing-navbar"))

                let frontMatter: DocFrontMatter =
                    {
                        Title = "Second"
                        Description = None
                        Order = None
                        Layout = Some "landing"
                        PageNav = None
                        MenuFilter = None
                        MenuMemory = None
                        Toc = None
                        Main = []
                    }

                let html = Theme.layout options (contextOf frontMatter) |> Render.htmlView
                Expect.stringContains html "data-site=\"landing-navbar\"" "the layout's own navbar"
                Expect.stringContains html "data-layout=\"landing\"" "named as the site called it"
            }

            test "parts for a layout nobody added fail, naming the ones there are" {
                let message =
                    try
                        Theme.defaults
                        |> Theme.layoutComponents "landing" (ThemeComponents.navbar (marker "x"))
                        |> ignore

                        ""
                    with error ->
                        error.Message

                Expect.stringContains message "'landing'" "names what was asked for"
                Expect.stringContains message "bare, docs, splash" "and what there is"
            }
        ]
