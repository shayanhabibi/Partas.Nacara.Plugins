module Partas.Nacara.Plugins.Tests.OgImage

open Expecto
open Nacara.Core
open global.Nacara.Plugins
open YamlDotNet.RepresentationModel

let private locale = Locale.root "en"

let private site origin : SiteInfo =
    {
        Title = "Demo"
        Description = None
        Url = SiteUrl.create "/Demo/"
        Origin = origin
        Locales = [ locale ]
        RootLocale = locale
        PageAssets = []
    }

let private page collection title : Page =
    {
        Id = $"%s{collection}:%s{title}"
        Collection = collection
        Source = PageSource.Generated "test"
        ProjectPath = None
        Format = ".md"
        Locale = locale
        Route = Route.create locale [ "guide"; title ]
        Title = title
        Order = 0
        Body = ""
        BodyLine = 1
        Html = ""
        Headings = []
        FrontMatter = null
        Dependencies = []
        Data = Map.empty
    }

let private frontMatter (yaml: string) =
    match Yaml.parse yaml with
    | Ok(Some(:? YamlMappingNode as mapping)) -> OgImage.fromFrontMatter OgImage.defaults mapping
    | other -> failwithf "not a mapping: %A" other

let private head = "<html><head><title>x</title></head><body></body></html>"

[<Tests>]
let tests =
    testList
        "og image"
        [
            testList
                "front matter"
                [
                    test "a path, with alt text beside it" {
                        Expect.equal
                            (frontMatter "og_image: /img/a.png\nog_image_alt: A picture")
                            (OgImage.Specified(OgImage.image "/img/a.png" |> OgImage.withAlt "A picture"))
                            "image"
                    }

                    test "a mapping carries its own alt and size" {
                        Expect.equal
                            (frontMatter "image: { url: https://cdn.x/a.png, alt: Cdn, width: 1200, height: 630 }")
                            (OgImage.Specified(
                                OgImage.image "https://cdn.x/a.png"
                                |> OgImage.withAlt "Cdn"
                                |> OgImage.withSize 1200 630
                            ))
                            "image"
                    }

                    test "the first key wins" {
                        Expect.equal
                            (frontMatter "image: /b.png\nog_image: /a.png")
                            (OgImage.Specified(OgImage.image "/a.png"))
                            "og_image before image"
                    }

                    test "false opts out" {
                        Expect.equal (frontMatter "og_image: false") OgImage.OptedOut "opted out"
                    }

                    test "no key leaves it to the fallbacks" {
                        Expect.equal (frontMatter "title: Hello") OgImage.Unspecified "unspecified"
                    }
                ]

            testList
                "resolve"
                [
                    let options =
                        OgImage.defaults
                        |> OgImage.defaultImage (OgImage.image "/default.png")
                        |> OgImage.fallback (fun page ->
                            if page.Collection = "blog" then Some(OgImage.image "/blog.png") else None
                        )

                    test "front matter beats the fallbacks" {
                        Expect.equal
                            (OgImage.resolve options (OgImage.Specified(OgImage.image "/own.png")) (page "blog" "a"))
                            (Some(OgImage.image "/own.png"))
                            "own image"
                    }

                    test "the fallback is asked before the default" {
                        Expect.equal
                            (OgImage.resolve options OgImage.Unspecified (page "blog" "a"))
                            (Some(OgImage.image "/blog.png"))
                            "fallback"

                        Expect.equal
                            (OgImage.resolve options OgImage.Unspecified (page "docs" "a"))
                            (Some(OgImage.image "/default.png"))
                            "default"
                    }

                    test "opting out skips the fallbacks" {
                        Expect.isNone (OgImage.resolve options OgImage.OptedOut (page "docs" "a")) "no image"
                    }

                    test "an excluded collection gets nothing" {
                        let options = options |> OgImage.excludeCollections [ "blog" ]

                        Expect.isNone
                            (OgImage.resolve options (OgImage.Specified(OgImage.image "/own.png")) (page "blog" "a"))
                            "no image"
                    }
                ]

            testList
                "tags"
                [
                    test "a path is made absolute under the base url" {
                        Expect.equal
                            (OgImage.absoluteUrl (site (Some "https://example.com")) "/img/a.png")
                            "https://example.com/Demo/img/a.png"
                            "absolute"

                        Expect.equal (OgImage.absoluteUrl (site None) "img/a.png") "/Demo/img/a.png" "no origin"

                        Expect.equal
                            (OgImage.absoluteUrl (site (Some "https://example.com")) "https://cdn.x/a.png")
                            "https://cdn.x/a.png"
                            "already absolute"
                    }

                    test "the head gains og and twitter tags" {
                        let html =
                            OgImage.apply
                                (site (Some "https://example.com"))
                                (OgImage.image "/a.png" |> OgImage.withAlt "A \"b\"" |> OgImage.withSize 1200 630)
                                head

                        let expected =
                            [
                                "<meta property=\"og:image\" content=\"https://example.com/Demo/a.png\">"
                                "<meta property=\"og:image:width\" content=\"1200\">"
                                "<meta property=\"og:image:height\" content=\"630\">"
                                "<meta property=\"og:image:alt\" content=\"A &quot;b&quot;\">"
                                "<meta name=\"twitter:card\" content=\"summary_large_image\">"
                                "<meta name=\"twitter:image\" content=\"https://example.com/Demo/a.png\">"
                            ]

                        for tag in expected do
                            Expect.stringContains html tag tag

                        Expect.isLessThan (html.IndexOf "og:image") (html.IndexOf "</head>") "inside the head"
                    }

                    test "a twitter card the theme wrote is kept" {
                        let html =
                            OgImage.apply
                                (site None)
                                (OgImage.image "/a.png")
                                "<head><meta name=\"twitter:card\" content=\"summary\"></head>"

                        Expect.isFalse (html.Contains "summary_large_image") "not overridden"
                    }

                    test "a head with og:image already is left alone, minified or not" {
                        for html in
                            [
                                "<head><meta property=\"og:image\" content=\"y\"></head>"
                                "<head><meta property=og:image content=y></head>"
                            ] do
                            Expect.equal (OgImage.apply (site None) (OgImage.image "/a.png") html) html html
                    }

                    test "only a site path is looked for in the output" {
                        Expect.equal (OgImage.localPath "/img/a.png?v=2") (Some "img/a.png") "local"
                        Expect.isNone (OgImage.localPath "https://cdn.x/a.png") "remote"
                    }
                ]
        ]
