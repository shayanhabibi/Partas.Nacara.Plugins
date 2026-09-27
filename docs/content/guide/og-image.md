---
title: Link preview images
description: Open Graph and Twitter image tags from front matter, with a site-wide fallback
---

The image a chat app or a social network shows when someone shares a page. A page
names its own in front matter; one that does not falls back to whatever the site
configured.

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.OgImage
```

```fsharp
open Nacara.Plugins

let site =
    Site.create "My site"
    |> Site.origin "https://example.com"
    |> Markdown.register
    |> OgImage.registerWith (
        OgImage.defaultImage (
            OgImage.image "/images/card.png"
            |> OgImage.withAlt "My site"
            |> OgImage.withSize 1200 630
        )
    )
```

## In front matter

The first of `og_image`, `ogImage` and `image` a page has names its image:

```yaml
---
title: Release notes
og_image: /images/release.png
og_image_alt: The 2.0 banner
---
```

A mapping says everything at once:

```yaml
og_image:
  url: https://cdn.example.com/release.png
  alt: The 2.0 banner
  width: 1200
  height: 630
```

`og_image: false` leaves the page without one, fallbacks included.

## Fallbacks

A page with nothing in front matter is asked of `Fallback` first, then given
`Default`. `Fallback` sees the whole page, so a collection or a section can have a
card of its own:

```fsharp
OgImage.registerWith (fun options ->
    options
    |> OgImage.fallback (fun page ->
        match page.Route.Segments with
        | "blog" :: _ -> Some(OgImage.image "/images/blog.png")
        | _ -> None)
    |> OgImage.defaultImage (OgImage.image "/images/card.png"))
```

`OgImage.frontMatterKeys` and `OgImage.altKeys` change which keys are read, and
`OgImage.excludeCollections` turns the plugin off for whole collections.

## What ends up in the page

`og:image`, its alt text and size when known, and `twitter:image`, added to the
head of the rendered page — so it works under any theme. A path is made absolute
with the site's origin, since a crawler has no page to resolve it against; a site
without `Site.origin` is warned about. A path whose file is not in the output is
warned about too, pointing at the page that asked for it.

A theme that writes `og:image` itself wins: the plugin adds nothing to a head that
already has one. Such a theme reads what the plugin resolved from the page, under
`OgImage.DataKey`, as an `OgImage option`.
