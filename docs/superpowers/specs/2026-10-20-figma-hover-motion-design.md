# Motion from Figma, version one: hover

## Why

The Figma plugin already carries a theme's tokens and layouts from Figma to a site. Motion
is the next thing a designer draws there and has to re-create by hand in CSS. The aim is
that motion drawn in Figma reaches the live site with no hand translation, for the theme's
own components and for an author's own pieces.

The long-term scope is three kinds of motion: hover effects, ambient loops (a moving
background pattern), and responsive UI (elements reacting to the reader, layouts animating
across screen sizes). **Version one is hover only.** Everything in it is shaped so loops and
responsive motion can be added without changing what version one writes.

## Goals

- A hover drawn in Figma as an interactive component plays on the site, for theme
  components and for an author's own elements.
- The output is compiled when the site builds: CSS wherever CSS can express it, and
  JavaScript only for hovers that need it, written as literals, bundled and minified. A site
  with no JavaScript hovers ships no motion JavaScript.
- Everything exported is configurable from the site's F#.
- Nothing is lost silently. What does not translate is reported by the plugin and by the
  site build.
- Using it is easy: missing pieces are set in the plugin's panel, not through naming tricks,
  and the docs walk from a drawn hover to a moving page.

## Anti-goals

These make the work a failure even if it runs, in the user's words: something that is not
configurable; something in Figma that does not translate properly; documentation or plugin
usability that is too difficult. Also: a heavy or slow output site.

## Non-goals for version one

Ambient loops and Figma Motion timelines; scroll-triggered or scroll-linked motion; layout
changes across breakpoints; the `:::motion` Markdown directive; click, press and drag
triggers; motion tokens in the theme (see "Calls made").

## What Figma provides (researched October 2026)

Verified in Figma's Plugin API docs:

- `node.reactions` holds `{ trigger, actions }`. The plugin's manifest uses
  `"documentAccess": "dynamic-page"`, so reactions are read-only and are read, not written.
- The UI's "While hovering" is trigger type `ON_HOVER`. (`MOUSE_ENTER`/`MOUSE_LEAVE` exist
  too and are not version one.)
- A variant-to-variant interaction is an action of type `NODE` with
  `navigation: "CHANGE_TO"` and `destinationId`, the target variant's id, on a variant
  `ComponentNode` inside a `ComponentSetNode`. Figma does not state the before/after pair;
  it is derived: the reaction's node is "before", the destination is "after".
- `transition` is `SMART_ANIMATE` (the only one version one translates; `DISSOLVE`, `MOVE_IN`
  and the rest are reported as not translated), with `easing` and `duration`.
- Easing types: `LINEAR`, `EASE_IN`, `EASE_OUT`, `EASE_IN_AND_OUT`, the three `_BACK`
  variants, `CUSTOM_CUBIC_BEZIER` (`easingFunctionCubicBezier {x1,y1,x2,y2}`), the spring
  presets `GENTLE`, `QUICK`, `BOUNCY`, `SLOW`, and `CUSTOM_SPRING`
  (`easingFunctionSpring {mass, stiffness, damping}`).
- A transition's duration cannot be bound to a variable (no `VariableScope` covers it).

