# Figma Hover Motion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hover drawn in Figma as a variant interaction plays on a Nacara site, compiled at build time to CSS, or to a bundled anime.js script when the site's F# asks for it.

**Architecture:** The Figma plugin snapshots each component set into plain data. Pure functions in `code.js` compare the before and after variants and write `motion.json`. A new F# package, `Partas.Nacara.Plugins.Motion`, reads that document when the site is described and compiles it to a `motion` style layer offered to the theme. For hovers switched to anime.js, it also writes an entry script, bundled with a vendored anime.js by the site's Esbuild step.

**Tech Stack:** F# on net10.0, Nacara.Core 2.0.0-beta.6, System.Text.Json, Expecto. The Figma Plugin API in plain JavaScript (no bundler), with `node --test` for the plugin's pure logic. anime.js 4.5.0 (MIT), vendored.

**Spec:** `docs/superpowers/specs/2026-10-20-figma-hover-motion-design.md`

## Global Constraints

- Package `Partas.Nacara.Plugins.Motion`, version `0.1.0`, `namespace Nacara.Plugins`, MIT. It depends on `Partas.Nacara.Theme.Contracts` and `Nacara.Core` `2.0.0-beta.6`, not on the theme.
- Plugin shape as in `src/Partas.Nacara.Plugins.OgImage/OgImage.fs`: an options record, `defaults`, one setter per option, `create` / `createWith` / `register` / `registerWith`.
- Every public member has a `<summary>`. An option's default goes in `<remarks>` as `Defaults to <c>…</c>.`
- Version one translates `SMART_ANIMATE` transitions on `ON_HOVER` triggers only. Anything else is reported, never dropped silently.
- Durations are Figma seconds stored as float32. Round them to whole milliseconds. Round cubic-bezier points to 3 decimals.
- `CUSTOM_SPRING` carries `mass`, `stiffness` and `damping` only, with no initial velocity.
- Spring presets `GENTLE`, `QUICK`, `BOUNCY` and `SLOW` play over **1200ms**. Their stand-in parameters live in one place, `Spring.preset`:
  - `GENTLE` = mass 1, stiffness 100, damping 15
  - `QUICK` = 1, 300, 20
  - `BOUNCY` = 1, 600, 15
  - `SLOW` = 1, 80, 20
- Figma's named easings become these cubic-beziers:
  - `EASE_IN` (0.42, 0, 1, 1)
  - `EASE_OUT` (0, 0, 0.58, 1)
  - `EASE_IN_AND_OUT` (0.42, 0, 0.58, 1)
  - `EASE_IN_BACK` (0.3, -0.05, 0.7, -0.5)
  - `EASE_OUT_BACK` (0.45, 1.45, 0.8, 1)
  - `EASE_IN_AND_OUT_BACK` (0.7, -0.4, 0.4, 1.4)
  - `LINEAR` is `linear`.
- Spring solving and settling match anime.js 4.5.0 `Spring` exactly:
  - time step 0.02s
  - rest threshold 0.0005
  - rest window 200ms, which is 10 steps; the loop stops on the 11th consecutive resting step
  - `settlingDuration = round(solverDuration * 1000)`
- A spring's `linear()` easing has 32 evenly spaced samples over its settling time, each rounded to 3 decimals. The first is `0` and the last is `1`.
- A token path `a.b.….z` is written `var(--a-z)`: first segment, then last. So `nacara.color.bg-subtle` is `var(--nacara-bg-subtle)` and `nacara.shadow.shadow` is `var(--nacara-shadow)`. This is the plugin's own WEB code syntax (`code.js:213`).
- Unbound values are literal: colours as lowercase hex, `#rrggbb`, or `#rrggbbaa` when alpha < 1. Lengths are in `px`.
- Hovers also play on `:focus-visible`.
- **Scale is not produced.** No Figma node property holds a scale (the fixture has none), so a size change comes across as width and height. This is a deviation from the spec's property list. Tell the user at handoff.
- anime.js comes from the vendored `dist/bundles/anime.esm.js` of 4.5.0 by default. The CDN default is `https://cdn.jsdelivr.net/npm/animejs@4.5.0/dist/bundles/anime.esm.min.js`.
- Commits:
  - Conventional messages (`feat(motion): …`, `docs(motion): …`).
  - Each ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - Write the message to `$TEMP/claude-commit-msg.txt` with `printf`, then run `rtk git commit -q -F "$TEMP/claude-commit-msg.txt"`.
  - Stage files by name. Never stage `.claude/`, `.codex/`, `.idea/`, `.serena/`, `.superpowers/`, `*.DotSettings.user`, `tests/**/.SageFs/` or `mutations*.log`.
  - Do not edit any CHANGELOG by hand.
- Shell commands are prefixed with `rtk`, except `dotnet fsi build.fsx -- test|docs|pack`.

## Review Focus

