module Partas.Nacara.Plugins.Tests.AgentFriendly

open Expecto
open Nacara.Core
open global.Nacara.Plugins

let private locale = Locale.root "en"

let private site : SiteInfo =
    {
        Title = "Demo"
        Description = Some "A demo site"
        Url = SiteUrl.create "/Demo/"
        Origin = Some "https://example.com"
        Locales = [ locale ]
        RootLocale = locale
        PageAssets = []
    }

let private page (segments: string list) title format body (description: string option) : Page =
    let page =
        {
            Id = "docs:" + String.concat "/" segments
            Collection = "docs"
            Source = PageSource.Generated "test"
            ProjectPath = None
            Format = format
            Locale = locale
            Route = Route.create locale segments
            Title = title
            Order = 0
            Body = body
            BodyLine = 1
            Html = ""
            Headings = []
            FrontMatter = null
            Dependencies = []
            Data = Map.empty
        }

    page.WithData(AgentFriendly.DescriptionKey, description)

let private pages =
    [
        page [ "guide"; "setup" ] "Setup" ".md" "Install it.\r\n\r\n## Next\nmore" None
        page [] "Home" ".md" "Welcome." (Some "Start here")
        page [ "reference"; "api" ] "API" ".fsx" "let x = 1" None
    ]

let private agentPages options = AgentFriendly.agentPages site options pages

[<Tests>]
let tests =
    testList
        "agent friendly"
        [
            test "a page's markdown sits where its url would be" {
                Expect.equal
                    ([ []; [ "guide"; "setup" ] ]
                     |> List.map (Route.create locale >> AgentFriendly.markdownPath))
                    [ "index.md"; "guide/setup.md" ]
                    "paths"
            }

            test "only markdown sources get a copy, and llms.txt links to it" {
                let byTitle =
                    agentPages AgentFriendly.defaults |> List.map (fun page -> page.Page.Title, page) |> Map.ofList

                Expect.equal byTitle["Setup"].Url "https://example.com/Demo/guide/setup.md" "markdown"
                Expect.equal byTitle["API"].Url "https://example.com/Demo/reference/api/" "html"
                Expect.isNone byTitle["API"].MarkdownPath "no copy"
            }

            test "without copies, every page is linked by its html" {
                let options = AgentFriendly.defaults |> AgentFriendly.markdownCopies false

                for page in agentPages options do
                    Expect.isFalse (page.Url.EndsWith ".md") page.Url
            }

            test "llms.txt lists pages under their sections, the top of the site first" {
                Expect.equal
                    (AgentFriendly.llmsTxt site AgentFriendly.defaults (agentPages AgentFriendly.defaults))
                    ("# Demo\n\n> A demo site\n\n"
                     + "## Pages\n\n- [Home](https://example.com/Demo/index.md): Start here\n\n"
                     + "## Guide\n\n- [Setup](https://example.com/Demo/guide/setup.md)\n\n"
                     + "## Reference\n\n- [API](https://example.com/Demo/reference/api/)\n")
                    "llms.txt"
            }

            test "the summary and details are the site's to set" {
                let options =
                    AgentFriendly.defaults
                    |> AgentFriendly.summary "Custom"
                    |> AgentFriendly.details "Read the guide first."

                let text = AgentFriendly.llmsTxt site options (agentPages options)
                Expect.stringStarts text "# Demo\n\n> Custom\n\nRead the guide first.\n\n## Pages" "header"
            }

            test "a page's markdown restores its title and description" {
                let home = agentPages AgentFriendly.defaults |> List.find (fun page -> page.Page.Title = "Home")
                Expect.equal (AgentFriendly.markdown home) "# Home\n\n> Start here\n\nWelcome.\n" "markdown"
            }

            test "a body that opens with a heading keeps its own" {
                let own =
                    AgentFriendly.agentPages site AgentFriendly.defaults [ page [ "x" ] "X" ".md" "# Own\n\nText" None ]
                    |> List.exactlyOne

                Expect.equal (AgentFriendly.markdown own) "# Own\n\nText\n" "markdown"
            }

            test "llms-full.txt holds every markdown page in llms.txt's order" {
                let full = AgentFriendly.llmsFullTxt site AgentFriendly.defaults (agentPages AgentFriendly.defaults)
                Expect.stringStarts full "# Demo\n\n<!-- Source: https://example.com/Demo/index.md -->" "home first"
                Expect.stringContains full "# Setup\n\nInstall it.\n\n## Next\nmore\n" "line endings normalised"
                Expect.isFalse (full.Contains "let x = 1") "no fsx"
            }

            test "an excluded collection is left out of everything" {
                let options = AgentFriendly.defaults |> AgentFriendly.excludeCollections [ "docs" ]
                Expect.isEmpty (agentPages options) "nothing"
            }

            test "the alternate link is escaped" {
                Expect.equal
                    (AgentFriendly.alternateLink "/a.md?x=1&y=\"2\"")
                    "<link rel=\"alternate\" type=\"text/markdown\" href=\"/a.md?x=1&amp;y=&quot;2&quot;\">"
                    "link"
            }
        ]
