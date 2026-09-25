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
    |> Theme.navbar [NavbarSection("Guide", "guide", "/guide/getting-started/")]
    // One section per package, so the menu reads as the list of things on offer
    // rather than as a flat pile of pages.
    |> Theme.menu
           "guide"
           [ Menu.page "guide/getting-started.md"
             Menu.section "Directives" [ Menu.page "guide/directives.md" ]
             |> Menu.badge "New"
             Menu.section "Solid" [ Menu.page "guide/solid.md" ]
             |> Menu.badge "New"
             Menu.section "Tailwind" [ Menu.page "guide/tailwind.md" ]
             Menu.section "DaisyUI" [ Menu.page "guide/daisyui.md" ]
             Menu.section
                 "Theme"
                 [ Menu.page "guide/theme.md"
                   Menu.page "guide/styling.md" |> Menu.badge "New" ]
             |> Menu.badge "New" ]
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

/// Which Partas.Solid the live examples compile against. PARTAS_SOLID_FEED points at a folder of
/// locally packed nupkgs, for trying the docs against an unreleased Partas.Solid.
let private solidExamples (options: SolidExamplesOptions) =
    let fromEnvironment name =
        match System.Environment.GetEnvironmentVariable name with
        | null
        | "" -> None
        | value -> Some value

    options
    |> SolidExamples.partasVersion (fromEnvironment "PARTAS_SOLID_VERSION" |> Option.defaultValue "3.*")
    |> (fromEnvironment "PARTAS_SOLID_FEED" |> Option.map SolidExamples.feed |> Option.defaultValue id)

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
    |> SolidExamples.registerWith solidExamples
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