1. **A missing `motion.json`, or a wrong path to it.** The build fails, naming the full resolved path and how to export the document. It does not build a site with no motion. *(Task 4: `missing document fails with the resolved path`.)*
2. **A selector that is a list** (`a, b`). `:hover` must apply to every member, not just the last. *(Task 3: `a list selector is wrapped in :is()`.)*
3. **A Figma file built before this change.** "Build components" keeps existing sets, so they would never be tagged, and the theme's hovers would quietly attach to `[data-motion="navbar-link"]`. Tagging runs over existing sets too. *(Task 7: `tagging reaches sets that already exist`.)*
4. **Text layers that resize with their text.** The Large card's Title widens from 62 to 76 when its text changes. That width must not become a `width` animation that pins the title. It is reported with the text change. *(Task 6: `a text layer's size is not translated`.)*
5. **Token values under anime.js.** anime.js cannot interpolate `var(--…)`, and the colour scheme can change between hovers. The script resolves the custom property each time the hover starts. *(Task 5: `anime script resolves tokens when the hover starts`.)*

---

## File Structure

**New package:** `src/Partas.Nacara.Plugins.Motion/`

| File | Responsibility |
|------|----------------|
| `Document.fs` | The motion document's types and `MotionDocument.parse` |
| `Spring.fs` | The damped-spring solver, settling time, `linear()` samples, presets, and mass rescaling |
| `Css.fs` | `MotionCss`: easing, values, a hover's rules, reduced-motion rules |
| `Script.fs` | `MotionScript`: the anime.js entry script for the hovers switched to it |
| `Options.fs` | The option and result types |
| `Script.fs` | `MotionScript`: the anime.js entry script |
| `Motion.fs` | Setters, `Motion.compile`, and the plugin |
| `vendor/anime.esm.js`, `vendor/LICENSE.anime.md` | anime.js 4.5.0, embedded resources |
| `Partas.Nacara.Plugins.Motion.fsproj` | The project file |

**Tests:** `tests/Partas.Nacara.Plugins.Tests/`

| File | Contents |
|------|----------|
| `MotionDocumentTests.fs`, `SpringTests.fs`, `MotionCssTests.fs`, `MotionTests.fs`, `FigmaPluginTests.fs` | The test files |
| `motion-sample.json` | A hand-written document covering every easing and property |
| `anime-spring.json` + `anime-spring.mjs` | anime.js's own spring output, and the script that writes it |

**Figma plugin:**
- `design/figma-plugin/code.js`: a new `// ---- motion ----` section, theme tagging, and two new message types.
- `design/figma-plugin/ui.html`: a Motion section.
- `design/figma-plugin/motion.test.js`: `node --test` tests against `figma-hover.json`.

**Docs:**
- `design/tokens/motion.json`: exported from the scratch file.
- `docs/content/guide/motion.md`
- `docs/Site.fs`
- `docs/docs.fsproj`
- `docs/content/guide/figma.md`
- `design/figma-plugin/listing/README.md`
- `design/README.md`
- `README.md`

---

### Task 1: The package and the motion document

**Files:**
- Create: `src/Partas.Nacara.Plugins.Motion/Partas.Nacara.Plugins.Motion.fsproj`, `Document.fs`
- Modify: `Partas.Nacara.Plugins.slnx` (add the project under `/src/`), `tests/Partas.Nacara.Plugins.Tests/Partas.Nacara.Plugins.Tests.fsproj` (ProjectReference, and `MotionDocumentTests.fs` before `Main.fs`)
- Test: `tests/Partas.Nacara.Plugins.Tests/MotionDocumentTests.fs`, `motion-sample.json`

**Interfaces:**
- Produces, in `Document.fs`:

```fsharp
type MotionValue = Token of path: string | Literal of css: string
type SpringParameters = { Mass: float; Stiffness: float; Damping: float }
type Easing =
    | Linear
    | CubicBezier of x1: float * y1: float * x2: float * y2: float
    | Spring of SpringParameters
    | SpringPreset of name: string          // "GENTLE" | "QUICK" | "BOUNCY" | "SLOW"
type Change = { Layer: string option; Part: string option; Property: string; From: MotionValue; To: MotionValue }
type NotTranslated = { Layer: string option; Property: string; Reason: string }
type Hover = { Name: string; Selector: string; Trigger: string; DurationMs: int; Easing: Easing
               Changes: Change list; NotTranslated: NotTranslated list }
type Skipped = { Component: string; Variant: string; Reason: string }
type MotionDocument = { Version: int; Hovers: Hover list; Skipped: Skipped list }
module MotionDocument =
    val SupportedVersion: int                       // 1
    val parse: json: string -> Result<MotionDocument, string>
```

**The document format** (version 1). The schema is this plan's call:

```json
{ "version": 1,
  "hovers": [ { "name": "card[size=small]",
                "selector": "[data-motion=\"card\"][data-size=\"small\"]",
                "trigger": "hover", "duration": 500,
                "easing": { "type": "CUSTOM_SPRING", "mass": 1, "stiffness": 300, "damping": 20 },
                "changes": [ { "property": "background-color", "from": { "token": "nacara.color.bg-raised" }, "to": { "value": "#f7f7ff" } },
                             { "layer": "Accent", "part": "[data-motion-part=\"accent\"]", "property": "width",
                               "from": { "value": "40px" }, "to": { "value": "80px" } } ],
                "notTranslated": [ { "layer": "Title", "property": "text", "reason": "Text content does not animate." } ] } ],
  "skipped": [ { "component": "Tab", "variant": "Selected=False", "reason": "DISSOLVE is not translated; use Smart Animate." } ] }
```

- `easing.type` is Figma's own name.
  - The six named curves and `LINEAR` decode into `CubicBezier` and `Linear`, using the Global Constraints values.
  - `CUSTOM_CUBIC_BEZIER` has `x1 y1 x2 y2`.
  - `CUSTOM_SPRING` has `mass stiffness damping`.
  - The four presets decode into `SpringPreset`.
- `"part"` is the CSS selector of a child, below the hover's selector. `"layer"` is the Figma layer name. Both are absent for the root element.

