# Partas.Nacara.Plugins

Independent NuGet packages for [Nacara](https://github.com/MangelMaxime/Nacara), an F# static
site generator whose site is an F# program. Each `src/` project is one package; `docs/` is the
documentation site, built with those packages; `tests/` is one Expecto project covering them all.

## Commands

`dotnet fsi build.fsx -- <command>`, with `--help` for the rest:

- `build`, `test`, `pack` — the solution in Release; `pack` writes to `bin/`.
- `docs` — builds the site into `docs/output`; `docs --watch` serves it with live reload.

For iterating on F#, load `tests/Partas.Nacara.Plugins.Tests` into a SageFs session: it references
every plugin, so the REPL reaches all of them. Run the full build and the unfiltered suite once,
at the end.

## Writing a plugin

A plugin is a module in `namespace Nacara.Plugins` with an options record, `defaults`, one setter
per option, and `create` / `createWith` / `register` / `registerWith`. Copy the shape from
`src/Partas.Nacara.Plugins.OgImage/OgImage.fs`. The Nacara engine source, for the `Registry` and
hook contracts, is at `../Nacara/src/Nacara.Core` (`Registry.fs`, `Build.fs`).

- **Front matter a plugin cannot type.** A collection decodes front matter into the site's own
  type. Read raw keys with `RawFrontMatter` from `src/Shared/PageSupport.fs`, inside a content
  transform, where `context.Registry.FrontMatterFormats` is available.
- **Shared code is a linked file.** `src/Shared/PageSupport.fs` is compiled into each package
  that lists it as `<Compile Include="../Shared/PageSupport.fs" Link="PageSupport.fs" />`, and
  stays `internal`: two assemblies exposing the same public module are ambiguous to a site
  referencing both.
- **Catch-all content transforms** (`Extensions = []`) claim every page. Pass the page through
  `CatchAll.passThrough` so a format no renderer claims still gets its body as html.
- **Adding to a page's head** from a plugin: an asset transform with `Extensions = []` that
  filters on `context.Path` and uses `Head.inject`. Naming `.html` triggers a
  `duplicate-asset-transform` warning beside the html minifier.
- **Anything written after the build** goes through `HookContext.Write` in `onBuildComplete`,
  which keeps the file from being pruned.
- **Public functions are the test surface.** Tests reach plugins through their public API
  (`OgImage.resolve`, `AgentFriendly.llmsTxt`), not `InternalsVisibleTo`.

A new package also needs: an entry in `Partas.Nacara.Plugins.slnx`, a reference from the tests
project, a README section and package-table row, a `docs/content/guide/` page with a menu entry
in `docs/Site.fs`, and its assembly name in `apiOptions.Sources` there for the API reference.

## Doc comments

Every public member has a `<summary>`. An option's default goes in its `<remarks>` as
`Defaults to <c>…</c>.`; there is no `<defaultValue>` tag. State what a value is and what it
guarantees, in present tense.

## Publishing

- `Partas.Nacara.Plugins.Tailwind` and `.DaisyUI` belong to an older NuGet account, whose key
  is rejected. The current key can publish every other package. `build.fsx -- publish` pushes
  everything in `bin/`, so the old two fail until they move accounts.
- `Partas.Nacara.Plugins.Solid` is `IsPackable=false` until Partas.Solid 3 is on NuGet.
- The docs site deploys to GitHub Pages on every push to `master` (`.github/workflows/docs.yml`).
  `ci.yml` packs but does not publish.
- `src/Partas.Nacara.Theme` is Apache-2.0, derived from `Nacara.Theme.Default`; keep the licence
  header on its files. Everything else is MIT.
