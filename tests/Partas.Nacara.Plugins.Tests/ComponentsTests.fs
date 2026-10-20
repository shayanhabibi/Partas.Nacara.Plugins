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
let private render (options: ThemeOptions) (doc: DocPage) =
    let context: PageContext<string> =
        {
            Page = second
            FrontMatter = "theirs"
            Site = site
            Pages = [ first; second ]
            Content = "<p>Body</p>"
        }

    Theme.shell options doc context |> Render.htmlView

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
        ]