- [ ] **Step 1: Write `motion-sample.json` and the failing tests**

  `motion-sample.json` holds:
  - one hover per easing kind, with these names, which later tasks' tests use:
    - `navbar-link`: `EASE_OUT`, selector `.nacara-navbar__link:not([data-active="true"]):not([aria-current])`
    - `card[size=large]`: `CUSTOM_CUBIC_BEZIER`, with the Title/text `notTranslated` entry
    - `card[size=small]`: `CUSTOM_SPRING` 1/300/20, as in the format example
    - `sidebar-link`: `GENTLE`
    - `icon-button`: `LINEAR`
  - every property: `background-color`, `color`, `opacity`, `translate`, `rotate`, `width`, `height`, `border-radius`, `box-shadow`
  - a token value and a literal value
  - one child part
  - one `notTranslated` entry
  - one `skipped` entry

  Tests in `MotionDocumentTests.fs`, reading the file via `__SOURCE_DIRECTORY__` like `DesignTokensTests.fs`:
  - `parses every hover of the sample`: `Ok` with 5 hovers and 1 skipped entry.
  - `EASE_OUT is a cubic bezier`: equals `CubicBezier(0.0, 0.0, 0.58, 1.0)`.
  - `a custom spring keeps its parameters`: equals `Spring { Mass = 1.0; Stiffness = 300.0; Damping = 20.0 }`.
  - `a preset keeps its name`: equals `SpringPreset "GENTLE"`.
  - `a token and a literal are told apart`: `From = Token "nacara.color.bg-raised"`, `To = Literal "#f7f7ff"`.
  - `a part keeps its selector and layer`: `Part = Some "[data-motion-part=\"accent\"]"`, `Layer = Some "Accent"`.
  - `an unknown version is an error naming it`: `parse """{"version":2,"hovers":[]}"""` is `Error` containing `"version 2"` and `"1"`.
  - `an unknown easing is an error naming the hover`: `Error` containing the hover's name and the type.

- [ ] **Step 2: Run them and see them fail**

  Run `dotnet test tests/Partas.Nacara.Plugins.Tests --filter "FullyQualifiedName~MotionDocument"`, or eval in a SageFs session on the tests project.

  Expected: compile error, because `MotionDocument` is not defined.

- [ ] **Step 3: Create the project and `Document.fs`**

  - The fsproj copies OgImage's, with Description "Hovers drawn in Figma, compiled into a Nacara site's CSS, or into a bundled anime.js script."
  - It has a ProjectReference to `../Partas.Nacara.Theme.Contracts/Partas.Nacara.Theme.Contracts.fsproj`.
  - `MotionDocument.parse` uses `System.Text.Json.JsonDocument`, as `DesignTokens.fs:403` does.
  - A missing `skipped`, `notTranslated` or `layer`/`part` reads as empty or `None`.

- [ ] **Step 4: Run the tests and see them pass**

  Same command. Expected: 8 passed.

- [ ] **Step 5: Commit** `feat(motion): the motion document and its parser`, staging:
  - the fsproj
  - `Document.fs`
  - the slnx
  - the tests fsproj
  - `MotionDocumentTests.fs`
  - `motion-sample.json`

### Task 2: Springs, checked against anime.js

**Files:**
- Create: `src/Partas.Nacara.Plugins.Motion/Spring.fs`, `vendor/anime.esm.js`, `vendor/LICENSE.anime.md`
- Modify: the Motion fsproj (`Spring.fs` after `Document.fs`; `<EmbeddedResource Include="vendor/anime.esm.js" LogicalName="anime.esm.js" />`; the licence as an EmbeddedResource and as `<None Pack="true" PackagePath="\" />`)
- Test: `tests/Partas.Nacara.Plugins.Tests/SpringTests.fs`, `anime-spring.mjs`, `anime-spring.json`

**Interfaces:**
- Consumes: `SpringParameters` (Task 1).
- Produces:

```fsharp
module Spring =
    val position: spring: SpringParameters -> seconds: float -> float   // anime's 1 - solve(t)
    val solverDuration: spring: SpringParameters -> float               // seconds
    val settle: spring: SpringParameters -> int                         // ms, = round(solverDuration * 1000)
    val linear: spring: SpringParameters -> string                      // "linear(0, …, 1)", 32 samples
    val preset: name: string -> SpringParameters option
    val forAnime: spring: SpringParameters -> SpringParameters          // mass < 1 rescaled to mass 1
```

- [ ] **Step 1: Vendor anime.js 4.5.0**

  - Run `npm pack animejs@4.5.0` in a fresh scratch directory and extract it.
  - Copy `package/dist/bundles/anime.esm.js` to `vendor/anime.esm.js`, and `package/LICENSE.md` to `vendor/LICENSE.anime.md`.
  - Check: `grep -c "export { Spring, createSpring, spring }" package/dist/modules/easings/spring/index.js` prints `1`. Its header says `v4.5.0`.

- [ ] **Step 2: Write `anime-spring.mjs` and generate `anime-spring.json`**

  The script imports `spring` from `../../src/Partas.Nacara.Plugins.Motion/vendor/anime.esm.js`. For each parameter set it records:
  - the parameters
  - `s.solverDuration`
  - `s.settlingDuration`
  - `s.solve(s.solverDuration * t)` for t = 0.1 … 0.9

  The parameter sets cover underdamped, critically damped and overdamped springs, and a mass above 1:
  - 1, 100, 10
  - 1, 300, 20
  - 1, 600, 15
  - 1, 80, 20
  - 1, 100, 20 (critically damped)
  - 1, 100, 30 (overdamped)
  - 2, 200, 10

  Run `node tests/Partas.Nacara.Plugins.Tests/anime-spring.mjs > tests/Partas.Nacara.Plugins.Tests/anime-spring.json`.

- [ ] **Step 3: Write the failing tests**
  - `settling matches anime.js`: for every entry, `Spring.settle p = settlingDuration` exactly.
  - `the curve matches anime.js`: `abs (Spring.position p (solverDuration * t) - (1 - solve)) < 1e-9`.
  - `linear() has 32 samples from 0 to 1`: for 1/300/20, the result starts `"linear(0, "`, ends `", 1)"`, and has 32 comma-separated values.
  - `a bouncy spring overshoots in linear()`: for 1/600/15, some sample is > 1.
  - `presets are the stand-ins`: `Spring.preset "GENTLE" = Some { Mass = 1.0; Stiffness = 100.0; Damping = 15.0 }`, likewise the other three, and `Spring.preset "NOPE" = None`.
  - `a light spring is rescaled for anime.js`: `Spring.forAnime { Mass = 0.5; Stiffness = 100.0; Damping = 10.0 } = { Mass = 1.0; Stiffness = 200.0; Damping = 20.0 }`, and the two settle equally.

