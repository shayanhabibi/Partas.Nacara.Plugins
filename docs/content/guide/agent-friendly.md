---
title: Agent-friendly sites
description: llms.txt, llms-full.txt and a markdown copy of every page
---

A site an agent can read without scraping it. The plugin writes an
[`llms.txt`](https://llmstxt.org) index, an `llms-full.txt` holding every page,
and a markdown copy of each page beside its html.

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.AgentFriendly
```

```fsharp
open Nacara.Plugins

let site =
    Site.create "My site"
    |> Site.origin "https://example.com"
    |> Markdown.register
    |> AgentFriendly.register
```

## What it writes

| File | What is in it |
| --- | --- |
| `/guide/setup.md` | The page `/guide/setup/` as authored, front matter stripped and its title restored as a heading |
| `/llms.txt` | The site's title and description, then every page as a link under a heading for its section |
| `/llms-full.txt` | Every markdown copy, one after another, in the order `llms.txt` lists them |

Each page's head also gains
`<link rel="alternate" type="text/markdown" href="…/setup.md">`, so an agent that
lands on the html finds the markdown.

Only pages written in markdown get a copy: the body is written as it was authored,
and that reads well only when it already is markdown. Other pages are still listed
in `llms.txt`, linked by their html.

A page's description in `llms.txt` is its front matter `description` or `summary`.

## Configuring it

```fsharp
AgentFriendly.registerWith (fun options ->
    options
    |> AgentFriendly.summary "Plugins for the Nacara static site generator"
    |> AgentFriendly.details "Start with the getting-started guide; every package is independent."
    |> AgentFriendly.section (fun page ->
        match page.Route.Segments with
        | "reference" :: _ -> "Optional"
        | _ -> AgentFriendly.sectionOf page)
    |> AgentFriendly.excludeCollections [ "drafts" ])
```

A section named `Optional` is, by the llms.txt proposal, what an agent may skip
when its context is short. `AgentFriendly.llmsTxtPath None` and
`AgentFriendly.llmsFullTxtPath None` leave either file out,
`AgentFriendly.markdownCopies false` the copies, and `AgentFriendly.alternate false`
the head link.