Checked in a real file on 2026-10-20 (Figma web, the Plugin API through `window.figma`, in
a scratch file built with this repository's plugin). The capture is
`tests/Partas.Nacara.Plugins.Tests/figma-hover.json`, the fixture the tests use:

- **Durations are seconds**, stored as 32-bit floats: `0.2` reads back as
  `0.20000000298023224` and Figma shows 200ms. The export rounds to whole milliseconds.
- **`CUSTOM_SPRING` has no initial velocity.** `setReactionsAsync` rejects
  `initialVelocity`, though Figma's type docs list it.
- **A custom spring's duration is Figma's own.** Choosing "Custom spring" in the UI set mass
  1, stiffness 100, damping 10 and a duration of 1.4727s, the time the spring takes to
  settle. A value written through the API is stored as given. The site uses its own settling
  time for springs, so a spring's stored duration is not used.
- **Spring presets store only their name.** `GENTLE`, `QUICK`, `BOUNCY` and `SLOW` come back
  as `{ type }` with no parameters, and Figma showed Gentle, Bouncy and Slow at 1200ms
  whatever duration was stored. Their parameters cannot be read, so the export reports a
  preset as approximated and the site plays it with stand-in parameters (the builder's to
  choose, written down in one place) over 1200ms. A designer who needs it exact uses a custom
  spring.
- `figma.motion.physicalSpringToNormalized({mass: 1, stiffness: 300, damping: 20})` returns
  `0.4226`, which is 1 − ζ: the spring's bounce, in the perceived model anime.js also offers.

anime.js 4.5.0 (MIT) solves the same damped-spring model as Figma's custom spring, so mass,
stiffness and damping pass straight through; anime.js clamps mass to at least 1, so a lower
mass is rescaled with stiffness and damping by the same factor.

## How it works

### In Figma

A hover is drawn the way Figma already expects: a component set with a default and a hover
variant, joined by a "While hovering" interaction using Smart Animate, with an easing and a
duration.

The plugin gets a **Motion** section in its panel:

- It lists every hover it finds in the file: the component, the two variants, the easing and
  duration, and the selector it will attach to.
- For each hover it shows what will come across and what will not, with the reason.
- **Export motion** saves `motion.json`, beside `light.tokens.json` and `dark.tokens.json`.

**Which properties come across.** The plugin compares the two variants layer by layer,
matching layers by name as Smart Animate does. Version one translates changes to: fill
colour, opacity, position (as a translate), scale, rotation, width and height, corner
radius, and drop shadow. Any other difference (text content, stroke weight, a layer that
exists in only one variant, a gradient, a blur) is listed as not translated.

**What a hover attaches to.**

- **Theme components.** The components the plugin builds already name their CSS class in
  their description. That text is prose, so the plugin also tags each component it builds,
  and the named child layers that map to a class, with shared plugin data (namespace
  `nacara`, key `selector`), the same way it already tags shadow styles with their token.
  A hover on a tagged component attaches to that selector.
- **An author's own components.** An untagged component set attaches by name: a set named
  `Card` exports as motion `card`, and the site element plays it by carrying
  `data-motion="card"`. The panel shows the name and lets the author change it, which is
  stored in the same plugin data.
- **A hover belongs to its "before" variant.** A set can have more variants than default and
  hover: the sidebar link has Default, Hover and Active, and an author's `Button` may have
  `Size=Small/Large`, each with its own hover. So the selector is scoped to the variant the
  hover starts from. On theme components the plugin tags each variant with its own selector
  (Default's excludes `[aria-current="page"]`, which is Active's). On an author's component,
  every variant property other than the one that switches to hover becomes a data attribute
  the element carries: the `Size=Small` hover of `Button` attaches to
  `[data-motion="button"][data-size="small"]`. The panel shows the full selector for each.
  The property that switches to hover is whichever one differs between the two variants; a
  hover whose two variants differ in more than one property is reported as not translated.
- **Child layers.** A change on a child layer comes across only if that layer has a selector:
  a tagged class on theme components, or, on an author's component, a child carrying
  `data-motion-part="<layer name>"`. A child change with no selector is reported, not
  dropped quietly.

### The motion document

`motion.json` is a document of this project's own; the design tokens format has nothing for
interactions. It carries a format version, so later kinds can be added beside `hovers`
without changing it. Each hover records: its name, its selector, its trigger (`hover`),
the properties that change with their before and after values, the easing (in Figma's own
terms, cubic-bezier points or spring parameters), and the duration in milliseconds.

**A value bound to a token stays a token.** The theme components the plugin builds bind
their colours, radii and spacing to the token variables, and their shadow to the tagged
`Shadow` style, in Light and Dark modes. A hover value bound that way is recorded as the
token's path (the plugin already maps variables to paths), and the site writes it as
`var(--…)`, so a hover follows the colour scheme and the site's own tokens. This applies to
an author's components too. Only an unbound value is written literally: colours as hex,
lengths in px.

The schema is the builder's to write, within those contents and the version field.

### On the site

A new package, `Partas.Nacara.Plugins.Motion`, shaped like the other plugins (an options
record, `defaults`, a setter per option, `create`/`createWith`/`register`/`registerWith`).
It depends on `Partas.Nacara.Theme.Contracts`, not the theme.

When the site builds, the package reads `motion.json` once, in F#, and compiles it. The
document is never sent to the browser.

- **CSS by default.** Each hover becomes rules on `<selector>:hover` and
  `<selector>:focus-visible`: the after values, and a `transition` listing only the changed
  properties with the duration and easing. The rules go into a style layer named `motion`,
  offered through `PluginLayers.offer`, so the theme merges them into its one stylesheet.
- **Easings.** Cubic-bezier and Figma's named curves become `cubic-bezier()`. Springs are
  solved in F# with the same damped-oscillator model and written as a `linear()` easing,
  with the duration the spring takes to settle. No easing maths runs in the browser.
- **anime.js only when the site's F# asks.** Nothing switches to anime.js automatically, a
  spring included: by default every hover is CSS and the site ships no motion JavaScript. The
  choice lives only in F#, not in the document or the panel. A hover switched to anime.js gets a generated script with its
  values as literals: `mouseenter`/`focus` play towards the after state, `mouseleave`/`blur`
  return, and a spring keeps its momentum when interrupted. The script imports only the
  anime.js modules it uses, and the site's Esbuild step bundles and minifies it into one
  fingerprinted asset added through the page assets. A site must not need npm for this: the
  package carries the anime.js modules it needs (MIT, with its licence), and writes them for
  the bundler. The CDN (`https://cdn.jsdelivr.net/npm/animejs@4.5.0/...`) is an option.
- **Reduced motion.** Under `prefers-reduced-motion: reduce`, hovers change instantly: CSS
  sets the transition duration to zero; the script checks the media query and applies the end
  state without animating. The site can change the policy (instant, keep, or off).
- **Mistakes fail the build.** An override or setting that names a motion the document does
  not have, or a document whose version the package does not know, fails the build with the
  known names in the message, as unknown layout names do now. Anything the document marks as
  not translated becomes a build warning naming the hover and the property.

### Configuration from F#

Everything exported can be changed in the site, by motion name:

- read the document: from a path given relative to the site project, like
  `DesignTokens.fromDirectory`'s (the docs site, whose project is `docs/`, passes
  `../design/tokens/motion.json`);