- [ ] **Step 4: Run them and see them fail.** Same command as before, filtered on `Spring`. Expected: `Spring` is not defined.

- [ ] **Step 5: Implement `Spring.fs`**

  This is a port of `Spring.compute` and `solve` from `package/dist/modules/easings/spring/index.js`, with the constants from Global Constraints, including the `w0` clamp to `[1e-11, 1000]`. Use the actual mass; only `forAnime` rescales.

- [ ] **Step 6: Run the tests and see them pass.** Expected: 6 passed.

- [ ] **Step 7: Commit** `feat(motion): springs solved as anime.js solves them`, staging:
  - `Spring.fs`
  - `vendor/*`
  - the fsproj
  - `SpringTests.fs`
  - `anime-spring.mjs`
  - `anime-spring.json`
  - the tests fsproj

### Task 3: Hovers as CSS

**Files:**
- Create: `src/Partas.Nacara.Plugins.Motion/Css.fs`
- Test: `tests/Partas.Nacara.Plugins.Tests/MotionCssTests.fs`

**Interfaces:**
- Consumes: Task 1's types, `Spring.settle`, `Spring.linear` and `Spring.preset`.
- Produces:

```fsharp
type ReducedMotion = Instant | Keep | Off
module MotionCss =
    val value: MotionValue -> string                      // Token -> "var(--a-z)", Literal -> as is
    val easing: Easing -> string                          // "linear" | "cubic-bezier(…)" | "linear(…)"
    val duration: Hover -> int                            // see below
    val hover: Hover -> string                            // the rules for one hover
    val stylesheet: policy: ReducedMotion -> hovers: Hover list -> string
```

- `duration`:
  - a cubic or linear easing uses `DurationMs`
  - `Spring p` uses `Spring.settle p`
  - `SpringPreset` uses `1200`

  A duration override (Task 4) is applied to the `Hover` before it reaches here.
- `hover h`, for selector `S` (wrapped as `:is(S)` when it contains a comma at the top level):
  - `S { transition: <prop> <d>ms <easing>, … }` lists only the root's changed properties, sorted by property name.
  - `S:hover, S:focus-visible { <prop>: <to>; … }`
  - The same pair for each `Part P`: `S P { transition… }` and `S:hover P, S:focus-visible P { … }`.
- `stylesheet`:
  - It concatenates the `hover` rules.
  - `Instant` adds `@media (prefers-reduced-motion: reduce) { S, S P, … { transition-duration: 0s; } }`.
  - `Off` wraps every `:hover`/`:focus-visible` rule in `@media (prefers-reduced-motion: no-preference) { … }`.
  - `Keep` adds nothing.

- [ ] **Step 1: Write the failing tests**
  - `a token becomes its custom property`: `value (Token "nacara.color.bg-subtle") = "var(--nacara-bg-subtle)"`, and `value (Token "nacara.shadow.shadow") = "var(--nacara-shadow)"`.
  - `named curves become cubic-bezier`: `easing (CubicBezier(0.0, 0.0, 0.58, 1.0)) = "cubic-bezier(0, 0, 0.58, 1)"`.
  - `a spring becomes linear()`: `easing (Spring p)` starts `"linear(0, "`.
  - `a preset plays over 1200ms`: `duration` of a `SpringPreset "GENTLE"` hover with `DurationMs = 300` is `1200`.
  - `a custom spring plays over its settling time`: `duration` of the 1/300/20 hover equals `Spring.settle` of it.
  - `hover writes only the changed properties`: for the sample's `card[size=small]`, the CSS contains:
    - `[data-motion="card"][data-size="small"]:hover, [data-motion="card"][data-size="small"]:focus-visible {`
    - `background-color: #f7f7ff;`
    - `transition: background-color`

    It does not contain `color:` on the root.
  - `a part gets its own rules`: it contains `:hover [data-motion-part="accent"]` and `width: 80px;`.
  - `a list selector is wrapped in :is()`: a hover with selector `"a, b"` produces `:is(a, b):hover`.
  - `Instant zeroes the transition under reduced motion`: `stylesheet Instant` contains `@media (prefers-reduced-motion: reduce)` and `transition-duration: 0s`.
  - `Off keeps hovers out of reduced motion`: every `:hover` in `stylesheet Off` comes after `@media (prefers-reduced-motion: no-preference)`.
  - `Keep adds no media query`: `stylesheet Keep` contains no `@media`.

- [ ] **Step 2: Run them and see them fail** (filter `MotionCss`).

- [ ] **Step 3: Implement `Css.fs`**

  Numbers are written with `InvariantCulture`, without trailing zeros (`0.58`, `1`).

- [ ] **Step 4: Run the tests and see them pass.** Expected: 11 passed.

- [ ] **Step 5: Commit** `feat(motion): hovers compiled to CSS`.

### Task 4: Options, overrides and the compile step

**Files:**
- Create: `src/Partas.Nacara.Plugins.Motion/Options.fs` (the types below, after `Css.fs`) and `Motion.fs` (the `Motion` module, last in the fsproj; the plugin is added in Task 5). Task 5's `Script.fs` goes between them, so it can see `AnimeSource`.
- Test: `tests/Partas.Nacara.Plugins.Tests/MotionTests.fs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:

```fsharp
type MotionEngine = Css | Anime
type AnimeSource = Bundled | Cdn of url: string
type HoverSettings = { Enabled: bool; Engine: MotionEngine option; Duration: int option
                       Easing: Easing option; Values: Map<string, string> }
type MotionOptions = { Document: string; Engine: MotionEngine; ReducedMotion: ReducedMotion
                       AnimeSource: AnimeSource; Hovers: Map<string, HoverSettings> }
