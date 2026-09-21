# Custom `:::` directives

## The problem

Nacara renders `:::steps`, `:::filetree` and `:::preview`, and nothing else. The code
that does it lives in `Nacara.Plugins.Internal`: `NacaraContainerRenderer`, a
`DirectiveResult` union closed over those three cases, and `Directive.builtIn`. None of
it is public. `MarkdownOptions` exposes `WarnOnUnknownLanguage`, `StrictLinks`, `Toc` and
`GithubRepo` — no directive hook. `Nacara.Core` exposes `ICodeBlockRenderer`,
`IHighlighter` and `IPlugin` — no directive interface.

So a site that wants `:::note` has no way to get one, and a plugin that wants to ship a
component has nowhere to put it.

## The opening

`Markdown.pipelineFor` takes a `Registry`, documented as *"What plugins contributed, read
for `IMarkdownExtension`."* `Nacara.Core` says the same from the other side: *"Extras is
how plugins extend each other with the core knowing about neither: the markdown plugin
reads every `MarkdigExtension` registered, a theme every `NavbarItem`."*

A plugin can therefore contribute a Markdig extension through `Registry.extra` and take
part in parsing and rendering. No fork of the markdown plugin is needed. What is missing
is not the hook but the ergonomics: reaching it today means writing an
`IMarkdownExtension` and an `HtmlObjectRenderer<CustomContainer>` by hand, then pulling
arguments out of a raw string yourself.

## Authoring surface

Arguments are inline pairs on the opening line:

```markdown
:::note title="Watch out" level=2
Body **markdown**, rendered by Markdig as usual.
:::
```

A directive is a decoder and a render function:

```fsharp
type Note = { Title: string option; Level: int }

Directive.create "note" (Decode.object (fun get ->
    { Title = get.Optional.Field "title" Decode.string
      Level = get.Optional.Field "level" Decode.int |> Option.defaultValue 1 }))
|> Directive.render (fun ctx args body ->
    Html.aside [
        prop.className $"nacara-note nacara-note--{args.Level}"
        prop.children [
            match args.Title with
            | Some title -> Html.p [ prop.className "nacara-note__title"; prop.text title ]
            | None -> Html.none
            body
        ]
    ])
```

`Directive.create` takes the decoder so `args` is typed at `render` without an
annotation.

Two decisions carry most of the ergonomics.

**Arguments reuse Nacara's own decoders.** `Decoder<'T>` is
`string -> YamlNode -> Result<'T, DecodeError>` — it reads YAML. The theme already
decodes front matter this way (`src/Partas.Nacara.Theme/Types.fs:505`). A quote-aware
tokenizer turns `title="Watch out" level=2` into the flow mapping
`{title: "Watch out", level: 2}` and hands it to `Yaml.decode`. An author who has written
front matter has already learned this API. Bare `:::note` yields `{}`, so an all-optional
decoder succeeds and a `Required.Field` fails by name.

**Output is `ReactElement`.** `Feliz.ViewEngine` is already the theme's idiom
(`src/Partas.Nacara.Theme/Components.fs:3`), and `Render.htmlView` produces the string
Markdig needs.

The body cannot be a `ReactElement` tree, because only Markdig can render markdown. It
arrives as rendered HTML, wrapped in a `span` carrying `dangerouslySetInnerHTML` — the
same wrap `Components.rawHtml` already performs — so the author places `body` wherever
they like and never touches raw HTML themselves.

## Context

`CustomContainer` is a `ContainerBlock` and every Markdig `Block` has a `Parent`, so the
nesting structure already exists in the tree. What the tree does not hold is a parent's
*decoded arguments*; recovering those by re-parsing an ancestor's opening line would be
the wrong shape. The context therefore keeps a stack of decoded values, pushed on enter
and popped on exit. Rendering is depth-first and single-threaded per document, so the top
of the stack is always the enclosing directive.

Lookup is by type, following `Registry.extras<'T>`, where the type is the contract:

```fsharp
type Steps = { Start: int }
type Step  = { Title: string }

Directive.create "step" (Decode.object (fun get ->
    { Title = get.Required.Field "title" Decode.string }))
|> Directive.render (fun ctx args body ->
    match ctx.TryAncestor<Steps>() with
    | None ->
        ctx.Error ":::step only means something inside :::steps"
        Html.none
    | Some steps ->
        Html.li [
            prop.className "nacara-step"
            prop.custom ("data-step", steps.Start + ctx.Index)
            prop.children [ Html.h3 args.Title; body ]
        ])
```

The context carries four things:

- `TryAncestor<'T>()` / `TryParent<'T>()` — typed, no casting, no matching on names.
- `Index` — position among same-named siblings under one parent, which is what makes step
  numbering work without the author counting.
- `Depth` — for directives that nest into themselves.
- `Error` / `Warn` — a Nacara `Diagnostic`, so a misplaced directive fails the build
  rather than rendering silently wrong HTML.

This is more than the built-in `:::steps` can do, which hardcodes its parent/child
relationship inside `Nacara.Plugins.Internal` and admits no siblings.

The page is **not** in the context. `pipelineFor` takes only a `Registry`, and nothing
hands a `Page` to the pipeline, so a directive cannot see which page it is on or read its
front matter. Supplying that needs a change upstream in the markdown plugin.

One authoring rule falls out of Markdig rather than from this design, and authors have to
know it: a nested directive's fence must be **shorter** than its parent's. Custom
containers follow the fenced-block rule, so `::::steps` wraps `:::step`, and writing both
at three colons does not nest — the first `:::` closes the outer container, leaving the
inner directives as siblings plus a stray empty one. Verified against Markdig 1.3.2 by
dumping the parsed block tree both ways. `ctx.TryAncestor<'T>()` returns `None` in the
equal-fence case, which is the correct answer to a wrong question: the directive genuinely
has no ancestor once the parse has gone that way.

