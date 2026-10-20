module Docs.Site

open Feliz.ViewEngine
open Nacara.Core
open Nacara.Plugins
open Partas.Nacara.Theme

let versions = [SiteVersion.root "1.0"]

/// The API of every package this repository publishes, read from the assemblies this site is
/// built with.
let apiOptions =
    { FSharpApi.defaults with
        Root = "reference"
        Exclude = [ "Nacara.Plugins.Internal" ]
        Sources =
            [
                let beside =
                    System.Reflection.Assembly.GetExecutingAssembly().Location
                    |> System.IO.Path.GetDirectoryName

                for name in
                    [
                        "Partas.Nacara.Theme"
                        "Partas.Nacara.Theme.Contracts"
                        "Partas.Nacara.Plugins.Directives"
                        "Partas.Nacara.Plugins.Tailwind"
                        "Partas.Nacara.Plugins.DaisyUI"
                        "Partas.Nacara.Plugins.OgImage"
                        "Partas.Nacara.Plugins.AgentFriendly"
                        "Partas.Nacara.Plugins.Motion"
                    ] -> FSharpApiSource.create (System.IO.Path.Combine(beside, $"%s{name}.dll"))
            ]
    }

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
    |> Theme.navbar
           [ NavbarSection("Guide", "guide", "/guide/getting-started/")
             NavbarSection("Reference", "reference", "/reference/") ]
    // One section per package, so the menu reads as the list of things on offer
    // rather than as a flat pile of pages.
    |> Theme.menu
           "guide"
           [ Menu.page "guide/getting-started.md"
             Menu.section "Directives" [ Menu.page "guide/directives.md" ]
             Menu.section "OgImage" [ Menu.page "guide/og-image.md" ]
             Menu.section "AgentFriendly" [ Menu.page "guide/agent-friendly.md" ]
             Menu.section "Tailwind" [ Menu.page "guide/tailwind.md" ]
             Menu.section "DaisyUI" [ Menu.page "guide/daisyui.md" ]
             Menu.section
                 "Theme"
                 [ Menu.page "guide/theme.md"
                   Menu.page "guide/styling.md"
                   Menu.page "guide/figma.md" ]
             Menu.section "Motion" [ Menu.page "guide/motion.md" |> Menu.badge "New" ] ]
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

// The motion guide's demo cards at rest. The motion document holds only what a hover changes,
// so where a card starts from is the site's own CSS, in the values the Figma variants rest at.
PluginLayers.offer
    { Name = "motion-demo"
      Css =
          """
.motion-demo { display: flex; flex-wrap: wrap; gap: var(--nacara-space-4); margin-block: var(--nacara-space-6); }
[data-motion="card"] { display: flex; flex-direction: column; gap: var(--nacara-space-3); padding: var(--nacara-space-4); background-color: var(--nacara-bg-raised); border: 1px solid var(--nacara-border); border-radius: var(--nacara-radius); box-shadow: none; color: var(--nacara-text); }
[data-motion="card"][data-size="small"] { width: 12rem; }
[data-motion="card"][data-size="large"] { width: 18rem; }
[data-motion="card"] [data-motion-part="accent"] { display: block; width: 40px; height: 4px; border-radius: 2px; background-color: var(--nacara-primary); opacity: 1; }
""" }

let reference =
    FSharpApi.collection "reference" DocFrontMatter.decoder apiOptions
    |> Collection.title _.Title
    |> Collection.layout (Theme.layout theme)

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
    |> FSharpApi.register apiOptions
    |> LinkValidator.register
    |> DaisyUI.register
    |> Directives.register [ steps; step ]
    |> OgImage.registerWith (
        OgImage.defaultImage (
            OgImage.image "https://opengraph.githubassets.com/1/shayanhabibi/Partas.Nacara.Plugins"
            |> OgImage.withAlt "Partas.Nacara.Plugins on GitHub"
            |> OgImage.withSize 1200 600
        )
    )
    |> AgentFriendly.registerWith (
        AgentFriendly.summary "Plugins and a documentation theme for the Nacara static site generator, each an independent NuGet package"
        >> AgentFriendly.details "Start with the getting-started guide. The Reference section is the generated API of every published package."
    )
    // |> Rumdl.register
    // |> LightningCss.register
    |> Esbuild.register
    |> Nuglify.minifyHtml
    |> Versions.register versions
    |> GitHubPages.register
    // Resolved against the working directory, which is wherever the build is started from, so the
    // path is made absolute from this file.
    |> Motion.registerWith (
        Motion.document (System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../design/tokens/motion.json"))
        >> Motion.hoverEngine "card[size=small]" Anime
        // The cards' hover ends on a literal #f7f7ff in Figma, which stays light in the dark scheme.
        >> Motion.value "card[size=small]" "background-color" "var(--nacara-primary-subtle)"
        >> Motion.value "card[size=large]" "background-color" "var(--nacara-primary-subtle)"
    )
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
    |> Site.collection reference

[<EntryPoint>]
let main argv = Nacara.run site argv