type CompiledMotion = { Css: string; AnimeHovers: Hover list; Warnings: string list }
[<RequireQualifiedAccess>]
module Motion =
    val defaults: MotionOptions
    val document: path: string -> MotionOptions -> MotionOptions
    val engine: MotionEngine -> MotionOptions -> MotionOptions
    val reducedMotion: ReducedMotion -> MotionOptions -> MotionOptions
    val animeSource: AnimeSource -> MotionOptions -> MotionOptions
    val animeFromCdn: MotionOptions -> MotionOptions                    // Cdn of the Global Constraints url
    val hoverEngine: name: string -> MotionEngine -> MotionOptions -> MotionOptions
    val duration: name: string -> ms: int -> MotionOptions -> MotionOptions
    val easing: name: string -> Easing -> MotionOptions -> MotionOptions
    val value: name: string -> key: string -> css: string -> MotionOptions -> MotionOptions
    val disable: name: string -> MotionOptions -> MotionOptions
    val compile: MotionOptions -> MotionDocument -> Result<CompiledMotion, string>
    val load: MotionOptions -> Result<CompiledMotion, string>           // reads Document, then compile
```

- `defaults`:
  - `Document = "design/tokens/motion.json"`, resolved against the working directory, as `DesignTokens.fromDirectory` resolves.
  - `Engine = Css`
  - `ReducedMotion = Instant`
  - `AnimeSource = Bundled`
  - `Hovers = Map.empty`
- A `value` key is the property (`"background-color"`) for the root, or `"<Layer>/<property>"` (`"Accent/width"`) for a part. An override replaces that change's `To` with `Literal css`.
- `compile`:
  - It applies the overrides and drops disabled hovers.
  - It splits the hovers by engine (`HoverSettings.Engine`, else `options.Engine`).
  - The CSS hovers go to `MotionCss.stylesheet options.ReducedMotion`; the Anime hovers go to `AnimeHovers`.
- `Warnings` has one line per `NotTranslated`, `"<hover>: <layer>/<property> is not translated: <reason>"`, plus:
  - `"<hover>: the <PRESET> spring preset is approximated; use a custom spring to match Figma exactly."` per preset
  - one line per `Skipped`

- [ ] **Step 1: Write the failing tests** (against `motion-sample.json`)
  - `defaults compile every hover to CSS`: `Ok`, `AnimeHovers = []`, and `Css` contains every sample selector.
  - `a duration override wins`: with `Motion.duration "card[size=large]" 400`, `Css` contains `400ms`.
  - `a value override replaces the after value`: with `Motion.value "card[size=small]" "Accent/width" "120px"`, it contains `width: 120px;` and not `width: 80px;`.
  - `a disabled hover writes nothing`: with `Motion.disable "navbar-link"`, the `Css` has no `nacara-navbar__link`.
  - `a hover switched to anime.js leaves the CSS`: with `Motion.hoverEngine "card[size=small]" Anime`, `AnimeHovers` has that one hover, and `Css` lacks its selector.
  - `an unknown hover name fails naming the known ones`: with `Motion.duration "cards" 1`, the result is `Error` containing `"cards"` and `"card[size=small]"`.
  - `an unknown value key fails naming the keys`: with `Motion.value "card[size=small]" "Nope/width" "1px"`, it is `Error` containing `"Accent/width"`.
  - `not translated and presets become warnings`: `Warnings` contains `"Title/text is not translated"` and `"GENTLE spring preset is approximated"`.
  - `missing document fails with the resolved path`: `Motion.load (Motion.document "nowhere/motion.json" Motion.defaults)` is `Error` containing `Path.GetFullPath "nowhere/motion.json"` and `"Export motion"`.

- [ ] **Step 2: Run them and see them fail** (filter `Motion`).

- [ ] **Step 3: Implement the options, setters, `compile` and `load` in `Motion.fs`**

  Doc comments on every member. Defaults go in `<remarks>`.

- [ ] **Step 4: Run the tests and see them pass.** Expected: 9 passed.

- [ ] **Step 5: Commit** `feat(motion): configure every hover from F#`.

### Task 5: The anime.js script and the plugin

**Files:**
- Create: `src/Partas.Nacara.Plugins.Motion/Script.fs` (after `Options.fs`, before `Motion.fs` in the fsproj)
- Modify: `Motion.fs` (the plugin and its `create`/`createWith`/`register`/`registerWith`)
- Test: `MotionTests.fs` (more tests)

**Interfaces:**
- Consumes: `CompiledMotion`, `MotionOptions`, `Spring.forAnime`, `MotionCss.value`, `MotionCss.duration`.
- Produces:

```fsharp
module MotionScript =
    val entry: source: AnimeSource -> policy: ReducedMotion -> hovers: Hover list -> string
    val anime: unit -> string                        // the embedded anime.esm.js
[<RequireQualifiedAccess>]
module Motion =
    val LayerName: string                            // "motion"
    val create: unit -> IPlugin
    val createWith: (MotionOptions -> MotionOptions) -> IPlugin
    val register: Site -> Site
    val registerWith: (MotionOptions -> MotionOptions) -> Site -> Site
```

- `entry`:
  - `Bundled` starts with `import { … } from "./anime.esm.js";`, naming only what it uses: `createAnimatable` always, `spring` when a hover is a spring or preset, `cubicBezier` when one is a bezier.
  - `Cdn url` has no static import. It does `const anime = await import(url)`, with `url` held in a variable so esbuild leaves it alone, inside an async IIFE.
  - Each hover's selector, parts, after values, duration and easing are literals.
  - A spring is passed through `Spring.forAnime`; a preset uses `Spring.preset`.
  - On `mouseenter` and `focus` the hover plays to the after values. On `mouseleave` and `blur` it returns to the values computed when it started. Use `createAnimatable` so an interrupted spring retargets from where it is.
  - A `Token` value is resolved at enter with `getComputedStyle(element).getPropertyValue("--a-z")`, never passed as `var()`.
  - `Instant` sets duration 0 when `matchMedia("(prefers-reduced-motion: reduce)").matches`. `Off` attaches nothing then. `Keep` ignores the media query.