## Hooking into Markdig

One `IMarkdownExtension`, contributed with `Registry.extra`. Its
`Setup(pipeline, renderer)` calls `renderer.ObjectRenderers.Insert(0, ourRenderer)`, and
`Accept` returns true only for a `CustomContainer` whose `Info` names a registered
directive.

That selectivity is the point: this intercepts rather than replaces. `:::steps`,
`:::filetree` and `:::preview` keep falling through to `NacaraContainerRenderer`. Nothing
existing changes, and registering a directive under a built-in name shadows it
deliberately.

The body is rendered by swapping `renderer.Writer` for a `StringWriter`, calling
`renderer.WriteChildren container`, and restoring. Renderer state and every other
extension survive, so nested directives, code blocks and syntax highlighting inside a
`:::` all continue to work.

### Failure modes

| Case | Behaviour |
|---|---|
| Unknown name | Falls through to Nacara. Existing behaviour untouched. |
| Duplicate registration | Error at registration, before any page renders. |
| Arguments do not decode | `DecodeError` becomes a `Diagnostic`; the body renders unwrapped so content is still visible while the build reports the fault. |
| Malformed argument line | As above. |

`DiagnosticSink.For` takes a source name, and `DecodeError.toDiagnostic` takes an
`AbsolutePath`. Without the page, the path is unavailable, so a diagnostic names the
directive and its line within the page body. Markdig can carry a `MarkdownParserContext`
through parsing; if Nacara passes one holding the page, full `file:line` is reachable.
Whether it does is unknown and is the first thing implementation should establish. The
design degrades to name-plus-body-line if it does not.

## Packaging

A new `src/Partas.Nacara.Plugins.Directives`, referencing `Nacara.Core`, `Markdig`,
`Feliz.ViewEngine` and `Partas.Nacara.Theme.Contracts` — but not the theme. A directive
pack must work against any Nacara theme.

The contracts reference settles an open question from `2c93322`. `PluginLayers.offer` was
committed unused and flagged as speculative. This is its purpose:

```fsharp
PluginLayers.offer { Name = "directives"; Css = myDirectiveCss }
```

`Theme.effectiveLayers` already appends offered layers to its own, and `ThemeLayers.declare`
runs afterwards, so a directive's CSS lands in a named cascade layer and Tailwind orders
itself against it with no change to either. The order holds because the theme registers
last, after the plugins whose menus it reads. `PluginLayers` stays.

Version one ships the kit alone: `Directive`, the context, the tokenizer, the Markdig
extension, `Directives.register`. No built-in directives and no CSS — Nacara already
renders notes and tips, and a built-in set is a separate decision once the kit has proved
itself.

Deliberately deferred: a YAML block form for directives needing nested arguments
(`data: [1, 2, 3]`). Inline pairs cover notes, steps, tabs and previews. Should something
need more, it is a second decoder entry point rather than a redesign.

## Testing

Unit tests go in the existing `tests/Partas.Nacara.Plugins.Tests` (Expecto, currently a
scaffold):

1. The tokenizer — quoted values containing spaces, embedded `=`, bare `:::name`,
   unbalanced quotes.
2. The ancestor stack — `TryAncestor<'T>` across two levels, `Index` resetting per parent.
3. Decode failures returning a `DecodeError` rather than throwing.

The renderer is proved end to end: a `:::steps` / `:::step` pair added to `docs/`, built,
with the emitted HTML asserted. That single pass exercises the kit, `PluginLayers` and
Tailwind's layer ordering together. Earlier in this work two "verifications" were
worthless because they built the upstream `Nacara.Theme.Default` package rather than the
local project; an end-to-end assertion over real output is the one to trust.

## Estimate

About a day for the kit. The first step — determining whether Markdig's parser context
carries the page, and so whether diagnostics get `file:line` — is under an hour and
blocks nothing else.

## Spike result

**Degraded case confirmed.** Decompiling `Nacara.Plugin.Markdown` 1.0.0-beta.2 shows
`transform` calls `Markdig.Markdown.Parse(page.Body, pipelineFor(context.Registry),
(MarkdownParserContext)null)` — the parser context is unconditionally `null`, and nothing
in the assembly ever calls `MarkdownObject.SetData`. A probe `IMarkdownExtension`
contributed via `Registry.extra` and built into `docs/Site.fs` confirmed this at runtime:
`doc.GetData(...)` returned `null` for every candidate key on both pages, and
`doc.ContainsData(typeof<Page>)` was `false`. `pipelineFor` also caches the built
`MarkdownPipeline` once per `Registry` for the whole build, so an extension added this
way is a single shared instance across every page regardless.

The same absence holds for diagnostics: `TransformContext.Diagnostics` (a
`DiagnosticSink`) exists only on `TransformContext`, which a `Registry`-scoped
`IMarkdownExtension` never receives — its `Setup` overloads see only
`MarkdownPipelineBuilder`/`MarkdownPipeline`/`IMarkdownRenderer`. The one place both page
and diagnostics *are* reachable is `Nacara.Plugins.Internal.NacaraContainerRenderer`,
which the markdown plugin builds fresh per page inside `transform` and hands `context`
and `page` as closed-over constructor arguments before swapping it into the renderer's
`ObjectRenderers` — a privileged path open only to the markdown plugin itself, not to a
third-party extension registered through `Registry.extra`.

So a directive registered as a Markdig extension per this design's "Hooking into
Markdig" section cannot reach `file:line` or a `Diagnostic` sink by any channel Markdig
or Nacara currently exposes to it. The name-plus-body-line degraded path this design
already specifies is the one to implement; nothing here changes its shape.
