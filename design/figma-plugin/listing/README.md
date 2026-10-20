# Figma Community listing

What the plugin's Community page says, and how it is published. The plugin itself is the
folder above: `manifest.json`, `code.js`, `ui.html`.

## Name

Nacara Theme Tokens

## Tagline

Design a Nacara documentation site's theme in Figma, and export it to the site's code.

## Description

Partas.Nacara.Theme is the documentation theme for Nacara, an F# static site generator. Its
colours, spacing, radii, font sizes and shadows are design tokens, and this plugin carries
them between Figma and the site in both directions.

Start from the theme's Community file, which has the tokens, the components and a template
of each layout ready: https://www.figma.com/community/file/1694279269526963076

To your site:
1. Change the variables in the Partas.Nacara.Theme collections, Light and Dark, and the
   Shadow and Shadow (Dark) effect styles.
2. Run the plugin and choose Export from variables, then save light.tokens.json and
   dark.tokens.json into your site.
3. Read them in the site:
   Theme.defaults |> Theme.tokens (DesignTokens.fromDirectory "design/tokens")

Lengths come back in the units the theme uses, so 16px is 1rem again.

Layouts: duplicate a template on the Layouts page, rename it Layout/<name>, delete the parts it
leaves out, and Export layouts writes the Theme.addLayout lines that add it.

From your site: load the two documents DesignTokens.write produces and Import into variables.
Build components, example and layouts draws the theme's components, an example docs page and
the layout templates in any file.

Documentation: https://shayanhabibi.github.io/Partas.Nacara.Plugins/guide/theme/
Source (Apache-2.0): https://github.com/shayanhabibi/Partas.Nacara.Plugins

## Tags

design tokens, variables, documentation, theme, export, code

## Images

- Icon: `icon.png`, 128 × 128.
- Cover: 1920 × 1080. Export the Light frame on the Community file's Example page, placed on a
  1920 × 1080 frame filled with `nacara/color/bg-subtle`.

## Publishing

In the Figma desktop app:

1. **Plugins → Development → Import plugin from manifest…** and choose `manifest.json`, if it is
   not imported already.
2. Run it in a copy of the Community file. Export from variables, Build components, example and
   layouts, and Export layouts should each finish without an error in the plugin's status line.
3. **Plugins → Development → Manage plugins in development**, then **Publish** on this plugin.
   Figma gives the plugin an id: put it in `manifest.json` in place of
   `partas-nacara-theme-tokens`, and commit that.
4. Fill in the name, tagline, description, tags and images above, and submit for review.

Once the plugin is approved, link it from the Community file's description, and add its
Community link to `design/README.md` and the theme guide.
