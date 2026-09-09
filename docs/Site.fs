module Docs.Site

open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Nacara.Theme

let versions = [SiteVersion.root "1.0"]

let theme =
    Theme.defaults
    |> Theme.navbar [NavbarSection("Guide", "guide", "/guide/introduction/")]
    |> Theme.navbarEnd
           [// NavbarDynamicWidget Search.trigger
            NavbarDynamicWidget(Versions.switcher (Versions.versions versions Versions.defaults))
            NavbarIcon("GitHub", "https://github.com/shayanhabibi/Partas.Nacara.Plugins", Icons.github)
           ]
    |> Theme.editUrl "https://github.com/shayanhabibi/Partas.Nacara.Plugins/edit/main/docs"
    |> Theme.footer (Html.p [Html.text "Built with Nacara"])

let site =
    Site.create "Partas.Nacara.Plugins"
    |> Site.baseUrl "/Partas.Nacara.Plugins/"
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> TreeSitter.register
    |> Literate.register
    // |> Search.register
    |> Sitemap.register
    |> LinkValidator.register
    |> DaisyUI.register
    // |> Rumdl.register
    // |> LightningCss.register
    |> Esbuild.register
    |> Nuglify.minifyHtml
    |> Versions.register versions
    |> GitHubPages.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")

[<EntryPoint>]
let main argv = Nacara.run site argv