- `createWith configure`:
  - It runs `Motion.load`, and `failwith` on `Error`, at registration, as `Theme.layoutComponents` does.
  - It calls `PluginLayers.offer { Name = LayerName; Css = compiled.Css }` once, at registration. A site may register Motion before or after the theme.
  - In `Configure`:
    - `Registry.onPagesRouted` adds each warning as `Diagnostic.warning "motion-not-translated"`.
    - When `AnimeHovers` is non-empty, it registers the script at `assets/motion.<first 8 of BuildCache.Hash entry>.js`, plus `Registry.extra (Script(path, true))`:
      - `Bundled`: as `Bundle([ "motion.js", entry; "anime.esm.js", MotionScript.anime () ], "motion.js", path)`.
      - `Cdn`: as `WriteText(entry, path)`.
    - When `Bundled`, `Registry.onBuildComplete` reads the written script. If it still contains `"./anime.esm.js"`, it adds `Diagnostic.error "motion-needs-esbuild"` with the hint `Add |> Esbuild.register to the site.`
- `register` = `registerWith id`.

- [ ] **Step 1: Write the failing tests**
  - `bundled entry imports only what it uses`: an entry with only a bezier hover contains `cubicBezier` and `createAnimatable`, and not `spring`.
  - `cdn entry has no static import`: `entry (Cdn "https://x/anime.js") Instant hovers` contains no `import {`, and contains `await import(`.
  - `anime script resolves tokens when the hover starts`: for a hover whose `To` is `Token "nacara.color.bg-subtle"`, the entry contains `getPropertyValue("--nacara-bg-subtle")` and not `var(--nacara-bg-subtle)`.
  - `a light spring reaches anime.js rescaled`: a 0.5/100/10 hover's entry contains `stiffness: 200` and `damping: 20`.
  - `reduced motion is honoured in the script`: the `Instant` entry contains `prefers-reduced-motion: reduce`.
  - `the vendored anime.js is 4.5.0`: `MotionScript.anime ()` contains `v4.5.0`.

- [ ] **Step 2: Run them and see them fail.**

- [ ] **Step 3: Implement `Script.fs` and the plugin in `Motion.fs`.**

- [ ] **Step 4: Run the tests and see them pass.** Expected: 6 passed.

- [ ] **Step 5: Commit** `feat(motion): anime.js hovers, bundled or from a CDN`.

### Task 6: The plugin's comparison, tested against the real capture

**Files:**
- Modify: `design/figma-plugin/code.js` (a `// ---- motion ----` section before `// ---- plugin entry ----`, and `if (typeof module !== "undefined") module.exports = { … }` at the end)
- Create: `design/figma-plugin/motion.test.js`
- Create: `tests/Partas.Nacara.Plugins.Tests/FigmaPluginTests.fs` (in the tests fsproj)

**Interfaces:**
- Consumes: a **set snapshot**, the shape of each `componentSets[]` entry in `figma-hover.json`, plus optional fields the snapshot adds from shared plugin data (namespace `nacara`):
  - `selector` on the set, a variant or a layer: from key `selector`
  - `motionName` on the set: from key `motion-name`
- Produces (pure, no `figma.*`):

```js
motionName(setName)                 // "Navbar link" -> "navbar-link"
compareVariants(before, after, isTheme) // -> { changes: Change[], notTranslated: NotTranslated[] }
hoversOf(setSnapshot)               // -> { hovers: Hover[], skipped: Skipped[] }  (document shapes, Task 1)
motionDocument(setSnapshots)        // -> { version: 1, hovers, skipped }
```

**Rules:**
- **The hover's variants.** "Before" is the variant with the `ON_HOVER` reaction; "after" is the variant its `destinationId` names.
  - A transition other than `SMART_ANIMATE` is skipped with its type named.
  - Two variants that differ in more than one variant property are skipped.
- **Name and selector.**
  - A tagged set (it has `selector`) uses the before variant's `selector`, else the set's.
  - An untagged set uses `[data-motion="<motionName ?? kebab(set name)>"]` plus `[data-<kebab key>="<kebab value>"]` for every variant property other than the one that switches.
  - The name is the motion name, plus `[<key>=<value>,…]` (lowercase kebab) when there are other properties. So the names are `navbar-link`, `card[size=small]` and `card[size=large]`.
- **The root.** Its `x` and `y` are ignored, since they are the variants' places in the set.
- **What translates.**
  - `fills[0]` SOLID becomes `background-color`. On a TEXT layer, or a theme layer tagged `:self`, it becomes `color` on the root.
  - Changes also translate for `opacity`, `width`/`height` (not on TEXT), corner radius as `border-radius`, `rotation` as `rotate` (negated, in `deg`), and drop shadows as `box-shadow`.
  - A child's `x`/`y` delta becomes `translate: <dx>px <dy>px`, from `0px 0px`.
- **Values.**
  - A fill with `variable` is `{ token: variable.replaceAll("/", ".") }`.
  - Corner radius is a token when all four `*Radius` `boundVariables` name the same variable.
  - A shadow is the `effectStyle.token` when it has one. Otherwise it is literal from the `DROP_SHADOW` list: `0px 4px 12px 0px #10182810`, comma-joined.
  - No fill is `transparent`; no shadow is `none`.
- **Children.** They are matched by name.
  - On an untagged set, a child's part is `[data-motion-part="<kebab layer name>"]`.
  - On a tagged set, a child's part is its tagged `selector`. A theme child with no tag is reported as `"no selector: tag the layer or leave it unchanged"`.
