module Docs.Site

open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let versions = [SiteVersion.root "1.0"]

let private steps =
    Directive.create "steps" (Decode.object (fun get ->
        {| Start = get.Optional.Field "start" Decode.int |> Option.defaultValue 1 |}))
    |> Directive.render (fun _ _ body -> Html.ol [ prop.className "nacara-steps"; prop.children [ body ] ])

let private step =
    Directive.create "step" (Decode.object (fun get ->
        {| Title = get.Required.Field "title" Decode.string |}))
    |> Directive.render (fun ctx args body ->
        match ctx.TryAncestor<{| Start: int |}>() with
        | None ->
            ctx.Error ":::step only means something inside :::steps"
            Html.none
        | Some parent ->
            Html.li [
                prop.className "nacara-step"
                prop.custom ("data-number", string (parent.Start + ctx.Index))
                prop.children [ Html.h3 args.Title; body ]
            ])

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

PluginLayers.offer
    { Name = "directives"
      Css = ".nacara-steps { list-style: none; } .nacara-step { margin-block: 1rem; }" }

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
    |> Directives.register [ steps; step ]
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