- override a hover's duration, easing, or individual property values;
- turn a hover off;
- choose CSS or anime.js per hover, and the default for all;
- set the reduced-motion policy;
- choose where anime.js comes from: bundled (default) or CDN.

The exact names of these setters are the builder's, following the plugins' existing shape.

## The package's other obligations

As for every new package (AGENTS.md): an entry in `Partas.Nacara.Plugins.slnx`, a reference
from the tests project, a README section and package-table row, a guide page
`docs/content/guide/motion.md` with a menu entry in `docs/Site.fs`, and the assembly in
`apiOptions.Sources`. The docs site uses it, so its own pages show it working. The guide is
numbered steps from a drawn hover to a moving page, in the style of the Figma guide, plus a
"what comes across" table. `figma.md` and the plugin's listing README gain a motion section.

## Testing

- The F# compiler from document to CSS and script is tested from fixture documents, through
  the package's public functions: each easing type, spring rescaling, each property, the
  reduced-motion output, and the build failures.
- The spring solver is checked against anime.js's own output for the same parameters.
- `figma-hover.json`, captured from a real file, has a hover on two theme components (an
  ease-out and a spring preset), and an author `Card` set with Size × State whose two hovers
  use a custom spring and a custom cubic bezier. They change a bound and an unbound fill, a
  token-bound corner radius made literal, the shadow style, a child's width and opacity, and,
  on the Large card, a child's text, which does not translate. The plugin's comparison is
  tested against it, so a Figma API change shows up as a failing test once the fixture is
  captured again.
- The plugin's comparison and translation logic is kept apart from its `figma.*` calls, so it
  can be run against fixture node data outside Figma. How to run it is the builder's call.

## Calls made (not decided by the user)

- A new package rather than adding to the theme, so the theme stays light without motion.
- Hovers also play on `:focus-visible`, for keyboard readers.
- anime.js is bundled locally by default; the CDN is opt-in.
- The motion document lives beside the tokens and has its own format with a version field.
- Child-layer changes need a selector, and are reported, not guessed, without one.
- Token-bound values stay tokens; only unbound values are literal.
- A hover is scoped to its "before" variant; an author's other variant properties become
  `data-*` attributes.
- anime.js is never chosen automatically; the choice is F#'s alone.
- **Motion tokens dropped from version one.** It was proposed to add duration and easing
  tokens to the theme. The research showed Figma cannot bind a transition's duration or
  easing to a variable, so for hovers nothing in Figma would feed those tokens. They belong
  with Figma Motion, whose motion variables can, in the loops version.

## Left to the builder

The document's schema, the F# setter names, the plugin panel's layout, the spring-settling
threshold and the number of `linear()` samples, how the plugin's logic is tested outside
Figma, and how the anime.js modules are carried in the package.
