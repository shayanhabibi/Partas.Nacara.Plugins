---
title: Hover motion from Figma
---

A hover drawn in Figma is a variant that smart-animates to another variant while
the pointer is over it. `Partas.Nacara.Plugins.Motion` reads those hovers from
the document the Figma plugin exports and writes them into the site, as CSS
transitions in the theme's stylesheet or as a script that plays them with
[anime.js](https://animejs.com).

The links in this site's navbar and sidebar are hovers from the theme's Figma
file. The two cards below are hovers of a component set of this site's own. The
small one springs, played by anime.js. The large one eases, played by CSS.
Point at them, or tab to them.

<div class="motion-demo">
<div data-motion="card" data-size="small" tabindex="0"><span data-motion-part="accent"></span><strong>Small card</strong><span>A custom spring: mass 1, stiffness 300, damping 20. anime.js plays it.</span></div>
<div data-motion="card" data-size="large" tabindex="0"><span data-motion-part="accent"></span><strong>Large card</strong><span>A custom bezier, cubic-bezier(0.4, 0, 0.2, 1), over 250ms. CSS plays it.</span></div>
</div>

```bash frame="terminal"
dotnet add package Partas.Nacara.Plugins.Motion
```

## From a drawn hover to a moving page

The Figma plugin is the one [Designing in Figma](figma.md) sets up. Its Motion
section, below the tokens and layouts, has the two buttons this takes: **Find
hovers** and **Export motion**.

::::steps
:::step title="Draw a resting variant and a hover variant"
A hover lives in a component set. Give the set a variant property such as
`State`, with a resting variant (`State=Default`) and a hover variant
(`State=Hover`). The two must differ in that one property only. Any other
property, such as `Size`, picks which elements the hover applies to.

On the hover variant, change what the hover changes: the fill, the corner
radius, the drop shadow, the opacity, the size, the rotation, or where a layer
inside sits. [What comes across](#what-comes-across) lists each one.
:::
:::step title="Connect them with While hovering and Smart animate"
Select the resting variant. In **Prototype**, add an interaction: **While
hovering**, then **Change to** the hover variant, with **Smart animate**. The
easing and duration you choose there are the hover's.
:::
:::step title="Find hovers"
Run the plugin and choose **Find hovers**. Each hover gets a card with its
name, the selector it applies to, and its easing and duration. Under
**Comes across** are the CSS changes. A change to a layer inside names the
markup it plays on, such as `Accent (data-motion-part="accent")`. A token shows
the value it falls back to, such as `nacara.color.bg-raised (#ffffff)`. Under
**Does not come across, because…** is each change that does not translate,
with the reason. An interaction the plugin cannot read at all is listed under
**Skipped**, with the reason.
:::
:::step title="Name the set"
Each component set of your own has a field, **Motion name of** and the set's
name. It defaults to the set's name in kebab case, so `Card` is `card`. Under
the field the plugin shows the markup the hover plays on, such as
`On elements with data-motion="card" data-size="small"`. The theme's own sets,
Navbar link, Sidebar link, TOC link and Icon button, have no field. Their
hovers apply to the theme's own classes.

Each hover needs a name of its own. Two sets named `Promo card` and
`Promo-card` are both `promo-card`, so the second one's hover is listed under
**Skipped**, naming the first set. Give one of them another motion name.
:::
:::step title="Export motion"
Choose **Export motion** and save `motion.json` as `design/tokens/motion.json`
in your site, beside the tokens. A later change is the same export, saved over
the file.

The site reads `motion.json` when it starts, so a site running with a watch
does not see a new export until you stop it and start it again.
:::
:::step title="Register the plugin"
Register Motion before `Theme.register`. Motion offers its CSS to the theme,
and the theme bundles what was offered when it registers.

```fsharp
let site =
    Site.create "My site"
    |> Markdown.register
    |> Esbuild.register // needed for anime.js hovers with the bundled copy
    |> Motion.register
    |> Theme.register theme
    |> Site.collection (Theme.docs theme "content")
```
:::
:::step title="Build"
Build the site. Each hover plays on `:hover` and on `:focus-visible`. The build
reports everything the document leaves out as a `motion-not-translated`
warning, so nothing is lost without a line saying so.
:::
::::

## Binding from Markdown

A hover on a set of your own applies to any element with `data-motion` set to
the set's motion name. Each of the set's other variant properties becomes an
attribute too, named and valued in kebab case. The `Card` set has a `Size`
property, so its `Size=Small` hover applies to
`[data-motion="card"][data-size="small"]`.

A layer inside the set that the hover changes is a part. It is the descendant
with `data-motion-part` set to the layer's name in kebab case. A nested layer is
a nested part, so a layer `Accent/Dot` is
`[data-motion-part="accent"] [data-motion-part="dot"]`.

The cards on this page are this Markdown:

```html
<div class="motion-demo">
<div data-motion="card" data-size="small" tabindex="0"><span data-motion-part="accent"></span><strong>Small card</strong><span>…</span></div>
<div data-motion="card" data-size="large" tabindex="0"><span data-motion-part="accent"></span><strong>Large card</strong><span>…</span></div>
</div>
```

- **Keep the block together.** Markdown ends an HTML block at a blank line, so a
  blank line inside one turns the rest into text.
- **Make it focusable.** A hover also plays on keyboard focus, but only on an
  element that can take focus. A link or a button can. Give a `div` a
  `tabindex="0"`.
- **Style it at rest.** The document holds only what the hover changes, so where
  the element starts from is your CSS. This site offers its cards' resting
  style as a layer of its own, in the values the Figma variants rest at, below.
- **Leave the transition to the hover.** A hover played by CSS sets the
  element's `transition` to the properties it changes, and that replaces any
  other `transition` the element has. To keep one of your own, write a rule of
  your own that lists both, or play the hover with anime.js, which sets no
  `transition`.

The cards' resting style:

```fsharp
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
```

A hover's name, which the F# setters below take, is the motion name and the
other variant properties in brackets: `card[size=small]`. With two other
properties, both are listed, joined by a comma, as `card[size=small,tone=muted]`.
A set with no other property has the motion name alone, such as `navbar-link`.
**Find hovers** shows each name in bold at the top of its card.

## What comes across

| In Figma | In CSS | How |
|---|---|---|
| The first solid fill | `background-color` | On a text layer, `color`. |
| Corner radius | `border-radius` | Four different corners give four lengths. |
| Drop shadows | `box-shadow` | The `Shadow` effect style is `var(--nacara-shadow, …)`. |
| Opacity | `opacity` | Rounded to 3 decimals. |
| Width and height | `width`, `height` | Not on a text layer, nor on a side auto layout sizes; see below. |
| Rotation | `rotate` | In degrees, about the layer's centre. |
| Where a layer inside sits | `translate` | The offset from where it rests; for a rotated layer, of its centre. |

A value bound to a variable comes across as its custom property, so the hover
follows the site's tokens and both colour schemes. Beside it is the value
Figma resolves the variable to, as a fallback: `nacara/color/bg-subtle` is
`var(--nacara-bg-subtle, #f6f7f9)`. Where the site defines
`--nacara-bg-subtle`, as the theme does for its own tokens, the hover uses the
site's value. Where it does not, such as a variable of a collection of your
own, the hover ends on Figma's value instead of on nothing. The fallback is the
variable's value in the mode the component is drawn in, so it does not follow
the colour scheme. anime.js falls back the same way.

An unbound colour is hex, `#rrggbb`, or `#rrggbbaa` when it is not opaque. An
unbound length is in `px`.

### Easing

| Figma | CSS and anime.js |
|---|---|
| Linear | `linear` |
| Ease in | `cubic-bezier(0.42, 0, 1, 1)` |
| Ease out | `cubic-bezier(0, 0, 0.58, 1)` |
| Ease in and out | `cubic-bezier(0.42, 0, 0.58, 1)` |
| Ease in back | `cubic-bezier(0.3, -0.05, 0.7, -0.5)` |
| Ease out back | `cubic-bezier(0.45, 1.45, 0.8, 1)` |
| Ease in and out back | `cubic-bezier(0.7, -0.4, 0.4, 1.4)` |
| Custom bezier | Its four points, rounded to 3 decimals. |
| Custom spring | Its mass, stiffness and damping. |
| Gentle, Quick, Bouncy, Slow | An approximation over 1200ms. |

Durations are rounded to whole milliseconds.

**A custom spring plays until it settles.** It has no duration of its own, so
Figma's duration does not apply to it. The plugin solves the spring the way
anime.js 4.5.0 does, and plays it for the time it takes to come to rest. CSS
gets that motion as a `linear()` easing of 32 samples. anime.js gets the same
spring. Both engines play the same curve.

**The four spring presets are approximated.** Figma does not publish the
parameters behind Gentle, Quick, Bouncy and Slow, and plays each over 1200ms.
The plugin plays each over 1200ms too, on both engines, with these stand-in
springs. CSS gets the stand-in as a `linear()` easing of 32 samples, and
anime.js plays the same samples, so a preset looks the same whichever engine
plays it:

| Preset | Mass | Stiffness | Damping |
|---|---|---|---|
| Gentle | 1 | 100 | 15 |
| Quick | 1 | 300 | 20 |
| Bouncy | 1 | 600 | 15 |
| Slow | 1 | 80 | 20 |

The build warns about each hover that uses a preset. To match Figma exactly,
choose **Custom spring** in Figma, or set the spring from F# with
`Motion.easing`, below.

### What does not, and why

The plugin reports everything below, in **Does not come across, because…** or
**Skipped**, and the build repeats each one as a warning.

- **Triggers other than While hovering.** On click, While pressing, Mouse enter
  and the rest are skipped. This version translates hovers only.
- **Transitions other than Smart animate.** Instant, Dissolve, Move in and the
  rest are skipped. Only Smart animate tweens between the two variants' layers,
  and that tween is what becomes CSS.
- **A hover variant that differs in more than one variant property.** Skipped.
  The properties that stay the same are what pick the elements, so exactly one
  must change.
- **Gradient and image fills, and any fill after the first.** CSS can transition
  one colour, not a stack of paints. Use one solid fill.
- **Strokes.** A Figma stroke can sit inside, centred or outside, and per side.
  No CSS property transitions it the same way.
- **Effects other than drop shadows,** such as blurs and inner shadows.
- **Text.** Text content does not animate. A text layer's size follows its text,
  so its width and height are not translated either: a title that widens with
  longer text would otherwise be pinned to a width. The large card's title is
  one such layer, so the build reports `card[size=large]: Title/text is not
  translated`.
- **A shadow on a text layer.** `box-shadow` draws around the box, not the
  glyphs.
- **A layer in only one variant,** and **two layers with the same name.** Layers
  are matched by name, so neither has a partner to tween to.
- **Auto layout spacing and padding, and other bound fields.** Each one that
  changes is reported as "is not animated".
- **What auto layout moves or sizes.** A layer in an auto layout frame that is
  not absolutely positioned sits where the layout puts it, and a width or height
  set to Hug or Fill is the layout's. When one of those changes, the layout did
  it, not the hover, so it is reported as "follows auto layout". A Fixed width
  or height comes across. To move a layer inside auto layout on hover, set its
  position to **Absolute**.
- **On the theme's own sets:** a changed layer the theme has no selector for, and
  anything but the colour of the `Label` and `Icon` layers. Those layers are the
  link or the button itself in the theme's HTML, so only their colour has
  somewhere to go.

### Why there is no scale

A Figma layer has no scale. Scaling a layer in Figma resizes it, so the hover
variant only holds a new width and height. That is what comes across: `width`
and `height`, never `transform: scale()`. If you want a scale on hover, write it
in your own CSS.

## Configuring from F#

`Motion.register` uses the defaults. `Motion.registerWith` takes the setters,
joined with `>>`. The hover names in these examples are this site's. Yours come
from your own `motion.json`, and a name it does not have fails the build with the
names it does have.

```fsharp
site
|> Motion.registerWith (
    Motion.document "design/tokens/motion.json"
    >> Motion.duration "navbar-link" 150
    >> Motion.hoverEngine "card[size=small]" Anime
)
|> Theme.register theme
```

| Option | Setter | Default |
|---|---|---|
| The motion document | `Motion.document` | `"design/tokens/motion.json"` |
| The engine | `Motion.engine` | `Css` |
| Reduced motion | `Motion.reducedMotion` | `Instant` |
| Where anime.js comes from | `Motion.animeSource`, `Motion.animeFromCdn` | `Bundled` |
| One hover's settings | `Motion.hoverEngine`, `Motion.duration`, `Motion.easing`, `Motion.value`, `Motion.disable` | the document's |

A setter that names a hover the document does not have fails the build, and the
message lists the hovers the document has. So does a `Motion.value` key the hover
does not change, a `Motion.duration` on a hover that plays a spring, a
`SpringPreset` that is not one of the four, and a document with two hovers of
one name. A misspelt name or key is an error, not a setting that does nothing.

### `Motion.document`

The path of the exported document. A relative path is taken from the site's
project root, the directory Nacara reads `content` from: the value of `--root`
when the site is given one, otherwise the directory of the site's project file.
It does not depend on where the build is started. This site's project is in
`docs`, so it reaches the repository's `design/tokens` one level up:

```fsharp
Motion.document "../design/tokens/motion.json"
```

When the file is missing, the build fails. The message names the full path it
looked at and says to run **Export motion** and save `motion.json` there.

The document is read once, when the site starts. A site run with a watch, such
as `dotnet fsi build.fsx -- docs --watch` here, keeps the hovers it started
with: restart it after saving a new export.

### `Motion.engine`

The engine that plays every hover. `Css` writes CSS transitions into the theme's
stylesheet, in the `nacara.motion` layer. `Anime` writes a script that plays the
hovers with anime.js.

```fsharp
Motion.engine Anime
```

anime.js reads a token's value when the hover starts, so a hover that begins
after the visitor switches colour scheme ends on the new scheme's colours. It also
starts from whatever is on screen, so leaving a hover halfway through a spring
turns back from there without a jump.

### `Motion.hoverEngine`

The engine of one hover, instead of the plugin's. This site plays the small card
with anime.js and everything else with CSS:

```fsharp
Motion.hoverEngine "card[size=small]" Anime
```

### `Motion.duration`

The duration of one hover in milliseconds, instead of Figma's. It applies only
to linear and cubic-bezier easings. A custom spring plays over its settling time
and a spring preset over 1200ms, so neither has a duration to set. Giving one a
duration fails the build, naming the hover. To set the length of a spring hover,
change its easing with `Motion.easing`: to a cubic-bezier, which then takes the
duration, or to a custom spring that settles faster or slower.

```fsharp
Motion.duration "navbar-link" 150
```

### `Motion.easing`

The easing of one hover, instead of Figma's: `Linear`, `CubicBezier`,
`Easing.Spring` or `SpringPreset`. The spring case is written `Easing.Spring`
because the `Spring` module, which solves springs, has the same name in the same
namespace and would be found first. `SpringPreset "GENTLE"`, `"QUICK"`,
`"BOUNCY"` and `"SLOW"` use the stand-ins above, over 1200ms.

```fsharp
Motion.easing "navbar-link" (CubicBezier(0.2, 0.0, 0.0, 1.0))
Motion.easing "sidebar-link" (Easing.Spring { Mass = 1.0; Stiffness = 100.0; Damping = 15.0 })
Motion.easing "card[size=large]" (SpringPreset "QUICK")
```

A custom spring here replaces a preset's stand-in, and the preset's warning goes.
A preset name other than those four, such as `SpringPreset "GENTEL"`, fails the
build, naming the hover and the four presets.

### `Motion.value`

Replaces the hovered value of one change with a CSS value. The key is the CSS
property for the element itself, or `Layer/property` for a part. A `var(--…)`
value works under both engines: anime.js resolves it under the hovered element
each time the hover starts. Prefer lengths in `px` for a part under anime.js, which
resolves a value as a computed style, where a percentage may not resolve.

```fsharp
Motion.value "card[size=small]" "background-color" "var(--nacara-primary-subtle)"
Motion.value "card[size=small]" "Accent/width" "96px"
```

[This site](#this-site) uses the first line on both cards.

### `Motion.disable`

Leaves one hover out of the output:

```fsharp
Motion.disable "sidebar-link"
```

### `Motion.reducedMotion`

What happens for a visitor whose system asks for reduced motion:

- `Instant`: the hover still changes the element, at once.
- `Keep`: the hover plays as drawn.
- `Off`: the hover does not apply at all.

```fsharp
Motion.reducedMotion Off
```

Both engines follow it.

### `Motion.animeSource` and `Motion.animeFromCdn`

Where anime.js comes from, for the hovers it plays. `Bundled`, the default, is
the copy of anime.js 4.5.0 in this package. The script imports it, and the
site's bundler resolves that import into one file, so the site needs
`Esbuild.register`. Without it, the build fails with `motion-needs-esbuild`.
The script carries anime.js's MIT licence notice, which the bundle keeps.

`Motion.animeFromCdn` loads anime.js 4.5.0 from jsDelivr as the page loads
instead, and needs no bundler. The url is `Motion.CdnUrl`,
`https://cdn.jsdelivr.net/npm/animejs@4.5.0/dist/bundles/anime.esm.min.js`. `Motion.animeSource (Cdn url)` loads it from a url
of your own:

```fsharp
Motion.animeFromCdn
Motion.animeSource (Cdn "https://example.com/anime.esm.min.js")
```

The script is written to `assets/motion.<hash>.js` only when a hover plays with
anime.js. A site with only CSS hovers gets no script. When there is one, every
page loads it, since the plugin cannot know which pages have the elements. It
binds the elements that are on the page when it loads, so an element added later
by another script does not play its hover.

## This site

This site registers the document from the repository's `design/tokens`, with
the small card on anime.js:

```fsharp
|> Esbuild.register
// …
|> Motion.registerWith (
    Motion.document "../design/tokens/motion.json"
    >> Motion.hoverEngine "card[size=small]" Anime
    >> Motion.value "card[size=small]" "background-color" "var(--nacara-primary-subtle)"
    >> Motion.value "card[size=large]" "background-color" "var(--nacara-primary-subtle)"
)
|> Theme.register theme
```

Its `motion.json` has four hovers: `navbar-link` and `sidebar-link` from the
theme's sets, and `card[size=small]` and `card[size=large]` from a Card set with
a `Size` property. The sidebar link uses the Gentle preset, so the build warns
that it is approximated.

The two `Motion.value` lines are the worked example of overriding a value. In
Figma, both card hovers end on the literal colour `#f7f7ff`, a light lavender.
That reads well in the light scheme, but stays light in the dark one, under the
dark scheme's light text. `--nacara-primary-subtle` is the same light lavender in
the light scheme and a dark one in the dark scheme, so the hover reads in both.
The better fix is in Figma: bind the hover variant's fill to the
`nacara/color/primary-subtle` variable, and the export carries the token.