- **What does not translate.** Anything else is reported:
  - `text` when `characters` differ; a TEXT layer's width and height are folded into it
  - `stroke`
  - a non-SOLID `fill`
  - non-drop-shadow `effect`
  - `layer` when a layer exists in one variant only
- **Durations.** Rounded from seconds to ms. Presets write `1200`.

- [ ] **Step 1: Write the failing tests in `motion.test.js`** (`node:test`, `node:assert/strict`, requiring `./code.js` and `../../tests/Partas.Nacara.Plugins.Tests/figma-hover.json`). The theme sets are tagged in the test as Task 7's `THEME_SELECTORS` would tag them, by importing `THEME_SELECTORS` and a pure `applyTags(snapshot, THEME_SELECTORS)`.
  - `navbar link hover`:
    - name `navbar-link`
    - selector `.nacara-navbar__link:not([data-active="true"]):not([aria-current])`
    - duration 200, easing `{ type: "EASE_OUT" }`
    - changes:
      - `background-color` from `{ value: "transparent" }` to `{ token: "nacara.color.bg-subtle" }`
      - `color` from `{ token: "nacara.color.text-muted" }` to `{ token: "nacara.color.text" }`
    - `notTranslated` empty
  - `sidebar link plays its preset over 1200ms`: easing `{ type: "GENTLE" }`, duration 1200.
  - `an untagged theme layer is reported`: without tags, the navbar hover's `notTranslated` has `layer: "Label"`.
  - `small card`:
    - name `card[size=small]`
    - selector `[data-motion="card"][data-size="small"]`
    - easing `{ type: "CUSTOM_SPRING", mass: 1, stiffness: 300, damping: 20 }`, duration 500
    - changes:
      - `background-color` to `{ value: "#f7f7ff" }`
      - `border-radius` from `{ token: "nacara.size.radius" }` to `{ value: "12px" }`
      - `box-shadow` from `{ value: "none" }` to `{ token: "nacara.shadow.shadow" }`
      - Accent `width` `40px` → `80px`
      - Accent `opacity` `1` → `0.8`
      - each with part `[data-motion-part="accent"]`
  - `a text layer's size is not translated`: the `card[size=large]` changes have no Title entry. Its `notTranslated` has exactly one Title entry, `property: "text"`.
  - `large card bezier is rounded`: easing `{ type: "CUSTOM_CUBIC_BEZIER", x1: 0.4, y1: 0, x2: 0.2, y2: 1 }`, duration 250.
  - `a dissolve is skipped`: a copy of the navbar set with `transition.type = "DISSOLVE"` gives no hovers and one `skipped` entry naming `DISSOLVE`.
  `FigmaPluginTests.fs` holds:
  - `plugin tests pass under node`: runs `node --test design/figma-plugin/motion.test.js` from the repo root (`__SOURCE_DIRECTORY__/../..`) and asserts exit code 0, printing stdout and stderr on failure.
  - `the plugin's document parses`: runs `node -e` to print `motionDocument(...)` of the fixture with tags applied, and asserts `MotionDocument.parse` is `Ok` with 4 hovers.

- [ ] **Step 2: Run them and see them fail**

  Run `node --test design/figma-plugin/motion.test.js`. Expected: `motionName is not a function`.

- [ ] **Step 3: Implement the motion section of `code.js`**

  Implement `motionName`, `compareVariants`, `hoversOf`, `motionDocument`, `applyTags`, and an empty `THEME_SELECTORS`, which Task 7 fills; put the navbar and sidebar entries in now so the tests can run. Also add the `module.exports` guard.

- [ ] **Step 4: Run the tests and see them pass**

  Run `node --test design/figma-plugin/motion.test.js`, then the two Expecto tests (filter `FigmaPlugin`). Expected: all pass.

- [ ] **Step 5: Commit** `feat(theme): the Figma plugin reads hovers off variants`, staging `code.js`, `motion.test.js`, `FigmaPluginTests.fs` and the tests fsproj.

### Task 7: The plugin in Figma: tags, snapshot, panel, export

**Files:**
- Modify: `design/figma-plugin/code.js` (`THEME_SELECTORS` in full, `tagThemeComponents`, `snapshotSet`, `exportMotion`, `renameMotion`, and the `motion-scan`, `motion-export` and `motion-rename` messages; `buildLibrary` calls `tagThemeComponents` after `buildComponents`)
- Modify: `design/figma-plugin/ui.html` (a Motion section)
- Test: `motion.test.js` (more tests)

**Interfaces:**
- Consumes: Task 6's functions.
- Produces:
  - `THEME_SELECTORS[setName] = { set, variants: { [variantName]: selector }, layers: { [layerName]: selector | ":self" } }`
  - `tagThemeComponents(page)`, async, idempotent: it sets shared plugin data `nacara/selector` on every matching set, variant and layer, existing or new
  - `snapshotSet(set)`, async: produces the fixture's shape plus tags; colours rounded to 3 places, durations as stored
  - `exportMotion()` → `{ document, sets: [{ id, name, motionName, tagged }] }`
  - `renameMotion(setId, name)`: writes `nacara/motion-name`

**`THEME_SELECTORS`** covers every set with a hover variant:

| Set | Set selector | Variant selectors | Layers |
|-----|--------------|-------------------|--------|
| Navbar link | `.nacara-navbar__link` | `State=Default` → `.nacara-navbar__link:not([data-active="true"]):not([aria-current])`<br>`State=Active` → `.nacara-navbar__link:is([data-active="true"],[aria-current])` | `Label` → `:self` |
| Icon button | `.nacara-icon-button` | — | `Icon` → `:self` (its fill colours `currentColor`) |
| Sidebar link | `.nacara-sidebar__link` | `State=Default` → `.nacara-sidebar__link:not([aria-current="page"])`<br>`State=Active` → `.nacara-sidebar__link[aria-current="page"]` | `Label` → `:self`<br>`Badge` → `.nacara-badge` |
| TOC link | `.nacara-toc__link` | Each `Level=L, State=S`: Level 2 → `:not([data-level="3"])`, Level 3 → `[data-level="3"]`; Default → `:not([data-active="true"])`, Active → `[data-active="true"]`, appended to `.nacara-toc__link` | `Label` → `:self` |

