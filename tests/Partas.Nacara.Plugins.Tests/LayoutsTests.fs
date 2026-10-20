module Partas.Nacara.Plugins.Tests.Layouts

open Expecto
open Partas.Nacara.Theme

let private frontMatter layout =
    {
        Title = "A page"
        Description = None
        Order = None
        Layout = layout
        PageNav = None
        MenuFilter = None
        MenuMemory = None
        Toc = None
        Main = []
    }

let private pageOf (options: ThemeOptions) layout =
    DocFrontMatter.toDocPageWith options.Layouts (frontMatter layout)

[<Tests>]
let tests =
    testList
        "theme layouts"
        [
            test "no layout is the ordinary page, named nothing" {
                let page = pageOf Theme.defaults None
                Expect.equal page (DocPage.create "A page") "everything the theme offers"
            }

            test "splash is wide and bare of navigation, and says its name" {
                let page = pageOf Theme.defaults (Some "splash")
                Expect.isFalse page.ShowMenu "no menu"
                Expect.isFalse page.ShowToc "no table of contents"
                Expect.isTrue page.Styled "still the theme's prose styles"
                Expect.equal page.Layout (Some "splash") "named, for data-layout"
            }

            test "docs names the ordinary page" {
                let page = pageOf Theme.defaults (Some "docs")
                Expect.equal page { DocPage.create "A page" with Layout = Some "docs" } "the ordinary page"
            }

            test "a site's own layout is what its function makes of the page" {
                let options =
                    Theme.defaults |> Theme.addLayout "landing" (DocPage.splash >> DocPage.withoutMenuMemory)

                let page = pageOf options (Some "landing")
                Expect.isFalse page.ShowMenu "from splash"
                Expect.isFalse page.MenuMemory "and its own change"
                Expect.equal page.Layout (Some "landing") "named as the site called it"
            }

            test "a site's layout of a built-in name replaces it" {
                let options = Theme.defaults |> Theme.addLayout "splash" id
                let page = pageOf options (Some "splash")
                Expect.isTrue page.ShowMenu "the site's splash keeps the menu"
            }

            test "front matter still has the last word over the layout" {
                let page =
                    DocFrontMatter.toDocPageWith
                        Theme.defaults.Layouts
                        { frontMatter (Some "docs") with Toc = Some TocOff }

                Expect.isFalse page.ShowToc "toc: false wins"
            }

            test "a layout nobody added fails, naming the ones there are" {
                let message =
                    try
                        pageOf Theme.defaults (Some "landing") |> ignore
                        ""
                    with error ->
                        error.Message

                Expect.stringContains message "'landing'" "names what was asked for"
                Expect.stringContains message "bare, docs, splash" "and what there is"
            }
        ]
