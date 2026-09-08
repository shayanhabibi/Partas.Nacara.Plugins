# Partas.Nacara.Plugins

## Tailwind

TailwindCSS in Nacara!

```bash
dotnet add package Partas.Nacara.Plugins.Tailwind
```

```fsharp
open Nacara.Core
open Nacara.Plugins
open Nacara.Theme

let site =
    Site.create "Partas.Nacara.Plugins"
    |> Site.baseUrl "/Partas.Nacara.Plugins/"
    |> Site.origin "https://shayanhabibi.github.io"
    |> Site.output "output"
    |> Site.staticFiles "static"
    |> Markdown.register
    |> TailwindCss.register
    // register with options
    |> TailwindCss.registerWith (fun twOpts ->
        {
            twOpts with
                TailwindEntryFooter = [
                    "@base {"
                    " --noot: var(--black);"
                    "}"
                ]
        }
        )
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```

> [!NOTE]
> Base styles are not injected by default since it will
> conflict with the base nacara styling.
> You can change this by setting `TailwindEntryHeader = []` or `TailwindEntryHeader = [ "@import \"tailwindcss\"" ]`