- [ ] **Step 1: Write the failing tests**
  - `tagging reaches sets that already exist`: a fake page (plain objects with `type`, `name`, `children`, and `get/setSharedPluginData` backed by a `Map`) holds a "Navbar link" set built with no tags. After `await tagThemeComponents(page)`, its `State=Default` variant's `nacara/selector` is the Default selector above, and a second run changes nothing.
  - `a renamed motion names the hover`: `hoversOf` of the Card snapshot with `motionName: "promo"` gives the names `promo[size=small]` and `promo[size=large]`, and the selector `[data-motion="promo"][data-size="small"]`.

- [ ] **Step 2: Run them and see them fail.**

- [ ] **Step 3: Implement the Figma side**
  - `snapshotSet` reads:
    - `reactions`, `variantProperties`, `fills`, `strokes`, `effects`, `opacity`, `rotation`, `cornerRadius`, `width`, `height`, `x`, `y`, `characters`
    - `boundVariables`, mapped to variable names via `figma.variables.getVariableByIdAsync`
    - `effectStyleId`, mapped to `{ name, token: style.getSharedPluginData("nacara", "token") }`
  - The **Motion** section in `ui.html` has:
    - a "Find hovers" button (`motion-scan`)
    - a list, one card per hover: name, selector, easing, duration, and "comes across" and "does not come across, because …" lists
    - a name field per untagged set, which sends `motion-rename` on change and rescans
    - an **Export motion** button that saves `motion.json`, using the existing `save` helper's download pattern
    - skipped hovers, listed with their reason

- [ ] **Step 4: Run the tests and see them pass**, then check it in Figma:

  1. In the in-app browser, open the scratch file https://www.figma.com/design/a1QwrYvkc80TRxZOu8NdPP.
  2. Paste the new `code.js` through `window.figma`. Figma's CSP blocks fetching from localhost; eval works.
  3. Run `await tagThemeComponents(figma.currentPage)`.
  4. Run `JSON.stringify((await exportMotion()).document)`.

  Expected: 4 hovers, matching the Task 6 expectations. The panel itself is checked by loading the plugin from the manifest in Figma desktop. If that isn't available, say so at handoff, as unverified.

- [ ] **Step 5: Commit** `feat(theme): a Motion section in the Figma plugin`.

### Task 8: The docs site uses it, and the docs explain it

**Files:**
- Create: `design/tokens/motion.json` (the scratch file's export from Task 7, Step 4), `docs/content/guide/motion.md`
- Modify:
  - `docs/docs.fsproj` (ProjectReference to the Motion project)
  - `docs/Site.fs`:
    - `"Partas.Nacara.Plugins.Motion"` in `apiOptions.Sources`
    - `Menu.section "Motion" [ Menu.page "guide/motion.md" |> Menu.badge "New" ]` after Theme
    - `|> Motion.registerWith (Motion.document "../design/tokens/motion.json" >> Motion.hoverEngine "card[size=small]" Anime)` before `Theme.register`
    - a `PluginLayers.offer` for the demo card's resting style
  - `docs/content/guide/figma.md` and `design/figma-plugin/listing/README.md`: a "Motion" section
  - `design/README.md`: `motion.json` beside the tokens
  - `README.md`: a section and a package-table row
  - `docs/Site.fs:74`: remove the `Menu.badge "New"` from `guide/figma.md`. The menu badges only the newest guide, as commit `da4e767` set it up.

- [ ] **Step 1: Write the guide**

  `motion.md` follows `figma.md`'s style:
  - `:::steps` from a drawn hover (component set, "While hovering", Smart Animate) to Export motion, `Motion.register`, and a moving page.
  - A "What comes across" table: each property and its CSS; what does not translate and why; the 1200ms preset approximation; no scale.
  - Live demos in raw HTML: the small and large cards (`data-motion="card" data-size="small|large"`, with an `<span data-motion-part="accent">`). The small card is the anime.js one, so the page shows both engines.
  - The F# setters, with one example each.
  - Binding from Markdown: an element carrying `data-motion` and its variant attributes.

- [ ] **Step 2: Build the docs and check them**

  Run `dotnet fsi build.fsx -- docs`. Expected:
  - The build succeeds.
  - Its warnings include `card[size=large]: Title/text is not translated` and the `GENTLE` approximation.
  - It has no `motion-needs-esbuild` error.

  Then check in `docs/output`:
  - `assets/css/nacara.*.css` contains `@layer nacara.motion` and `.nacara-navbar__link:not([data-active="true"]):not([aria-current]):hover`.
  - `assets/motion.*.js` exists, holds no `import`, and is smaller than 60KB.

- [ ] **Step 3: Check it in the browser**

  Serve with `docs --watch`, open `/guide/motion/` in the in-app browser, and check:
  - Hovering a navbar link eases in its background.
  - Tabbing to it does the same.
  - The small card springs. Moving out mid-spring returns without a jump.
  - The large card eases.
  - Switching the colour scheme while hovered keeps token colours right on the next hover.

  Then use `resize_window` with `colorScheme` to emulate reduced motion if the pane supports it. If it does not, say so, as unverified.

- [ ] **Step 4: The final gate**

  Run `dotnet fsi build.fsx -- test` (unfiltered) and `dotnet fsi build.fsx -- pack`. Expected: every test passes, and `bin/Partas.Nacara.Plugins.Motion.0.1.0.nupkg` contains `LICENSE.anime.md`.

- [ ] **Step 5: Commit** `docs(motion): a guide, and the docs site's own hovers`. Push only when the user says so; a push deploys the docs.
