// Imports the theme's design tokens documents (design/tokens/*.tokens.json) as Figma
// variables, and exports Figma variables back as the same documents. A shadow token, which no
// variable can hold, is an effect style instead.
//
// A variable's name is its token path with slashes: nacara.color.bg-subtle is
// nacara/color/bg-subtle, and its WEB code syntax is var(--nacara-bg-subtle). A colour scheme is
// a mode of one collection; on a plan limited to one mode it is a collection of its own.

const COLLECTION = "Partas.Nacara.Theme";
const REM = 16;

const schemeCollection = scheme => `${COLLECTION} (${scheme})`;

// ---- documents -> flat tokens ----------------------------------------------------------------

function flatten(node, path, out) {
  if (node && typeof node === "object" && "$value" in node) {
    out.push({ path, type: node.$type, value: node.$value });
    return out;
  }
  for (const [key, child] of Object.entries(node || {})) {
    if (!key.startsWith("$") && child && typeof child === "object") flatten(child, [...path, key], out);
  }
  return out;
}

const isAlias = value => typeof value === "string" && /^\{[^}]+\}$/.test(value);
const aliasName = value => value.slice(1, -1).split(".").join("/");

function parseHex(text) {
  let hex = text.replace("#", "");
  if (hex.length <= 4) hex = [...hex].map(c => c + c).join("");
  const channel = i => parseInt(hex.slice(i, i + 2), 16) / 255;
  return { r: channel(0), g: channel(2), b: channel(4), a: hex.length === 8 ? channel(6) : 1 };
}

const toHex = ({ r, g, b, a }) => {
  const channel = v => Math.round(v * 255).toString(16).padStart(2, "0");
  return "#" + channel(r) + channel(g) + channel(b) + (a < 1 ? channel(a) : "");
};

/** The Figma type and value a token's $value becomes, or null for an alias. */
function figmaValue(token) {
  const { type, value } = token;
  if (isAlias(value)) return null;
  if (type === "color" && typeof value === "string" && value.startsWith("#")) return ["COLOR", parseHex(value)];
  if (type === "color" && value && value.hex) return ["COLOR", { ...parseHex(value.hex), a: value.alpha ?? 1 }];
  if (type === "dimension" || type === "number" || type === "fontWeight") {
    const amount = typeof value === "number" ? value : value.value;
    const unit = typeof value === "number" ? "px" : value.unit;
    return ["FLOAT", unit === "rem" ? amount * REM : amount];
  }
  return ["STRING", Array.isArray(value) ? value.join(", ") : String(value)];
}

function scopesFor(type, path) {
  const leaf = path[path.length - 1];
  if (type === "COLOR") {
    if (path[0] === "tok" || /^(text|heading)|-text$/.test(leaf)) return ["TEXT_FILL"];
    if (/border/.test(leaf)) return ["STROKE_COLOR"];
    return ["FRAME_FILL", "SHAPE_FILL", "STROKE_COLOR"];
  }
  if (type === "FLOAT") {
    if (/^font-size/.test(leaf)) return ["FONT_SIZE"];
    if (/^font-weight/.test(leaf)) return ["FONT_WEIGHT"];
    if (/^line-height/.test(leaf)) return ["LINE_HEIGHT"];
    if (/^space-|gap$/.test(leaf)) return ["GAP"];
    if (/radius/.test(leaf)) return ["CORNER_RADIUS"];
    if (/blur/.test(leaf)) return ["EFFECT_FLOAT"];
    return ["WIDTH_HEIGHT"];
  }
  return []; // CSS strings: kept for code, not bindable to anything in Figma.
}

// ---- where each scheme lives ---------------------------------------------------------------

/** One { collection, modeId } per scheme: modes of one collection where the plan allows. */
async function schemeTargets(schemes) {
  const collections = await figma.variables.getLocalVariableCollectionsAsync();
  const named = name => collections.find(c => c.name === name);
  const separate = () =>
    schemes.map(scheme => {
      const collection = named(schemeCollection(scheme)) || figma.variables.createVariableCollection(schemeCollection(scheme));
      collection.renameMode(collection.modes[0].modeId, scheme);
      return { scheme, collection, modeId: collection.modes[0].modeId };
    });

  if (schemes.some(scheme => named(schemeCollection(scheme)))) return separate();

  const created = !named(COLLECTION);
  const joint = named(COLLECTION) || figma.variables.createVariableCollection(COLLECTION);
  if (created) joint.renameMode(joint.modes[0].modeId, schemes[0]);
  try {
    return schemes.map(scheme => {
      const mode = joint.modes.find(m => m.name === scheme);
      return { scheme, collection: joint, modeId: mode ? mode.modeId : joint.addMode(scheme) };
    });
  } catch (limited) {
    if (!created) throw new Error(`${COLLECTION} needs a mode per scheme (${schemes.join(", ")}), and this plan allows no more modes.`);
    joint.remove();
    return separate();
  }
}

// ---- shadows: effect styles, since a variable cannot hold one -------------------------------
//
// A shadow token is an effect style per scheme: "Shadow" for Light, "Shadow (Dark)" for Dark.
// The token's path and scheme are kept on the style (shared plugin data, namespace "nacara"), so renaming it in Figma loses nothing.

const shadowTitle = leaf => leaf.charAt(0).toUpperCase() + leaf.slice(1).replace(/-/g, " ");
const shadowStyleName = (leaf, scheme) => (scheme === "Light" ? shadowTitle(leaf) : `${shadowTitle(leaf)} (${scheme})`);
const pixels = length => (typeof length === "number" ? length : length.unit === "rem" ? length.value * REM : length.value);

/** The effect style holding one scheme's value of a shadow token, if there is one. */
async function shadowStyle(path, scheme) {
  const styles = await figma.getLocalEffectStylesAsync();
  return (
    styles.find(s => s.getSharedPluginData("nacara", "token") === path.join(".") && s.getSharedPluginData("nacara", "scheme") === scheme) ||
    styles.find(s => s.name === shadowStyleName(path[path.length - 1], scheme))
  );
}

async function importShadow(path, scheme, layers) {
  const style = (await shadowStyle(path, scheme)) || figma.createEffectStyle();
  style.name = shadowStyleName(path[path.length - 1], scheme);
  style.setSharedPluginData("nacara", "token", path.join("."));
  style.setSharedPluginData("nacara", "scheme", scheme);
  style.description = `var(--${path[0]}-${path[path.length - 1]}), ${scheme}`;
  style.effects = (Array.isArray(layers) ? layers : [layers]).map(layer => ({
    type: layer.inset ? "INNER_SHADOW" : "DROP_SHADOW",
    color: parseHex(typeof layer.color === "string" ? layer.color : layer.color.hex),
    offset: { x: pixels(layer.offsetX), y: pixels(layer.offsetY) },
    radius: pixels(layer.blur),
    spread: pixels(layer.spread || 0),
    visible: true,
    blendMode: "NORMAL",
  }));
}

async function exportShadows(documents) {
  for (const style of await figma.getLocalEffectStylesAsync()) {
    const token = style.getSharedPluginData("nacara", "token");
    const scheme = style.getSharedPluginData("nacara", "scheme");
    if (!token || !scheme) continue;
    const px = value => ({ value: Math.round(value * 100) / 100, unit: "px" });
    const layers = style.effects
      .filter(e => e.visible && (e.type === "DROP_SHADOW" || e.type === "INNER_SHADOW"))
      .map(e => ({
        color: toHex(e.color),
        offsetX: px(e.offset.x),
        offsetY: px(e.offset.y),
        blur: px(e.radius),
        spread: px(e.spread || 0),
        ...(e.type === "INNER_SHADOW" ? { inset: true } : {}),
      }));
    place((documents[scheme] = documents[scheme] || {}), token.split("."), { $type: "shadow", $value: layers });
  }
}

// ---- import --------------------------------------------------------------------------------

/** Writes { Light: document, Dark: document } into variables. Returns counts and problems. */
async function importTokens(documents) {
  const schemes = Object.keys(documents);
  const targets = await schemeTargets(schemes);
  const problems = [];
  let written = 0;

  // Variables by collection and name, so a re-import updates instead of duplicating.
  const all = await figma.variables.getLocalVariablesAsync();
  const byName = new Map(all.map(v => [`${v.variableCollectionId}|${v.name}`, v]));
  const seen = new Set();

  for (const pass of ["values", "aliases"]) {
    for (const { scheme, collection, modeId } of targets) {
      for (const token of flatten(documents[scheme], [], [])) {
        const name = token.path.join("/");
        const key = `${collection.id}|${name}`;
        if (token.type === "shadow") {
          // An alias to another shadow has nothing to draw; the code keeps it as var(). A variable
          // left from when the shadow was a string goes, or exporting would read it back instead.
          const leaf = token.path[token.path.length - 1];
          for (const [k, v] of byName) {
            if (k.startsWith(`${collection.id}|`) && v.name.split("/").pop() === leaf) { v.remove(); byName.delete(k); }
          }
          if (pass === "values" && !isAlias(token.value)) { await importShadow(token.path, scheme, token.value); written++; }
          continue;
        }
        const direct = figmaValue(token);
        if ((pass === "values") !== (direct !== null)) continue;

        let type, value;
        if (direct) [type, value] = direct;
        else {
          const target = byName.get(`${collection.id}|${aliasName(token.value)}`);
          if (!target) { problems.push(`${scheme} ${name}: alias to missing ${token.value}`); continue; }
          type = target.resolvedType;
          value = figma.variables.createVariableAlias(target);
        }

        let variable = byName.get(key);
        if (variable && variable.resolvedType !== type) {
          if (seen.has(key)) { problems.push(`${scheme} ${name}: ${type} here, ${variable.resolvedType} in another scheme`); continue; }
          variable.remove();
          variable = undefined;
        }
        if (!variable) {
          variable = figma.variables.createVariable(name, collection, type);
          byName.set(key, variable);
        }
        seen.add(key);
        variable.scopes = scopesFor(type, token.path);
        variable.setVariableCodeSyntax("WEB", `var(--${token.path[0]}-${token.path[token.path.length - 1]})`);
        variable.setValueForMode(modeId, value);
        written++;
      }
    }
  }

  return { schemes, collections: [...new Set(targets.map(t => t.collection.name))], written, problems };
}

// ---- export --------------------------------------------------------------------------------

function place(document, path, token) {
  let node = document;
  for (const key of path.slice(0, -1)) node = node[key] = node[key] || {};
  node[path[path.length - 1]] = token;
}

/** Reads every scheme's variables back as { Light: document, Dark: document }. */
async function exportTokens() {
  const collections = (await figma.variables.getLocalVariableCollectionsAsync())
    .filter(c => c.name === COLLECTION || c.name.startsWith(`${COLLECTION} (`));
  const variables = await figma.variables.getLocalVariablesAsync();
  const byId = new Map(variables.map(v => [v.id, v]));
  const documents = {};

  for (const collection of collections) {
    for (const mode of collection.modes) {
      const document = (documents[mode.name] = documents[mode.name] || {});
      for (const id of collection.variableIds) {
        const variable = byId.get(id);
        const path = variable.name.split("/");
        const raw = variable.valuesByMode[mode.modeId];
        if (raw === undefined) continue;
        const aliased = raw && raw.type === "VARIABLE_ALIAS" ? byId.get(raw.id) : null;
        const resolved = aliased ? aliased.resolvedType : variable.resolvedType;
        // Line heights and weights are bare numbers in the theme; every other FLOAT is a length.
        const leaf = path[path.length - 1];
        const bare = /^(line-height|font-weight)/.test(leaf);
        const type =
          resolved === "COLOR" ? "color"
          : resolved === "FLOAT" ? (bare ? (leaf.startsWith("font-weight") ? "fontWeight" : "number") : "dimension")
          : path.includes("font") ? "fontFamily"
          : "string";
        const value =
          aliased ? `{${aliased.name.split("/").join(".")}}`
          : resolved === "COLOR" ? toHex(raw)
          : resolved === "FLOAT" ? (bare ? Math.round(raw * 10000) / 10000 : { value: raw, unit: "px" })
          : raw;
        place(document, path, { $type: type, $value: value });
      }
    }
  }
  await exportShadows(documents);
  return documents;
}

// ---- library: components and an example page -----------------------------------------------
//
// Draws the theme's layout and components on a "Components" page, every paint and spacing bound
// to the variables imported above, and composes them into a docs page per scheme on an "Example"
// page. A component that already exists by name is kept, so running it again adds only what is
// missing; the example frames are redrawn each time.

const COMPONENTS_PAGE = "Components";
const EXAMPLE_PAGE = "Example";
// The theme turns ligatures off in code, and Figma cannot, so code uses a mono that has none.
// Fira Mono is Fira Code, which is in the theme's font stack, without them.
const MONOS = ["Fira Mono", "Source Code Pro", "Roboto Mono"];
const WEIGHTS = ["Regular", "Medium", "Semi Bold", "Bold"];
const CONTENT_WIDTH = 647; // 75ch of the body font
const PAGE_WIDTH = 1440;

// name, font (sans | mono), style, size, line height, letter spacing %, case, what it is
const TEXT_STYLES = [
  ["Heading/H1", "sans", "Bold", 36, 43.2, -2, "ORIGINAL", "h1, the page title"],
  ["Heading/H2", "sans", "Bold", 24, 39.6, -1, "ORIGINAL", "h2"],
  ["Heading/H3", "sans", "Bold", 19, 31.35, 0, "ORIGINAL", "h3"],
  ["Heading/Step", "sans", "Bold", 18, 21.6, 0, "ORIGINAL", ".nacara-steps__title"],
  ["Body/Default", "sans", "Regular", 16, 26.4, 0, "ORIGINAL", "Prose and callouts"],
  ["Body/Strong", "sans", "Semi Bold", 16, 26.4, 0, "ORIGINAL", "Callout title (weight 650)"],
  ["Body/Small", "sans", "Regular", 15, 24.75, 0, "ORIGINAL", "Table cells, sidebar links"],
  ["Body/Small Strong", "sans", "Semi Bold", 15, 24.75, 0, "ORIGINAL", "Table headers, the active sidebar link"],
  ["UI/Nav link", "sans", "Medium", 15, 24.75, 0, "ORIGINAL", "Navbar links"],
  ["UI/Brand", "sans", "Semi Bold", 16, 26.4, 0, "ORIGINAL", "The navbar's site title"],
  ["UI/Control", "sans", "Regular", 14, 20, 0, "ORIGINAL", "Selects in the navbar"],
  ["Caption/Default", "sans", "Regular", 14, 23.1, 0, "ORIGINAL", "TOC links, footer, edit link"],
  ["Caption/Label", "sans", "Regular", 12, 19.8, 0, "ORIGINAL", "The page nav's Previous / Next"],
  ["Caption/Overline", "sans", "Bold", 12, 19.8, 6, "UPPER", "Sidebar section and TOC titles"],
  ["Caption/Badge", "sans", "Semi Bold", 11, 18.15, 2, "UPPER", "Badge"],
  ["Caption/Step number", "sans", "Medium", 10, 16.5, 12, "UPPER", ".nacara-steps__number"],
  ["Caption/Terminal", "sans", "Regular", 12, 19.8, 2, "ORIGINAL", "A terminal frame's title"],
  ["Code/Block", "mono", "Regular", 14, 22.4, 0, "ORIGINAL", "Code blocks"],
  ["Code/Inline", "mono", "Regular", 14, 23.1, 0, "ORIGINAL", "Inline code"],
  ["Code/Title", "mono", "Regular", 13, 21.45, 0, "ORIGINAL", "A code block's file name"],
];

// The size and weight variables a text style reads, so editing a token resizes every style on it.
// The rest of a style's size is the theme's own, written in its CSS.
const TEXT_BINDINGS = {
  "Heading/H1": ["font-size-2xl", "font-weight-heading"],
  "Heading/H2": ["font-size-xl", "font-weight-heading"],
  "Heading/H3": ["font-size-lg", "font-weight-heading"],
  "Heading/Step": [null, "font-weight-heading"],
  "Body/Default": ["font-size"],
  "Body/Strong": ["font-size"],
  "UI/Brand": ["font-size"],
  "Body/Small": ["font-size-sm"],
  "Body/Small Strong": ["font-size-sm"],
  "UI/Nav link": ["font-size-sm"],
  "Caption/Default": ["font-size-xs"],
  "UI/Control": ["font-size-xs"],
  "Caption/Label": ["font-size-2xs"],
  "Caption/Overline": ["font-size-2xs"],
  "Caption/Terminal": ["font-size-2xs"],
  "Code/Block": ["font-size-code"],
};

const ICONS = {
  GitHub: ["0 0 16 16", "fill", '<path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/>'],
  Menu: ["0 0 24 24", "stroke", '<path d="M4 6h16M4 12h16M4 18h16"/>'],
  Search: ["0 0 24 24", "stroke", '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>'],
  Sun: ["0 0 24 24", "stroke", '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M6.3 17.7l-1.4 1.4M19.1 4.9l-1.4 1.4"/>'],
  Link: ["0 0 24 24", "stroke", '<path d="M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1"/><path d="M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1"/>'],
  Edit: ["0 0 24 24", "stroke", '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'],
  Copy: ["0 0 24 24", "stroke", '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/>'],
  Check: ["0 0 24 24", "stroke", '<path d="M20 6 9 17l-5-5"/>'],
  "Chevron down": ["0 0 24 24", "stroke", '<path d="m6 9 6 6 6-6"/>'],
};

/** The token variables, text styles and fonts the library draws with. */
async function libraryContext() {
  const collections = await figma.variables.getLocalVariableCollectionsAsync();
  const variables = await figma.variables.getLocalVariablesAsync();
  const shortName = v => {
    const path = v.name.split("/");
    return (path[0] === "tok" ? "tok-" : "") + path[path.length - 1];
  };
  const inCollection = c => variables.filter(v => v.variableCollectionId === c.id);
  const joint = collections.find(c => c.name === COLLECTION);
  const light = collections.find(c => c.name === schemeCollection("Light"));
  const darkCollection = collections.find(c => c.name === schemeCollection("Dark"));

  let V, dark;
  if (joint) {
    V = Object.fromEntries(inCollection(joint).map(v => [shortName(v), v]));
    const mode = joint.modes.find(m => m.name === "Dark");
    dark = mode ? { collection: joint, modeId: mode.modeId } : null;
  } else if (light) {
    V = Object.fromEntries(inCollection(light).map(v => [shortName(v), v]));
    dark = darkCollection ? { byName: new Map(inCollection(darkCollection).map(v => [v.name, v])) } : null;
  } else {
    throw new Error(`Import the tokens first: there is no ${COLLECTION} collection.`);
  }

  const available = await figma.listAvailableFontsAsync();
  const mono = MONOS.find(family => available.some(f => f.fontName.family === family && f.fontName.style === "Regular"));
  if (!mono) throw new Error(`None of ${MONOS.join(", ")} is available for code.`);
  await Promise.all([...WEIGHTS.map(style => figma.loadFontAsync({ family: "Inter", style })), figma.loadFontAsync({ family: mono, style: "Regular" })]);

  // A style that already exists is the designer's: only its bindings are applied again. Line
  // height is a percentage of the size, as the theme's unitless line heights are.
  const existing = await figma.getLocalTextStylesAsync();
  const ts = {};
  for (const [name, font, style, size, lineHeight, spacing, textCase, description] of TEXT_STYLES) {
    let s = existing.find(t => t.name === name);
    if (!s) {
      s = figma.createTextStyle();
      s.name = name;
      s.fontName = { family: font === "mono" ? mono : "Inter", style };
      s.fontSize = size;
      s.lineHeight = { unit: "PERCENT", value: Math.round((lineHeight / size) * 10000) / 100 };
      s.letterSpacing = { unit: "PERCENT", value: spacing };
      s.textCase = textCase;
      s.description = description;
    }
    const [sizeToken, weightToken] = TEXT_BINDINGS[name] || [];
    if (sizeToken && V[sizeToken]) s.setBoundVariable("fontSize", V[sizeToken]);
    if (weightToken && V[weightToken]) s.setBoundVariable("fontWeight", V[weightToken]);
    ts[name] = s;
  }

  // The shadow comes from the tokens' import; a file imported before shadows were tokens gets
  // the theme's default here.
  let shadow = await shadowStyle(["nacara", "shadow", "shadow"], "Light");
  if (!shadow)
    await importShadow(["nacara", "shadow", "shadow"], "Light", [
      { color: "#1018280f", offsetX: 0, offsetY: 1, blur: 2, spread: 0 },
      { color: "#1018280f", offsetX: 0, offsetY: 4, blur: 12, spread: 0 },
    ]);
  shadow = await shadowStyle(["nacara", "shadow", "shadow"], "Light");
  const shadowDark = dark ? await shadowStyle(["nacara", "shadow", "shadow"], "Dark") : null;

  return { V, dark, mono, ts, shadow, shadowDark, byId: new Map(variables.map(v => [v.id, v])) };
}

/** Drawing helpers over one context. */
function drawingKit(ctx) {
  const variable = name => {
    const v = ctx.V[name];
    if (!v) throw new Error(`No token variable for ${name}.`);
    return v;
  };
  const bound = name => figma.variables.setBoundVariableForPaint({ type: "SOLID", color: { r: 0, g: 0, b: 0 } }, "color", variable(name));
  const paints = (node, prop, list) => (node[prop] = list.map(([name]) => bound(name)));
  const bind = (node, fields, name) => fields.forEach(field => node.setBoundVariable(field, variable(name)));
  const radius = (node, name) => bind(node, ["topLeftRadius", "topRightRadius", "bottomLeftRadius", "bottomRightRadius"], name);
  const pad = (node, vertical, horizontal) => {
    if (vertical) bind(node, ["paddingTop", "paddingBottom"], vertical);
    if (horizontal) bind(node, ["paddingLeft", "paddingRight"], horizontal);
  };
  const sides = (node, top, right, bottom, left) => {
    Object.assign(node, { strokeTopWeight: top, strokeRightWeight: right, strokeBottomWeight: bottom, strokeLeftWeight: left });
    node.strokeAlign = "INSIDE";
  };
  const border = (node, name = "border", opacity) => {
    paints(node, "strokes", [[name, opacity]]);
    node.strokeWeight = 1;
    node.strokeAlign = "INSIDE";
  };
  const layout = (node, direction, width) => {
    node.layoutMode = direction;
    node.primaryAxisSizingMode = "AUTO";
    node.counterAxisSizingMode = "AUTO";
    node.fills = [];
    if (width) {
      if (direction === "VERTICAL") node.counterAxisSizingMode = "FIXED";
      else node.primaryAxisSizingMode = "FIXED";
      node.resize(width, Math.max(node.height, 1));
    }
    return node;
  };
  const frame = (name, direction = "HORIZONTAL", width) => {
    const f = layout(figma.createFrame(), direction, width);
    f.name = name;
    return f;
  };
  const component = (name, direction = "HORIZONTAL", width) => {
    const c = layout(figma.createComponent(), direction, width);
    c.name = name;
    return c;
  };
  const text = async (characters, style, color = "text") => {
    const t = figma.createText();
    await t.setTextStyleIdAsync(ctx.ts[style].id);
    t.characters = characters;
    paints(t, "fills", [[color]]);
    return t;
  };
  // A text that wraps at its parent's width.
  const fillText = (parent, t) => {
    parent.appendChild(t);
    t.layoutSizingHorizontal = "FILL";
    t.textAutoResize = "HEIGHT";
    return t;
  };
  const put = (parent, child, name) => {
    child.name = name;
    parent.appendChild(child);
    return child;
  };
  const append = (parent, child, horizontal, vertical) => {
    parent.appendChild(child);
    if (horizontal) child.layoutSizingHorizontal = horizontal;
    if (vertical) child.layoutSizingVertical = vertical;
    return child;
  };
  // A colour at an opacity over the node: what CSS writes as color-mix. It is a layer of its own,
  // at a layer opacity, because instances drop the opacity of a variable-bound paint.
  const tint = (node, name, color, opacity, strokes) => {
    const layer = figma.createRectangle();
    layer.name = name;
    node.appendChild(layer);
    layer.layoutPositioning = "ABSOLUTE";
    layer.x = 0;
    layer.y = 0;
    layer.resize(node.width, node.height);
    layer.constraints = { horizontal: "STRETCH", vertical: "STRETCH" };
    if (strokes) {
      layer.fills = [];
      paints(layer, "strokes", [[color]]);
      sides(layer, ...strokes);
    } else {
      paints(layer, "fills", [[color]]);
      node.insertChild(0, layer);
    }
    layer.opacity = opacity;
    return layer;
  };
  const recolour = (node, color) => {
    for (const n of node.findAll(n => n.type === "VECTOR")) {
      if (n.strokes.length) paints(n, "strokes", [[color]]);
      if (n.fills.length) paints(n, "fills", [[color]]);
    }
    return node;
  };
  // Variants in a row (or column) with room around them, the way the Components page shows them.
  const variantSet = (variants, parent, name, direction = "HORIZONTAL") => {
    const set = figma.combineAsVariants(variants, parent);
    set.name = name;
    set.layoutMode = direction;
    set.primaryAxisSizingMode = "AUTO";
    set.counterAxisSizingMode = "AUTO";
    set.itemSpacing = 16;
    set.paddingTop = set.paddingBottom = set.paddingLeft = set.paddingRight = 16;
    set.cornerRadius = 8;
    return set;
  };
  const textProperty = (owner, name, value, nodes) => {
    const key = owner.addComponentProperty(name, "TEXT", value);
    nodes.forEach(n => (n.componentPropertyReferences = Object.assign({}, n.componentPropertyReferences, { characters: key })));
    return key;
  };
  return { variable, bound, paints, bind, radius, pad, sides, border, frame, component, text, fillText, put, append, tint, recolour, variantSet, textProperty };
}

/** The key of a component property by its name (Figma suffixes it with an id). */
const propertyKey = (owner, name) => Object.keys(owner.componentPropertyDefinitions).find(k => k.split("#")[0] === name);
const variantOf = (set, name) => set.children.find(v => v.name === name);

async function pageNamed(name) {
  const page = figma.root.children.find(p => p.name === name) || Object.assign(figma.createPage(), { name });
  await page.loadAsync();
  return page;
}

/** The library's components, built where missing. */
async function buildComponents(ctx, page) {
  const k = drawingKit(ctx);
  const found = name => page.findOne(n => (n.type === "COMPONENT" || n.type === "COMPONENT_SET") && n.name === name);

  // Each group of components is a section holding a vertical board.
  const boards = {};
  const board = name => {
    if (boards[name]) return boards[name];
    let section = page.children.find(n => n.type === "SECTION" && n.name === name);
    if (!section) {
      const bottom = Math.max(0, ...page.children.map(n => n.y + n.height + 80));
      section = figma.createSection();
      section.name = name;
      page.appendChild(section);
      section.x = 0;
      section.y = bottom;
    }
    let b = section.children.find(n => n.name === "Board");
    if (!b) {
      b = k.frame("Board", "VERTICAL");
      b.itemSpacing = 40;
      section.appendChild(b);
      b.x = 48;
      b.y = 72;
    }
    return (boards[name] = { section, board: b });
  };
  const fit = () => {
    for (const { section, board: b } of Object.values(boards)) section.resizeWithoutConstraints(Math.max(b.width + 96, 600), b.height + 120);
  };
  const lib = {};
  const ensure = async (name, group, build) => {
    lib[name] = found(name) || (await build(board(group).board));
    return lib[name];
  };

  // Icons
  for (const [name, [viewBox, mode, body]] of Object.entries(ICONS)) {
    await ensure(`Icon/${name}`, "Icons", async parent => {
      const attrs = mode === "fill" ? 'fill="#000"' : 'fill="none" stroke="#000" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
      const c = figma.createComponentFromNode(figma.createNodeFromSvg(`<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="${viewBox}" ${attrs}>${body}</svg>`));
      c.name = `Icon/${name}`;
      c.fills = [];
      k.recolour(c, "text-muted");
      c.description = `The theme's ${name} icon. CSS draws it in currentColor; override the vector's paint where it is used.`;
      parent.appendChild(c);
      return c;
    });
  }
  const icon = (name, size, color) => {
    const i = lib[`Icon/${name}`].createInstance();
    i.resize(size, size);
    return color ? k.recolour(i, color) : i;
  };

  // Badge
  await ensure("Badge", "Badges & inline code", async parent => {
    const variants = [];
    for (const [kind, background, opacity, color] of [["Default", "bg-subtle", 1, "text-muted"], ["New", "tip", 0.18, "tip"], ["Beta", "warning", 0.18, "warning"], ["Deprecated", "danger", 0.18, "danger"]]) {
      const c = k.component(`Kind=${kind}`);
      c.paddingTop = c.paddingBottom = 1;
      c.paddingLeft = c.paddingRight = 6;
      c.cornerRadius = 999;
      c.clipsContent = true;
      const t = await k.text(kind, "Caption/Badge", color);
      t.name = "Label";
      c.appendChild(t);
      k.tint(c, "Background", background, opacity);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Badge");
    k.textProperty(set, "Label", "New", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = ".nacara-badge on a sidebar link or section. New is data-kind new/updated (tip), Beta is experimental/beta (warning), Deprecated is deprecated (danger); the background is the kind colour at 18%.";
    return set;
  });

  // Inline code
  await ensure("Inline code", "Badges & inline code", async parent => {
    const c = k.component("Inline code");
    c.paddingTop = c.paddingBottom = 1.75;
    c.paddingLeft = c.paddingRight = 5.25;
    k.radius(c, "radius-sm");
    k.paints(c, "fills", [["code-inline-bg"]]);
    k.border(c, "code-inline-border");
    const t = await k.text("Theme.register", "Code/Inline");
    t.name = "Code";
    c.appendChild(t);
    k.textProperty(c, "Code", "Theme.register", [t]);
    c.description = "Inline code in prose: code-inline-bg, code-inline-border, radius-sm, mono at 0.875em.";
    parent.appendChild(c);
    return c;
  });

  // Navbar link
  await ensure("Navbar link", "Navigation", async parent => {
    const variants = [];
    for (const [state, background, color] of [["Default", null, "text-muted"], ["Hover", "bg-subtle", "text"], ["Active", "primary-subtle", "primary"]]) {
      const c = k.component(`State=${state}`);
      k.pad(c, "space-2", "space-3");
      k.radius(c, "radius-sm");
      if (background) k.paints(c, "fills", [[background]]);
      const t = await k.text("Guide", "UI/Nav link", color);
      t.name = "Label";
      c.appendChild(t);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Navbar link");
    k.textProperty(set, "Label", "Guide", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = ".nacara-navbar__link. Active is the current section (data-active) or aria-current.";
    return set;
  });

  // Icon button
  await ensure("Icon button", "Navigation", async parent => {
    const variants = [];
    for (const [state, background, color] of [["Default", null, "text-muted"], ["Hover", "bg-subtle", "text"]]) {
      const c = k.component(`State=${state}`);
      c.primaryAxisSizingMode = c.counterAxisSizingMode = "FIXED";
      c.resize(36, 36);
      c.primaryAxisAlignItems = c.counterAxisAlignItems = "CENTER";
      k.bind(c, ["width", "height"], "control-height");
      k.radius(c, "control-radius");
      if (background) k.paints(c, "fills", [[background]]);
      const i = icon("GitHub", 18, color === "text-muted" ? null : color);
      i.name = "Icon";
      c.appendChild(i);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Icon button");
    const key = set.addComponentProperty("Icon", "INSTANCE_SWAP", lib["Icon/GitHub"].id);
    set.children.forEach(v => (v.findOne(n => n.type === "INSTANCE").componentPropertyReferences = { mainComponent: key }));
    set.description = ".nacara-icon-button: a control-height square with control-radius. The GitHub link (NavbarIcon) and the mobile menu toggle.";
    return set;
  });

  // Select
  await ensure("Select", "Navigation", async parent => {
    const c = k.component("Select");
    c.counterAxisSizingMode = "FIXED";
    c.resize(82, 36);
    c.primaryAxisSizingMode = "AUTO";
    c.counterAxisAlignItems = "CENTER";
    k.bind(c, ["height"], "control-height");
    c.itemSpacing = 6;
    k.pad(c, null, "space-2");
    k.radius(c, "control-radius");
    k.paints(c, "fills", [["bg-subtle"]]);
    k.border(c);
    const t = await k.text("System", "UI/Control", "text-muted");
    t.name = "Value";
    c.appendChild(t);
    const chevron = icon("Chevron down", 14);
    chevron.name = "Chevron";
    c.appendChild(chevron);
    k.textProperty(c, "Value", "System", [t]);
    c.description = "select.nacara-theme-select (Light, Dark, System) and the version switcher: bg-subtle, border, control-radius, control-height.";
    parent.appendChild(c);
    return c;
  });

  // Section title
  await ensure("Section title", "Navigation", async parent => {
    const c = k.component("Section title");
    c.counterAxisAlignItems = "CENTER";
    k.bind(c, ["itemSpacing"], "space-2");
    const t = await k.text("Directives", "Caption/Overline", "text-muted");
    t.name = "Title";
    c.appendChild(t);
    const badge = variantOf(lib.Badge, "Kind=New").createInstance();
    badge.name = "Badge";
    c.appendChild(badge);
    k.textProperty(c, "Title", "Directives", [t]);
    badge.componentPropertyReferences = { visible: c.addComponentProperty("Badge", "BOOLEAN", false) };
    c.description = ".nacara-sidebar__title and .nacara-toc__title: 0.75rem, 700, uppercase, .06em, text-muted.";
    parent.appendChild(c);
    return c;
  });

  // Sidebar link
  await ensure("Sidebar link", "Navigation", async parent => {
    const variants = [];
    for (const [state, background, color, style] of [["Default", null, "text-muted", "Body/Small"], ["Hover", "bg-subtle", "text", "Body/Small"], ["Active", "primary-subtle", "primary", "Body/Small Strong"]]) {
      const c = k.component(`State=${state}`, "HORIZONTAL", 272);
      c.counterAxisAlignItems = "CENTER";
      c.primaryAxisAlignItems = "SPACE_BETWEEN";
      k.pad(c, "space-2", "space-3");
      k.radius(c, "radius-sm");
      if (background) k.paints(c, "fills", [[background]]);
      const t = await k.text("Writing directives", style, color);
      t.name = "Label";
      c.appendChild(t);
      const badge = variantOf(lib.Badge, "Kind=New").createInstance();
      badge.name = "Badge";
      c.appendChild(badge);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Sidebar link", "VERTICAL");
    k.textProperty(set, "Label", "Writing directives", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    const key = set.addComponentProperty("Badge", "BOOLEAN", false);
    set.children.forEach(v => (v.findOne(n => n.type === "INSTANCE").componentPropertyReferences = { visible: key }));
    set.description = ".nacara-sidebar__link: sidebar-width, padding space-2 × space-3, radius-sm. Active is aria-current=page: a primary-subtle pill in primary at 600. A badge sits at the end.";
    return set;
  });

  // Sidebar group
  await ensure("Sidebar group", "Navigation", async parent => {
    const variants = [];
    for (const open of [false, true]) {
      const c = k.component(`Open=${open}`, "HORIZONTAL", 272);
      c.counterAxisAlignItems = "CENTER";
      c.primaryAxisAlignItems = "SPACE_BETWEEN";
      k.pad(c, "space-2", "space-3");
      k.radius(c, "radius-sm");
      const t = await k.text("Components", "Body/Small", "text-muted");
      t.name = "Label";
      c.appendChild(t);
      const chevron = icon("Chevron down", 19);
      chevron.name = "Chevron";
      chevron.opacity = 0.55;
      if (!open) chevron.rotation = 90;
      c.appendChild(chevron);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Sidebar group", "VERTICAL");
    k.textProperty(set, "Label", "Components", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = "summary.nacara-sidebar__group-title of a collapsible group. The chevron turns 90° when open; the group's links sit behind a 1px border guide.";
    return set;
  });

  // TOC link
  await ensure("TOC link", "Navigation", async parent => {
    const variants = [];
    for (const level of ["2", "3"]) {
      for (const [state, color] of [["Default", "text-muted"], ["Active", "primary"]]) {
        const c = k.component(`Level=${level}, State=${state}`, "HORIZONTAL", 240);
        c.paddingTop = c.paddingBottom = 2;
        c.paddingRight = 12;
        c.paddingLeft = level === "2" ? 12 : 24;
        if (state === "Active") {
          k.paints(c, "strokes", [["primary"]]);
          k.sides(c, 0, 0, 0, 2);
        }
        const t = await k.text(level === "2" ? "Design tokens" : "Replacing a layer", "Caption/Default", color);
        t.name = "Label";
        c.appendChild(t);
        variants.push(c);
      }
    }
    const set = k.variantSet(variants, parent, "TOC link", "VERTICAL");
    k.textProperty(set, "Label", "Design tokens", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = ".nacara-toc__link. Active (scroll-spy's data-active) is primary with a 2px primary bar on the TOC's rail. Level 3 indents by space-3.";
    return set;
  });

  // Callout
  await ensure("Callout", "Content blocks", async parent => {
    const body = "Do not drop tokens unless you are replacing every custom property it defines.";
    const variants = [];
    for (const [kind, color] of [["Note", "note"], ["Tip", "tip"], ["Warning", "warning"], ["Danger", "danger"]]) {
      const c = k.component(`Kind=${kind}`, "HORIZONTAL", CONTENT_WIDTH);
      c.clipsContent = true;
      k.radius(c, "radius");
      k.paints(c, "fills", [["bg"]]);
      k.paints(c, "strokes", [["border"]]);
      k.sides(c, 1, 1, 1, 0);
      const bar = figma.createRectangle();
      bar.name = "Accent";
      bar.resize(3, 10);
      k.paints(bar, "fills", [[color]]);
      k.append(c, bar, null, "FILL");
      const content = k.append(c, k.frame("Content", "VERTICAL"), "FILL");
      k.pad(content, "space-3", "space-4");
      k.bind(content, ["itemSpacing"], "space-1");
      const title = k.fillText(content, await k.text(kind, "Body/Strong", color));
      title.name = "Title";
      const paragraph = k.fillText(content, await k.text(body, "Body/Default"));
      paragraph.name = "Body";
      k.tint(c, "Tint", color, 0.06);
      k.radius(k.tint(c, "Outline", color, 0.35, [1, 1, 1, 0]), "radius");
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Callout", "VERTICAL");
    k.textProperty(set, "Title", "Note", set.children.map(v => v.findOne(n => n.name === "Title")));
    k.textProperty(set, "Body", body, set.children.map(v => v.findOne(n => n.name === "Body")));
    const shown = set.addComponentProperty("Show title", "BOOLEAN", true);
    set.children.forEach(v => {
      const t = v.findOne(n => n.name === "Title");
      t.componentPropertyReferences = Object.assign({}, t.componentPropertyReferences, { visible: shown });
    });
    set.description = "aside.nacara-callout[data-kind][data-title]: the kind colour at 6% over bg, a border of the kind at 35% over border, and a 3px left border in the kind colour. caution draws as Warning. The title is data-title, in the kind colour at 650.";
    return set;
  });

  // Blockquote
  await ensure("Blockquote", "Content blocks", async parent => {
    const c = k.component("Blockquote", "VERTICAL", CONTENT_WIDTH);
    k.pad(c, "space-2", "space-4");
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 0, 0, 0, 3);
    const t = k.fillText(c, await k.text("A quotation, set in the muted text colour.", "Body/Default", "text-muted"));
    t.name = "Text";
    k.textProperty(c, "Text", t.characters, [t]);
    c.description = "blockquote in prose: a 3px left border, text-muted.";
    parent.appendChild(c);
    return c;
  });

  // Details
  await ensure("Details", "Content blocks", async parent => {
    const summary = "Why are the layers ordered this way?";
    const variants = [];
    for (const open of [false, true]) {
      const c = k.component(`Open=${open}`, "VERTICAL", CONTENT_WIDTH);
      c.clipsContent = true;
      k.radius(c, "radius");
      k.paints(c, "fills", [["bg-subtle"]]);
      k.border(c);
      const row = k.append(c, k.frame("Summary"), "FILL");
      row.counterAxisAlignItems = "CENTER";
      k.pad(row, "space-2", "space-4");
      k.bind(row, ["itemSpacing"], "space-2");
      const caret = icon("Chevron down", 12, "text");
      caret.opacity = 0.6;
      if (!open) caret.rotation = 90;
      row.appendChild(caret);
      const t = await k.text(summary, "Body/Default");
      t.name = "Summary text";
      row.appendChild(t);
      if (open) {
        k.paints(row, "strokes", [["border"]]);
        k.sides(row, 0, 0, 1, 0);
        const inner = k.append(c, k.frame("Body", "VERTICAL"), "FILL");
        k.pad(inner, "space-3", "space-4");
        k.fillText(inner, await k.text("Order decides ties rather than specificity, so a later layer wins against an earlier one.", "Body/Default")).name = "Body text";
      }
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Details", "VERTICAL");
    k.textProperty(set, "Summary", summary, set.children.map(v => v.findOne(n => n.name === "Summary text")));
    set.description = "details.nacara-details: bg-subtle, border, radius. The caret turns from closed to open.";
    return set;
  });

  // Step
  await ensure("Step", "Content blocks", async parent => {
    const c = k.component("Step", "VERTICAL", CONTENT_WIDTH);
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 0, 0, 0, 2);
    k.bind(c, ["paddingLeft"], "space-4");
    c.itemSpacing = 6;
    const number = await k.text("Step 1", "Caption/Step number", "primary");
    number.name = "Number";
    c.appendChild(number);
    const title = k.fillText(c, await k.text("Install the theme", "Heading/Step", "heading"));
    title.name = "Title";
    const body = k.fillText(c, await k.text("Add the package and register it on your site.", "Body/Default"));
    body.name = "Body";
    for (const t of [number, title, body]) k.textProperty(c, t.name, t.characters, [t]);
    c.description = "li.nacara-steps__step in ol.nacara-steps, space-6 apart: a 2px left border, the number in primary, the title at 1.125rem.";
    parent.appendChild(c);
    return c;
  });

  // Tab and Tabs
  await ensure("Tab", "Content blocks", async parent => {
    const variants = [];
    for (const selected of [false, true]) {
      const c = k.component(`Selected=${selected}`);
      k.pad(c, "space-2", "space-3");
      k.radius(c, "radius-sm");
      if (selected) {
        k.paints(c, "fills", [["bg"]]);
        await c.setEffectStyleIdAsync(ctx.shadow.id);
      }
      const t = await k.text("F#", selected ? "Body/Small Strong" : "Body/Small", selected ? "text" : "text-muted");
      t.name = "Label";
      c.appendChild(t);
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Tab");
    k.textProperty(set, "Label", "F#", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = "button.nacara-tabs__tab. Selected is bg, text, 600 and the theme shadow: a segmented control.";
    return set;
  });
  await ensure("Tabs", "Content blocks", async parent => {
    const c = k.component("Tabs", "VERTICAL", CONTENT_WIDTH);
    c.clipsContent = true;
    k.radius(c, "radius");
    k.border(c);
    const list = k.append(c, k.frame("Tab list"), "FILL");
    k.paints(list, "fills", [["bg-subtle"]]);
    k.bind(list, ["paddingTop", "paddingBottom", "paddingLeft", "paddingRight", "itemSpacing"], "space-1");
    k.paints(list, "strokes", [["border"]]);
    k.sides(list, 0, 0, 1, 0);
    const label = propertyKey(lib.Tab, "Label");
    list.appendChild(variantOf(lib.Tab, "Selected=true").createInstance());
    const second = variantOf(lib.Tab, "Selected=false").createInstance();
    list.appendChild(second);
    second.setProperties({ [label]: "JavaScript" });
    const panel = k.append(c, k.frame("Panel", "VERTICAL"), "FILL");
    k.pad(panel, "space-4", "space-4");
    k.fillText(panel, await k.text("The selected tab's panel. Tabs with the same data-sync stay in step across the page.", "Body/Default")).name = "Panel text";
    c.description = "<nacara-tabs>: border and radius; the tab list is bg-subtle with space-1 padding, the panel space-4.";
    parent.appendChild(c);
    return c;
  });

  // Code block. One drawn with a font other than the code font is redrawn.
  const codeFont = node => node.findAll(n => n.type === "TEXT" && n.name === "Code").every(t => t.getRangeAllFontNames(0, t.characters.length).every(f => f.family === ctx.mono));
  const staleCode = found("Code block");
  if (staleCode && !codeFont(staleCode)) staleCode.remove();
  await ensure("Code block", "Code, tables & page furniture", async parent => {
    const variants = [];
    for (const kind of ["Code", "Terminal"]) {
      const c = k.component(`Frame=${kind}`, "VERTICAL", CONTENT_WIDTH);
      c.clipsContent = true;
      k.radius(c, "radius");
      k.paints(c, "fills", [["bg-subtle"]]);
      k.border(c);
      const bar = k.append(c, k.frame("Title bar"), "FILL");
      bar.counterAxisAlignItems = "CENTER";
      k.pad(bar, "space-2", "space-4");
      k.paints(bar, "strokes", [["border"]]);
      k.sides(bar, 0, 0, 1, 0);
      if (kind === "Terminal") {
        bar.primaryAxisAlignItems = "CENTER";
        const dots = k.frame("Dots");
        dots.itemSpacing = 5.6;
        for (const color of ["danger", "warning", "tip"]) {
          const dot = figma.createEllipse();
          dot.resize(9.6, 9.6);
          k.paints(dot, "fills", [[color]]);
          dot.opacity = 0.75;
          dots.appendChild(dot);
        }
        bar.appendChild(dots);
        dots.layoutPositioning = "ABSOLUTE";
        dots.constraints = { horizontal: "MIN", vertical: "CENTER" };
        k.put(bar, await k.text("Terminal", "Caption/Terminal", "text-muted"), "Title");
        dots.x = 16;
        dots.y = (bar.height - 9.6) / 2;
      } else {
        k.put(bar, await k.text("Site.fs", "Code/Title", "text-muted"), "Title");
      }
      const body = k.append(c, k.frame("Body", "VERTICAL"), "FILL");
      k.pad(body, "space-4", "space-4");
      if (kind === "Terminal") k.paints(body, "fills", [["bg"]]);
      const code = k.fillText(body, await codeText(k, kind === "Code" ? SAMPLE_FSHARP : SAMPLE_TERMINAL));
      code.name = "Code";
      const copy = k.frame("Copy button");
      copy.primaryAxisSizingMode = copy.counterAxisSizingMode = "FIXED";
      copy.resize(32, 32);
      copy.primaryAxisAlignItems = copy.counterAxisAlignItems = "CENTER";
      k.radius(copy, "radius-sm");
      k.paints(copy, "fills", [["bg"]]);
      k.border(copy);
      copy.appendChild(icon("Copy", 18));
      c.appendChild(copy);
      copy.layoutPositioning = "ABSOLUTE";
      copy.constraints = { horizontal: "MAX", vertical: "MIN" };
      copy.x = c.width - 40;
      copy.y = bar.height + 8;
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Code block", "VERTICAL");
    const title = set.addComponentProperty("Title", "BOOLEAN", true);
    const copy = set.addComponentProperty("Copy button", "BOOLEAN", true);
    for (const v of set.children) {
      v.findOne(n => n.name === "Title bar").componentPropertyReferences = { visible: title };
      v.findOne(n => n.name === "Copy button").componentPropertyReferences = { visible: copy };
    }
    set.description = "figure.nacara-code. Code: bg-subtle, border, radius, with the file name in mono. Terminal: a centred sans title, three dots, a bg body. Syntax colours are bound to tok/color/*; the copy button shows on hover on the site.";
    return set;
  });

  // Table
  await ensure("Table cell", "Code, tables & page furniture", async parent => {
    const variants = [];
    for (const header of [true, false]) {
      const c = k.component(`Header=${header}`, "HORIZONTAL", 200);
      k.pad(c, "space-2", "space-3");
      k.paints(c, "strokes", [["border"]]);
      k.sides(c, 0, 1, 1, 0);
      if (header) k.paints(c, "fills", [["bg-subtle"]]);
      const t = k.fillText(c, await k.text("Cell", header ? "Body/Small Strong" : "Body/Small"));
      t.name = "Text";
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Table cell");
    k.textProperty(set, "Text", "Cell", set.children.map(v => v.findOne(n => n.type === "TEXT")));
    set.description = "th and td: padding space-2 × space-3, a 1px border on the right and bottom (the table draws the top and left), headers bg-subtle at 650.";
    return set;
  });
  await ensure("Table", "Code, tables & page furniture", async parent => {
    const c = k.component("Table", "VERTICAL", CONTENT_WIDTH);
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 1, 0, 0, 1);
    const key = propertyKey(lib["Table cell"], "Text");
    const rows = [
      ["Function", "Reaches"],
      ["Theme.lightTokens", "the --nacara-* properties, light scheme"],
      ["Theme.darkTokens", "the same, dark scheme"],
      ["Theme.lightSyntax", "the --tok-* highlighting colours, light scheme"],
    ];
    rows.forEach((cells, r) => {
      const row = k.append(c, k.frame("Row"), "FILL");
      cells.forEach((value, i) => {
        const cell = variantOf(lib["Table cell"], `Header=${r === 0}`).createInstance();
        row.appendChild(cell);
        cell.layoutSizingHorizontal = i === 0 ? "FIXED" : "FILL";
        cell.layoutSizingVertical = "FILL";
        cell.setProperties({ [key]: value });
      });
    });
    c.description = "A prose table at 0.9375rem with collapsed 1px borders, made of Table cell instances.";
    parent.appendChild(c);
    return c;
  });

  // Page nav
  await ensure("Page nav link", "Code, tables & page furniture", async parent => {
    const variants = [];
    for (const direction of ["Previous", "Next"]) {
      const c = k.component(`Direction=${direction}`, "VERTICAL", 315);
      if (direction === "Next") c.counterAxisAlignItems = "MAX";
      k.pad(c, "space-3", "space-4");
      k.radius(c, "radius");
      k.border(c);
      k.put(c, await k.text(direction, "Caption/Label", "text-muted"), "Label");
      k.put(c, await k.text(direction === "Previous" ? "Writing directives" : "Plugin style layers", "Body/Default", "primary"), "Title");
      variants.push(c);
    }
    const set = k.variantSet(variants, parent, "Page nav link");
    k.textProperty(set, "Title", "Writing directives", set.children.map(v => v.findOne(n => n.name === "Title")));
    set.description = "a.nacara-page-nav__link: padding space-3 × space-4, border, radius; the border turns primary on hover. Next aligns right.";
    return set;
  });
  await ensure("Page nav", "Code, tables & page furniture", async parent => {
    const c = k.component("Page nav", "HORIZONTAL", CONTENT_WIDTH);
    k.bind(c, ["itemSpacing"], "space-4");
    k.bind(c, ["paddingTop"], "space-6");
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 1, 0, 0, 0);
    for (const v of lib["Page nav link"].children) k.append(c, v.createInstance(), "FILL");
    c.children[1].setProperties({ [propertyKey(lib["Page nav link"], "Title")]: "Plugin style layers" });
    c.description = "nav.nacara-page-nav: a top border with space-6 above the links, space-12 below the content.";
    parent.appendChild(c);
    return c;
  });

  // Edit link
  await ensure("Edit link", "Code, tables & page furniture", async parent => {
    const c = k.component("Edit link");
    c.counterAxisAlignItems = "CENTER";
    k.bind(c, ["itemSpacing"], "space-1");
    c.appendChild(icon("Edit", 14));
    k.put(c, await k.text("Edit this page", "Caption/Default", "text-muted"), "Label");
    c.description = "a.nacara-edit-link: 0.875rem, text-muted, primary on hover. Shown when Theme.editUrl is set.";
    parent.appendChild(c);
    return c;
  });

  // Footer
  await ensure("Footer", "Code, tables & page furniture", async parent => {
    const c = k.component("Footer", "VERTICAL", PAGE_WIDTH);
    c.counterAxisAlignItems = "CENTER";
    k.pad(c, "space-8", "space-4");
    k.paints(c, "fills", [["bg"]]);
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 1, 0, 0, 0);
    const t = await k.text("Built with Nacara", "Caption/Default", "text-muted");
    t.name = "Text";
    c.appendChild(t);
    k.textProperty(c, "Text", t.characters, [t]);
    c.description = "footer.nacara-footer (Theme.footer): bg, a top border, text-muted, centred.";
    parent.appendChild(c);
    return c;
  });

  // ---- the layout: navbar, sidebar, table of contents ----

  await ensure("Navbar", "Layout", async parent => {
    const c = k.component("Navbar", "HORIZONTAL", PAGE_WIDTH);
    c.counterAxisSizingMode = "FIXED";
    c.resize(PAGE_WIDTH, 56);
    k.bind(c, ["height"], "navbar-height");
    c.counterAxisAlignItems = "CENTER";
    k.pad(c, null, "space-4");
    k.bind(c, ["itemSpacing"], "space-4");
    k.paints(c, "strokes", [["border"]]);
    k.sides(c, 0, 0, 1, 0);
    k.tint(c, "Backdrop", "bg", 0.85).effects = [{ type: "BACKGROUND_BLUR", radius: 8, visible: true }];
    const brand = await k.text("Partas.Nacara.Plugins", "UI/Brand");
    brand.name = "Brand";
    c.appendChild(brand);
    const links = k.frame("Links");
    k.bind(links, ["itemSpacing"], "space-1");
    c.appendChild(links);
    const label = propertyKey(lib["Navbar link"], "Label");
    links.appendChild(variantOf(lib["Navbar link"], "State=Active").createInstance());
    const reference = variantOf(lib["Navbar link"], "State=Default").createInstance();
    links.appendChild(reference);
    reference.setProperties({ [label]: "Reference" });
    const end = k.frame("End");
    end.counterAxisAlignItems = "CENTER";
    end.primaryAxisAlignItems = "MAX";
    k.bind(end, ["itemSpacing"], "space-1");
    k.append(c, end, "FILL");
    const version = lib.Select.createInstance();
    end.appendChild(version);
    version.setProperties({ [propertyKey(lib.Select, "Value")]: "1.0" });
    end.appendChild(variantOf(lib["Icon button"], "State=Default").createInstance());
    end.appendChild(lib.Select.createInstance());
    c.description = "header.nacara-navbar: navbar-height, bg at navbar-opacity with an 8px backdrop blur, a bottom border. Brand, then the navbar sections, then the end items and the theme select.";
    parent.appendChild(c);
    return c;
  });

  await ensure("Sidebar", "Layout", async parent => {
    const c = k.component("Sidebar", "VERTICAL", 272);
    k.bind(c, ["width"], "sidebar-width");
    c.itemSpacing = 1;
    const linkSet = lib["Sidebar link"];
    const label = propertyKey(linkSet, "Label");
    const badge = propertyKey(linkSet, "Badge");
    const link = (text, state = "Default", withBadge = false) => {
      const i = variantOf(linkSet, `State=${state}`).createInstance();
      i.setProperties({ [label]: text, [badge]: withBadge });
      return i;
    };
    k.append(c, link("Getting started"), "FILL");
    const sections = [
      ["Directives", true, [["Writing directives"]]],
      ["OgImage", true, [["Link preview images"]]],
      ["Theme", true, [["Cascade layers", "Active"], ["Plugin style layers", "Default", true]]],
    ];
    for (const [title, isNew, links] of sections) {
      const section = k.append(c, k.frame("Section", "VERTICAL"), "FILL");
      k.bind(section, ["itemSpacing"], "space-2");
      k.bind(section, ["paddingBottom"], "space-6");
      const heading = lib["Section title"].createInstance();
      section.appendChild(heading);
      heading.setProperties({ [propertyKey(lib["Section title"], "Title")]: title, [propertyKey(lib["Section title"], "Badge")]: isNew });
      const list = k.append(section, k.frame("List", "VERTICAL"), "FILL");
      list.itemSpacing = 1;
      for (const [text, state, withBadge] of links) k.append(list, link(text, state, withBadge), "FILL");
    }
    c.description = "nav.nacara-sidebar: sidebar-width, sticky under the navbar. Sections are space-6 apart, each a title over its links.";
    parent.appendChild(c);
    return c;
  });

  await ensure("TOC", "Layout", async parent => {
    const c = k.component("TOC", "VERTICAL", 240);
    k.bind(c, ["width"], "toc-width");
    k.bind(c, ["itemSpacing"], "space-2");
    const title = lib["Section title"].createInstance();
    c.appendChild(title);
    title.setProperties({ [propertyKey(lib["Section title"], "Title")]: "On this page" });
    const list = k.append(c, k.frame("List", "VERTICAL"), "FILL");
    k.bind(list, ["itemSpacing"], "space-1");
    k.paints(list, "strokes", [["border"]]);
    k.sides(list, 0, 0, 0, 1);
    list.strokeAlign = "OUTSIDE";
    const label = propertyKey(lib["TOC link"], "Label");
    for (const [text, level, state] of [["Design tokens", 2, "Active"], ["Tokens in Figma", 3, "Default"], ["Style layers", 2, "Default"], ["Replacing a layer", 3, "Default"], ["Dropping a layer", 3, "Default"], ["Licensing", 2, "Default"]]) {
      const i = variantOf(lib["TOC link"], `Level=${level}, State=${state}`).createInstance();
      k.append(list, i, "FILL");
      i.setProperties({ [label]: text });
    }
    c.description = "nav.nacara-toc: toc-width, a 1px rail; the active heading is primary with a 2px bar. Hidden below 1200px.";
    parent.appendChild(c);
    return c;
  });

  fit();
  return lib;
}

const SAMPLE_FSHARP = [
  [["// Configure the theme", "tok-comment"]],
  [["let", "tok-keyword"], [" theme ", "text"], ["=", "tok-operator"]],
  [["    Theme", "tok-type"], [".", "tok-punctuation"], ["defaults", "tok-property"]],
  [["    ", "text"], ["|>", "tok-operator"], [" Theme", "tok-type"], [".", "tok-punctuation"], ["navbar", "tok-function"], [" [ ", "tok-punctuation"], ["NavbarSection", "tok-constructor"], ["(", "tok-punctuation"], ['"Guide"', "tok-string"], [", ", "tok-punctuation"], ['"guide"', "tok-string"], [") ]", "tok-punctuation"]],
  [["    ", "text"], ["|>", "tok-operator"], [" Theme", "tok-type"], [".", "tok-punctuation"], ["editUrl", "tok-function"], [" ", "text"], ['"https://github.com/you/repo/edit/main/docs"', "tok-string"]],
  [["    ", "text"], ["|>", "tok-operator"], [" Theme", "tok-type"], [".", "tok-punctuation"], ["brandIcon", "tok-function"], [" (", "tok-punctuation"], ["BrandIcon", "tok-constructor"], [".", "tok-punctuation"], ["Image", "tok-constructor"], [" ", "text"], ['"logo.svg"', "tok-string"], [")", "tok-punctuation"]],
];
const SAMPLE_TERMINAL = [
  [["$ ", "text-muted"], ["dotnet fsi build.fsx -- docs --watch", "text"]],
  [["Serving docs/output on http://localhost:8080", "text-muted"]],
];

/** Lines of [text, colour token] as one code text, each run in its token's colour. */
async function codeText(k, lines) {
  const t = await k.text(lines.map(line => line.map(run => run[0]).join("")).join("\n"), "Code/Block");
  let at = 0;
  for (const line of lines) {
    for (const [run, token] of line) {
      t.setRangeFills(at, at + run.length, [k.bound(token)]);
      at += run.length;
    }
    at += 1;
  }
  return t;
}

/** Prose and blocks stacked as the theme's CSS spaces them, in a column of the given width. */
function contentKit(k, lib, width) {
  const prose = async parts => {
    if (parts.every(p => typeof p === "string")) {
      const t = await k.text(parts.join(""), "Body/Default");
      t.resize(width, t.height);
      t.textAutoResize = "HEIGHT";
      return t;
    }
    const line = k.frame("Paragraph", "HORIZONTAL", width);
    line.layoutWrap = "WRAP";
    line.counterAxisAlignItems = "CENTER";
    // A word per text, so the paragraph wraps between words as prose does. A text's width leaves
    // out a trailing space, so the space between words is the gap: Inter's at 16px.
    line.itemSpacing = 4.5;
    for (const part of parts) {
      if (typeof part === "string") {
        for (const word of part.trim().split(/\s+/)) line.appendChild(await k.text(word, "Body/Default"));
      } else {
        const code = lib["Inline code"].createInstance();
        line.appendChild(code);
        code.setProperties({ [propertyKey(lib["Inline code"], "Code")]: part.code });
      }
    }
    return line;
  };
  // Blocks with their CSS margins, as [node, top, bottom]; adjacent margins collapse to the larger.
  const stack = (content, blocks) => {
    let previous = 0;
    blocks.forEach(([node, top, bottom], i) => {
      const gap = i === 0 ? 0 : Math.max(previous, top);
      if (gap) {
        const spacer = k.frame("Spacer");
        spacer.resize(1, gap);
        spacer.primaryAxisSizingMode = spacer.counterAxisSizingMode = "FIXED";
        content.appendChild(spacer);
      }
      content.appendChild(node);
      if (node.type !== "TEXT" || node.textAutoResize !== "WIDTH_AND_HEIGHT") node.layoutSizingHorizontal = node.name === "Edit link" ? "HUG" : "FILL";
      previous = bottom;
    });
  };
  const instance = (set, variant) => (variant ? variantOf(set, variant) : set).createInstance();
  return { prose, stack, instance };
}

/** A docs page composed from the library, in light; and the same page in dark. */
async function buildExample(ctx, lib, page) {
  const k = drawingKit(ctx);
  for (const old of page.children.filter(n => n.name.startsWith("Docs page"))) old.remove();

  const root = k.frame("Docs page — Light", "VERTICAL", PAGE_WIDTH);
  root.clipsContent = true;
  k.paints(root, "fills", [["bg"]]);
  page.appendChild(root);
  root.x = 0;
  root.y = 0;

  k.append(root, lib.Navbar.createInstance(), "FILL");
  const row = k.append(root, k.frame("Layout"), "FILL");
  row.primaryAxisAlignItems = "CENTER";
  row.counterAxisAlignItems = "MIN";
  k.pad(row, "space-8", "space-4");
  k.bind(row, ["itemSpacing"], "layout-gap");
  row.appendChild(lib.Sidebar.createInstance());

  const content = k.frame("Content", "VERTICAL", CONTENT_WIDTH);
  row.appendChild(content);
  const { prose, stack, instance } = contentKit(k, lib, CONTENT_WIDTH);
  const blocks = [];
  const block = (node, top, bottom) => blocks.push([node, top, bottom]);

  block(await k.text("Cascade layers", "Heading/H1", "heading"), 0, 16);
  block(await prose([{ code: "Partas.Nacara.Theme" }, " is the documentation theme this site is built with: layout, navbar, design tokens and the web components behind them."]), 0, 16);
  block(instance(lib["Code block"], "Frame=Code"), 0, 16);
  block(await prose([{ code: "Theme.register" }, " installs it; ", { code: "Theme.docs" }, " builds the collection that renders your Markdown."]), 0, 16);
  block(await k.text("Design tokens", "Heading/H2", "heading"), 48, 12);
  block(await prose(["The tokens layer is the one layer you do not write CSS for. It is a record, and every field in it is a custom property the rest of the theme reads."]), 0, 16);
  block(lib.Table.createInstance(), 0, 24);
  block(await k.text("Tokens in Figma", "Heading/H3", "heading"), 32, 8);
  block(await prose(["The repository's design/figma-plugin imports the tokens as Figma variables and exports them back."]), 0, 16);
  const tip = instance(lib.Callout, "Kind=Tip");
  tip.setProperties({ [propertyKey(lib.Callout, "Title")]: "Tip", [propertyKey(lib.Callout, "Body")]: "Lengths come back in the unit each token already uses, so 16px reads back as 1rem." });
  block(tip, 0, 16);
  block(lib.Tabs.createInstance(), 0, 16);
  block(await k.text("Style layers", "Heading/H2", "heading"), 48, 12);
  const warning = instance(lib.Callout, "Kind=Warning");
  warning.setProperties({ [propertyKey(lib.Callout, "Show title")]: false });
  block(warning, 0, 16);
  block(lib["Edit link"].createInstance(), 32, 0);
  block(lib["Page nav"].createInstance(), 48, 0);
  stack(content, blocks);

  row.appendChild(lib.TOC.createInstance());
  k.append(root, lib.Footer.createInstance(), "FILL");

  const dark = root.clone();
  dark.name = "Docs page — Dark";
  dark.x = PAGE_WIDTH + 160;
  if (ctx.dark && ctx.dark.modeId) dark.setExplicitVariableModeForCollection(ctx.dark.collection, ctx.dark.modeId);
  else if (ctx.dark) rebind(ctx, dark, ctx.dark.byName);
  else dark.remove();
  // An effect style has no modes, so the dark shadow is a style of its own either way.
  if (ctx.dark && ctx.shadowDark) {
    for (const node of dark.findAll(n => "effectStyleId" in n && n.effectStyleId === ctx.shadow.id)) await node.setEffectStyleIdAsync(ctx.shadowDark.id);
  }
  return { light: root, dark: ctx.dark ? dark : null };
}

/** Points every colour bound under a node at the same-named variable of another collection. */
function rebind(ctx, root, byName) {
  const swap = paint => {
    const alias = paint.boundVariables && paint.boundVariables.color;
    const from = alias && ctx.byId.get(alias.id);
    const to = from && byName.get(from.name);
    return to ? figma.variables.setBoundVariableForPaint(paint, "color", to) : paint;
  };
  const repaint = (node, prop) => {
    const before = node[prop];
    if (!Array.isArray(before) || !before.some(p => p.boundVariables && p.boundVariables.color)) return;
    node[prop] = before.map(swap);
    node[prop] = node[prop].map((p, i) => Object.assign({}, p, { opacity: before[i].opacity }));
  };
  for (const node of [root, ...root.findAll()]) {
    if (node.type === "TEXT" && node.fills === figma.mixed) {
      for (const segment of node.getStyledTextSegments(["fills"])) node.setRangeFills(segment.start, segment.end, segment.fills.map(swap));
      continue;
    }
    if ("fills" in node) repaint(node, "fills");
    if ("strokes" in node) repaint(node, "strokes");
  }
}

// ---- layouts -------------------------------------------------------------------------------

const LAYOUTS_PAGE = "Layouts";
const LAYOUT_PREFIX = "Layout/";
// What each of the theme's layouts keeps of the chrome, as DocPage has it; docs is
// DocPage.create, the ordinary page.
const LAYOUTS = {
  docs: { menu: true, toc: true, pageNav: true, title: true, editLink: true, styled: true },
  splash: { menu: false, toc: false, pageNav: false, title: false, editLink: false, styled: true },
  bare: { menu: false, toc: false, pageNav: false, title: false, editLink: false, styled: false },
};
// The DocPage field each part of a layout frame stands for, and the layer that shows it is there:
// an instance of the component of that name, or a frame of that name.
const LAYOUT_PARTS = [
  ["menu", "ShowMenu", "Sidebar"],
  ["toc", "ShowToc", "TOC"],
  ["pageNav", "ShowPageNav", "Page nav"],
  ["title", "ShowTitle", "Page title"],
  ["editLink", "ShowEditLink", "Edit link"],
];

/** One layout's template, as the theme lays out a page that names it. */
async function buildLayout(ctx, lib, name, spec) {
  const k = drawingKit(ctx);
  const root = k.frame(LAYOUT_PREFIX + name, "VERTICAL", PAGE_WIDTH);
  root.clipsContent = true;
  root.counterAxisAlignItems = "CENTER";
  k.paints(root, "fills", [["bg"]]);
  k.append(root, lib.Navbar.createInstance(), "FILL");

  const row = k.frame("Layout");
  root.appendChild(row);
  row.counterAxisAlignItems = "MIN";
  if (!spec.styled) {
    // .nacara-layout--bare: no padding, no measure; the page places its own content.
    row.layoutSizingHorizontal = "FILL";
    const canvas = k.append(row, k.frame("Canvas", "VERTICAL"), "FILL");
    canvas.resize(canvas.width, 560);
    canvas.primaryAxisSizingMode = "FIXED";
    canvas.primaryAxisAlignItems = canvas.counterAxisAlignItems = "CENTER";
    k.paints(canvas, "fills", [["bg-subtle"]]);
    canvas.appendChild(await k.text("The page lays this out itself", "Caption/Default", "text-muted"));
  } else {
    k.pad(row, "space-8", "space-4");
    k.bind(row, ["itemSpacing"], "layout-gap");
    const wide = !spec.menu;
    if (wide) {
      // .nacara-layout--splash: one column, as wide as --nacara-splash-width.
      row.primaryAxisSizingMode = "FIXED";
      row.resize(ctx.V["splash-width"] ? 1088 : PAGE_WIDTH, row.height);
      if (ctx.V["splash-width"]) k.bind(row, ["width"], "splash-width");
    } else {
      row.layoutSizingHorizontal = "FILL";
      row.primaryAxisAlignItems = "CENTER";
      row.appendChild(lib.Sidebar.createInstance());
    }
    const width = wide ? row.width - 2 * 16 : CONTENT_WIDTH;
    const content = k.frame("Content", "VERTICAL", width);
    row.appendChild(content);
    if (wide) content.layoutSizingHorizontal = "FILL";
    const { prose, stack } = contentKit(k, lib, width);
    const blocks = [];
    const block = (node, top, bottom) => blocks.push([node, top, bottom]);

    if (spec.title) {
      const title = await k.text("Page title", "Heading/H1", "heading");
      title.name = "Page title";
      block(title, 0, 16);
    }
    if (wide) {
      // A landing page writes its own heading, and a section per thing it points at.
      block(await k.text("Your project", "Heading/H1", "heading"), 0, 16);
      block(await prose(["One sentence on what it is, and a link to where a reader starts."]), 0, 16);
      for (const heading of ["The first thing it does", "The second thing it does"]) {
        block(await k.text(heading, "Heading/H2", "heading"), 48, 12);
        block(await prose(["A paragraph on it, as long as it needs to be and no longer."]), 0, 16);
        block(contentKit(k, lib, width).instance(lib["Code block"], "Frame=Code"), 0, 16);
        block(await prose(["→ Read more"]), 0, 16);
      }
    } else {
      block(await prose(["The page's Markdown, in the theme's prose styles."]), 0, 16);
      block(await k.text("A section", "Heading/H2", "heading"), 48, 12);
      block(await prose(["Each heading of the page is an entry in the table of contents."]), 0, 16);
    }
    if (spec.editLink) block(lib["Edit link"].createInstance(), 32, 0);
    if (spec.pageNav) block(lib["Page nav"].createInstance(), 48, 0);
    stack(content, blocks);
    if (spec.toc && spec.menu) row.appendChild(lib.TOC.createInstance());
  }
  k.append(root, lib.Footer.createInstance(), "FILL");
  return root;
}

/** The theme's layouts, each drawn where its template is missing; a template already there is the designer's. */
async function buildLayouts(ctx, lib, page) {
  let x = 0;
  for (const frame of page.children) x = Math.max(x, frame.x + frame.width + 160);
  const drawn = [];
  for (const [name, spec] of Object.entries(LAYOUTS)) {
    if (page.children.some(n => n.name === LAYOUT_PREFIX + name)) continue;
    const frame = await buildLayout(ctx, lib, name, spec);
    page.appendChild(frame);
    frame.x = x;
    frame.y = 0;
    x += PAGE_WIDTH + 160;
    drawn.push(frame);
  }
  return drawn;
}

/** What a layout frame keeps of the chrome, read from the parts it holds. */
function layoutOf(frame) {
  const has = layer => !!frame.findOne(n => n.name === layer && (n.type === "INSTANCE" || layer === "Page title"));
  const spec = { styled: !frame.findOne(n => n.name === "Canvas") };
  for (const [key, , layer] of LAYOUT_PARTS) spec[key] = has(layer);
  // The theme shows a table of contents only beside a menu.
  if (!spec.menu) spec.toc = false;
  return spec;
}

/** Each layout on the Layouts page that is not the theme's own, as the F# that adds it. */
async function exportLayouts() {
  const page = figma.root.children.find(p => p.name === LAYOUTS_PAGE);
  if (!page) return { source: "", layouts: [] };
  await page.loadAsync();
  const fields = [...LAYOUT_PARTS.map(([key, field]) => [key, field]), ["styled", "Styled"]];
  const same = (a, b) => fields.every(([key]) => a[key] === b[key]);
  const lines = [];
  const layouts = [];
  for (const frame of page.children.filter(n => n.type === "FRAME" && n.name.startsWith(LAYOUT_PREFIX))) {
    const name = frame.name.slice(LAYOUT_PREFIX.length).trim();
    const spec = layoutOf(frame);
    if (!name || (LAYOUTS[name] && same(spec, LAYOUTS[name]))) continue;
    // Written from whichever of the theme's layouts it is nearest, so a splash with a title is
    // DocPage.splash and one change rather than five.
    const changesFrom = base => fields.filter(([key]) => spec[key] !== LAYOUTS[base][key]).map(([key, field]) => `${field} = ${spec[key]}`);
    const base = Object.keys(LAYOUTS).reduce((best, b) => (changesFrom(b).length < changesFrom(best).length ? b : best), "docs");
    const changes = changesFrom(base);
    const record = `fun page -> { page with ${changes.join("; ")} }`;
    const shape =
      base === "docs" ? (changes.length ? `(${record})` : "id") : changes.length ? `(DocPage.${base} >> ${record})` : `DocPage.${base}`;
    lines.push(`|> Theme.addLayout "${name}" ${shape}`);
    layouts.push(name);
  }
  return { source: lines.join("\n"), layouts };
}

/** Builds the Components page, the Example page and the Layouts page. */
async function buildLibrary() {
  const ctx = await libraryContext();
  const components = await pageNamed(COMPONENTS_PAGE);
  await figma.setCurrentPageAsync(components);
  const lib = await buildComponents(ctx, components);
  await tagThemeComponents(components);
  const example = await pageNamed(EXAMPLE_PAGE);
  await figma.setCurrentPageAsync(example);
  const { light, dark } = await buildExample(ctx, lib, example);
  const layoutsPage = await pageNamed(LAYOUTS_PAGE);
  const layouts = await buildLayouts(ctx, lib, layoutsPage);
  figma.viewport.scrollAndZoomIntoView([light, dark].filter(Boolean));
  return { components: Object.keys(lib).length, mono: ctx.mono, schemes: dark ? "Light and Dark" : "Light", layouts: layouts.map(f => f.name) };
}

// ---- motion --------------------------------------------------------------------------------
//
// A hover is a variant whose ON_HOVER reaction changes to another variant with Smart Animate.
// These functions compare the two variants of a set snapshot (plain data, no figma.*) and write
// the motion document that Partas.Nacara.Plugins.Motion reads. A snapshot may carry tags from
// shared plugin data (namespace "nacara"): "selector" on the set, a variant or a layer, and
// "motionName" on the set. Anything that does not translate is reported, never dropped.

/** The theme's component sets, with the CSS selectors their hovers apply to. */
const THEME_SELECTORS = {
  "Navbar link": {
    set: ".nacara-navbar__link",
    variants: {
      "State=Default": '.nacara-navbar__link:not([data-active="true"]):not([aria-current])',
      "State=Active": '.nacara-navbar__link:is([data-active="true"],[aria-current])',
    },
    layers: { Label: ":self" },
  },
  // The icon is an instance whose vectors CSS draws in currentColor: their fill is the button's colour.
  "Icon button": {
    set: ".nacara-icon-button",
    variants: {},
    layers: { Icon: ":self" },
  },
  "Sidebar link": {
    set: ".nacara-sidebar__link",
    variants: {
      "State=Default": '.nacara-sidebar__link:not([aria-current="page"])',
      "State=Active": '.nacara-sidebar__link[aria-current="page"]',
    },
    layers: { Label: ":self", Badge: ".nacara-badge" },
  },
  "TOC link": {
    set: ".nacara-toc__link",
    variants: {
      "Level=2, State=Default": '.nacara-toc__link:not([data-level="3"]):not([data-active="true"])',
      "Level=2, State=Active": '.nacara-toc__link:not([data-level="3"])[data-active="true"]',
      "Level=3, State=Default": '.nacara-toc__link[data-level="3"]:not([data-active="true"])',
      "Level=3, State=Active": '.nacara-toc__link[data-level="3"][data-active="true"]',
    },
    layers: { Label: ":self" },
  },
};

const SPRING_PRESETS = ["GENTLE", "QUICK", "BOUNCY", "SLOW"];
const PRESET_DURATION = 1200;
const UNTAGGED_LAYER = "no selector: tag the layer or leave it unchanged";
const SELF_ONLY = "a :self layer only carries its colour";

const kebab = text => String(text).trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
const round = (x, places) => Math.round(x * 10 ** places) / 10 ** places;
const px = x => `${round(x, 2)}px`;
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const tokenOf = variable => ({ token: variable.split("/").join(".") });

/** The motion name of a set: its name, kebab-cased. */
function motionName(setName) {
  return kebab(setName);
}

/** A copy of a set snapshot tagged as tagThemeComponents tags the set in Figma; other sets come back as they are. */
function applyTags(snapshot, selectors) {
  const entry = selectors[snapshot.name];
  if (!entry) return snapshot;
  // A variant's own layers only, as tagThemeComponents: the Badge instance holds a Label of its own.
  const tagLayer = layer => (entry.layers[layer.name] ? Object.assign({}, layer, { selector: entry.layers[layer.name] }) : layer);
  return Object.assign({}, snapshot, {
    selector: entry.set,
    variants: snapshot.variants.map(variant => {
      const tagged = Object.assign({}, variant, { layer: Object.assign({}, variant.layer) });
      if (entry.variants[variant.name]) tagged.selector = entry.variants[variant.name];
      if (variant.layer.children) tagged.layer.children = variant.layer.children.map(tagLayer);
      return tagged;
    }),
  });
}

const visiblePaints = paints => (paints || []).filter(p => p.visible !== false);
const visibleEffects = effects => (effects || []).filter(e => e.visible !== false);

function paintValue(paint) {
  if (!paint) return { value: "transparent" };
  if (paint.variable) return tokenOf(paint.variable);
  const alpha = (paint.color.a === undefined ? 1 : paint.color.a) * (paint.opacity === undefined ? 1 : paint.opacity);
  return { value: toHex({ r: paint.color.r, g: paint.color.g, b: paint.color.b, a: alpha }) };
}

function radiusValue(layer) {
  const bound = layer.boundVariables || {};
  const corners = ["topLeftRadius", "topRightRadius", "bottomRightRadius", "bottomLeftRadius"];
  const names = corners.map(c => bound[c]);
  if (names[0] && names.every(n => n === names[0])) return tokenOf(names[0]);
  if (typeof layer.cornerRadius === "number") return { value: px(layer.cornerRadius) };
  if (corners.every(c => typeof layer[c] === "number")) return { value: corners.map(c => px(layer[c])).join(" ") };
  return null;
}

function shadowValue(layer) {
  if (layer.effectStyle && layer.effectStyle.token) return { token: layer.effectStyle.token };
  const shadows = visibleEffects(layer.effects).filter(e => e.type === "DROP_SHADOW");
  if (!shadows.length) return { value: "none" };
  return {
    value: shadows.map(e => `${px(e.offset.x)} ${px(e.offset.y)} ${px(e.radius)} ${px(e.spread || 0)} ${toHex(e.color)}`).join(", "),
  };
}

// The keys of a layer snapshot that compareLayer does not report as "is not animated". Every other
// key that differs between the two variants is reported, so a field a snapshot adds (Task 7's
// snapshotSet, or a later Figma API) is never dropped silently. Add a key here only on purpose.
const LAYER_KEYS_HANDLED = [
  // translated, or reported with their own reason, by compareLayer
  "fills", "strokes", "characters", "width", "height", "opacity", "rotation", "effects", "effectStyle",
  "cornerRadius", "topLeftRadius", "topRightRadius", "bottomRightRadius", "bottomLeftRadius",
  // compared entry by entry below, apart from the radius corners
  "boundVariables",
  // positional: the root's x/y is its place in the set; a descendant's is a translate, in compareVariants
  "x", "y",
  // structural: identity, nesting, tags and interaction, not appearance
  "name", "type", "id", "children", "selector", "motionName", "variantProperties", "reactions",
];
const RADIUS_CORNERS = ["topLeftRadius", "topRightRadius", "bottomRightRadius", "bottomLeftRadius"];
// boundVariables entries compareLayer translates already: the radius corners, and fills (a paint's "variable").
// snapshotSet leaves fills, strokes and effects out of boundVariables: a paint carries its own "variable".
const BOUND_KEYS_HANDLED = RADIUS_CORNERS.concat(["fills"]);
// Bound fields whose literal value compareLayer translates or reports: a changed binding is reported
// only when the value stays the same, so a bound width that changes is not reported twice. The
// paddings and item spacing snapshotSet reads are reported by the catch-all below.
const BOUND_KEYS_LITERAL = ["width", "height", "opacity", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft", "itemSpacing"];
const NOT_ANIMATED = "is not animated";

/** The changes between one layer in two variants: CSS changes and what does not translate. */
function compareLayer(a, b, self) {
  const changes = [];
  const notTranslated = [];
  const change = (property, from, to) => {
    if (from && to && !same(from, to)) changes.push({ property, from, to });
  };
  const report = (property, reason) => notTranslated.push({ property, reason });
  const text = a.type === "TEXT" || b.type === "TEXT";

  const fillsA = visiblePaints(a.fills), fillsB = visiblePaints(b.fills);
  const [fillA, fillB] = [fillsA[0], fillsB[0]];
  if (!same(fillA, fillB)) {
    const odd = [fillA, fillB].find(p => p && p.type !== "SOLID");
    if (odd) report("fill", `${odd.type} fills do not translate; use a solid fill.`);
    else change(text || self ? "color" : "background-color", paintValue(fillA), paintValue(fillB));
  }
  if (!same(fillsA.slice(1), fillsB.slice(1))) report("fill", "Only the first fill translates.");

  if (!same(visiblePaints(a.strokes), visiblePaints(b.strokes))) report("stroke", "Strokes do not translate.");

  if (text) {
    const sized = a.width !== b.width || a.height !== b.height;
    if (a.characters !== b.characters) report("text", "Text content does not animate; the layer's size follows it.");
    else if (sized) report("text", "A text layer's size follows its text and does not animate.");
  } else {
    if (a.width !== b.width) change("width", { value: px(a.width) }, { value: px(b.width) });
    if (a.height !== b.height) change("height", { value: px(a.height) }, { value: px(b.height) });
  }

  const opacity = layer => (layer.opacity === undefined ? 1 : layer.opacity);
  if (opacity(a) !== opacity(b)) change("opacity", { value: String(round(opacity(a), 3)) }, { value: String(round(opacity(b), 3)) });

  const radiusA = radiusValue(a), radiusB = radiusValue(b);
  if (Boolean(radiusA) !== Boolean(radiusB)) report("border-radius", "The corner radius cannot be read in one of the variants.");
  else change("border-radius", radiusA, radiusB);

  const rotation = layer => -(layer.rotation || 0) || 0;
  if (rotation(a) !== rotation(b)) change("rotate", { value: `${round(rotation(a), 2)}deg` }, { value: `${round(rotation(b), 2)}deg` });

  const shadowA = shadowValue(a), shadowB = shadowValue(b);
  if (text && !same(shadowA, shadowB)) report("box-shadow", "A shadow on a text layer does not translate.");
  else change("box-shadow", shadowA, shadowB);
  const others = layer => visibleEffects(layer.effects).filter(e => e.type !== "DROP_SHADOW");
  if (!same(others(a), others(b))) {
    const kinds = [...new Set(others(a).concat(others(b)).map(e => e.type))].join(", ");
    report("effect", `${kinds || "This effect"} does not translate; only drop shadows do.`);
  }

  const boundA = a.boundVariables || {}, boundB = b.boundVariables || {};
  for (const key of new Set(Object.keys(boundA).concat(Object.keys(boundB)))) {
    if (BOUND_KEYS_HANDLED.includes(key) || same(boundA[key], boundB[key])) continue;
    if (BOUND_KEYS_LITERAL.includes(key) && !same(a[key], b[key])) continue;
    report(`boundVariables.${key}`, NOT_ANIMATED);
  }
  for (const key of new Set(Object.keys(a).concat(Object.keys(b)))) {
    if (!LAYER_KEYS_HANDLED.includes(key) && !same(a[key], b[key])) report(key, NOT_ANIMATED);
  }

  return { changes, notTranslated };
}

/**
 * Compares two variants of a set: the hover's changes and what does not translate. Descendants
 * are matched by name at each level and named by their path, "Accent/Dot".
 */
function compareVariants(before, after, isTheme) {
  const rootA = before.layer || before, rootB = after.layer || after;
  const root = compareLayer(rootA, rootB, false);
  const changes = root.changes.slice();
  const notTranslated = root.notTranslated.slice();

  // An untagged layer inside a :self layer is :self too: an icon's vectors draw in its currentColor.
  // Two of them changing the colour alike give one change; differently, the second is reported.
  const selfColour = (layer, c) => {
    const other = changes.find(x => !x.layer && x.property === c.property);
    if (!other) changes.push(c);
    else if (!same(other, c)) notTranslated.push({ layer, property: c.property, reason: "another :self layer changes this colour differently" });
  };
  const walk = (parentA, parentB, path, chain, inSelf) => {
    const kidsA = parentA.children || [], kidsB = parentB.children || [];
    const names = [...new Set(kidsA.concat(kidsB).map(k => k.name))];
    for (const name of names) {
      const named = path ? `${path}/${name}` : name;
      const as = kidsA.filter(k => k.name === name), bs = kidsB.filter(k => k.name === name);
      let pairs;
      if (as.length > 1 || bs.length > 1) {
        // Inside a :self layer (an icon's vectors, all "Vector") only the colour comes across, and
        // it is the same for each, so same-named siblings pair up by their order.
        if (!(isTheme && inSelf && as.length === bs.length)) {
          notTranslated.push({ layer: named, property: "layer", reason: "two layers share this name; rename one" });
          continue;
        }
        pairs = as.map((a, i) => [`${named}#${i + 1}`, a, bs[i]]);
      } else if (!as.length || !bs.length) {
        notTranslated.push({ layer: named, property: "layer", reason: as.length ? "The layer is in the resting variant only." : "The layer is in the hover variant only." });
        continue;
      } else pairs = [[named, as[0], bs[0]]];
      for (const [layer, a, b] of pairs) compareChild(layer, a, b, name, chain, inSelf);
    }
  };
  const compareChild = (layer, a, b, name, chain, inSelf) => {
    const tag = a.selector || b.selector || (inSelf ? ":self" : undefined);
    const self = isTheme && tag === ":self";
    const diff = compareLayer(a, b, self);
    if (a.x !== b.x || a.y !== b.y) {
      diff.changes.push({ property: "translate", from: { value: "0px 0px" }, to: { value: `${px(b.x - a.x)} ${px(b.y - a.y)}` } });
    }
    for (const n of diff.notTranslated) notTranslated.push(Object.assign({ layer }, n));
    const part = isTheme ? tag : `${chain ? chain + " " : ""}[data-motion-part="${kebab(name)}"]`;
    for (const c of diff.changes) {
      if (isTheme && !tag) notTranslated.push({ layer, property: c.property, reason: UNTAGGED_LAYER });
      else if (self && c.property !== "color") notTranslated.push({ layer, property: c.property, reason: SELF_ONLY });
      else if (self) selfColour(layer, c);
      else changes.push(Object.assign({ layer, part }, c));
    }
    walk(a, b, layer, isTheme ? "" : part, self);
  };
  walk(rootA, rootB, "", "", false);
  return { changes, notTranslated };
}

function easingOf(easing) {
  if (easing.type === "CUSTOM_CUBIC_BEZIER") {
    const { x1, y1, x2, y2 } = easing.easingFunctionCubicBezier;
    return { type: easing.type, x1: round(x1, 3), y1: round(y1, 3), x2: round(x2, 3), y2: round(y2, 3) };
  }
  if (easing.type === "CUSTOM_SPRING") {
    const { mass, stiffness, damping } = easing.easingFunctionSpring;
    return { type: easing.type, mass, stiffness, damping };
  }
  return { type: easing.type };
}

/** The hovers of a set snapshot, in the motion document's shape, and the reactions it skips. */
function hoversOf(setSnapshot) {
  const hovers = [];
  const skipped = [];
  const isTheme = Boolean(setSnapshot.selector) || Object.prototype.hasOwnProperty.call(THEME_SELECTORS, setSnapshot.name);
  const name = setSnapshot.motionName || motionName(setSnapshot.name);
  const byId = new Map(setSnapshot.variants.map(v => [v.id, v]));

  for (const before of setSnapshot.variants) {
    const skip = reason => skipped.push({ component: setSnapshot.name, variant: before.name, reason });
    for (const reaction of before.reactions || []) {
      const trigger = reaction.trigger ? reaction.trigger.type : "NONE";
      if (trigger !== "ON_HOVER") { skip(`${trigger} is not translated; only While hovering is.`); continue; }
      for (const action of reaction.actions || (reaction.action ? [reaction.action] : [])) {
        const after = action.type === "NODE" && action.navigation === "CHANGE_TO" ? byId.get(action.destinationId) : null;
        if (!after) { skip("The hover does not change to another variant of the set."); continue; }
        const transition = action.transition;
        if (!transition || transition.type !== "SMART_ANIMATE") {
          skip(`${transition ? transition.type : "An instant change"} is not translated; use Smart Animate.`);
          continue;
        }
        const props = before.variantProperties || {};
        const switching = Object.keys(props).filter(key => props[key] !== (after.variantProperties || {})[key]);
        if (switching.length !== 1) {
          skip(`The hover variant differs in ${switching.length ? switching.join(", ") : "no variant property"}; it must differ in one.`);
          continue;
        }
        const others = Object.keys(props).filter(key => key !== switching[0]);
        const selector = setSnapshot.selector
          ? before.selector || setSnapshot.selector
          : `[data-motion="${name}"]` + others.map(key => `[data-${kebab(key)}="${kebab(props[key])}"]`).join("");
        const easing = easingOf(transition.easing);
        const duration = SPRING_PRESETS.includes(easing.type) ? PRESET_DURATION : Math.round(transition.duration * 1000);
        const { changes, notTranslated } = compareVariants(before, after, isTheme);
        hovers.push({
          name: others.length ? `${name}[${others.map(key => `${kebab(key)}=${kebab(props[key])}`).join(",")}]` : name,
          selector,
          trigger: "hover",
          duration,
          easing,
          changes,
          notTranslated,
        });
      }
    }
  }
  return { hovers, skipped };
}

/** The motion document of a list of set snapshots. */
function motionDocument(setSnapshots) {
  const results = setSnapshots.map(hoversOf);
  return {
    version: 1,
    hovers: [].concat(...results.map(r => r.hovers)),
    skipped: [].concat(...results.map(r => r.skipped)),
  };
}

// ---- motion in Figma: tags, snapshots, export ----------------------------------------------
//
// These read and write the file through figma.*. They are plain functions of a node, so they run
// from an eval of this file as well as from the panel.

const NACARA = "nacara";

// The boundVariables fields a snapshot keeps, and why. Any other binding is left out on purpose:
// fills, strokes, effects and textRangeFills are carried by each paint's (or the effect style's) own
// value, and componentProperties and layoutGrids are not appearance a hover animates, so keeping them
// would only add a "boundVariables.… is not animated" next to a change already translated.
const BOUND_SNAPSHOT_KEYS = [
  // the radius as a token, translated by compareLayer
  ...RADIUS_CORNERS,
  // translated or reported as literal values; a binding swap alone is reported (BOUND_KEYS_LITERAL)
  ...BOUND_KEYS_LITERAL,
  // real changes the comparison reports as "is not animated" when their binding differs
  "visible", "minWidth", "maxWidth", "minHeight", "maxHeight", "counterAxisSpacing",
  "strokeWeight", "strokeTopWeight", "strokeRightWeight", "strokeBottomWeight", "strokeLeftWeight",
  "characters", "fontFamily", "fontSize", "fontStyle", "fontWeight", "letterSpacing", "lineHeight",
  "paragraphSpacing", "paragraphIndent",
];

/**
 * The component sets under a page or the document: Figma's own search where it has one (a page, and
 * the document once its pages are loaded), a walk otherwise.
 */
function componentSetsIn(node) {
  if (typeof node.findAllWithCriteria === "function") {
    try {
      return node.findAllWithCriteria({ types: ["COMPONENT_SET"] });
    } catch (error) {
      // the document before loadAllPagesAsync, in dynamic-page mode: walk what is loaded
    }
  }
  const sets = [];
  const walk = n => {
    if (n.type === "COMPONENT_SET") sets.push(n);
    else for (const child of n.children || []) walk(child);
  };
  walk(node);
  return sets;
}

/**
 * Tags the theme's component sets under a page or the document, those built before tagging existed
 * included: "selector" on the set, on each variant THEME_SELECTORS names, and on each named layer of a
 * variant. A tag THEME_SELECTORS no longer gives (a renamed or duplicated set, a renamed variant or
 * layer) is cleared at those three levels, so a copied tag never passes for the theme's. Writes only
 * what differs, so a second run changes nothing.
 */
async function tagThemeComponents(node) {
  let sets = 0, written = 0;
  const tag = (target, selector) => {
    if (target.getSharedPluginData(NACARA, "selector") === selector) return;
    target.setSharedPluginData(NACARA, "selector", selector);
    written++;
  };
  for (const set of componentSetsIn(node)) {
    const entry = THEME_SELECTORS[set.name] || { set: "", variants: {}, layers: {} };
    if (entry.set) sets++;
    tag(set, entry.set);
    for (const variant of set.children) {
      tag(variant, entry.variants[variant.name] || "");
      // A variant's own layers only: the Badge instance holds a Label of its own.
      for (const layer of variant.children || []) tag(layer, entry.layers[layer.name] || "");
    }
  }
  return { sets, written };
}

const round3 = x => round(x, 3);
const roundColour = c => {
  const out = { r: round3(c.r), g: round3(c.g), b: round3(c.b) };
  if (c.a !== undefined) out.a = round3(c.a);
  return out;
};

/** A snapshot reader with a cache of variable and style names, since one set asks for each many times. */
function snapshotReader() {
  const variables = new Map();
  const variableName = async id => {
    if (!variables.has(id)) {
      const variable = await figma.variables.getVariableByIdAsync(id);
      variables.set(id, variable ? variable.name : id);
    }
    return variables.get(id);
  };
  // boundVariables as variable names: an alias becomes its name, arrays and objects map through.
  const names = async value => {
    if (Array.isArray(value)) return Promise.all(value.map(names));
    if (value && value.type === "VARIABLE_ALIAS") return variableName(value.id);
    if (value && typeof value === "object") {
      const out = {};
      for (const [key, v] of Object.entries(value)) out[key] = await names(v);
      return out;
    }
    return value;
  };
  const paint = async p => {
    if (p.type !== "SOLID") {
      // Not translated, but kept whole so two different gradients or images still differ.
      const kept = JSON.parse(JSON.stringify(p));
      delete kept.boundVariables;
      return kept;
    }
    const out = { type: p.type, color: roundColour(p.color), opacity: round3(p.opacity === undefined ? 1 : p.opacity) };
    if (p.boundVariables && p.boundVariables.color) out.variable = await variableName(p.boundVariables.color.id);
    if (p.visible === false) out.visible = false;
    return out;
  };
  // Mixed text fills compare as one paint that does not translate.
  const paints = async list => (list === figma.mixed ? [{ type: "MIXED" }] : Promise.all(list.map(paint)));
  const effect = e => {
    const out = { type: e.type };
    if (e.color) out.color = roundColour(e.color);
    if (e.offset) out.offset = { x: e.offset.x, y: e.offset.y };
    if (e.radius !== undefined) out.radius = e.radius;
    if (e.spread !== undefined) out.spread = e.spread;
    if (e.visible === false) out.visible = false;
    return out;
  };
  const styleName = async id => {
    const style = await figma.getStyleByIdAsync(id);
    return style ? style.name : id;
  };
  const plain = v => (v === figma.mixed ? "mixed" : v);

  // A layer in the capture's shape; the keys after it are read only where the node has them, and the
  // comparison reports any of them that differ (they are not in LAYER_KEYS_HANDLED).
  const layer = async node => {
    const out = { name: node.name, type: node.type };
    if (node.type === "TEXT") out.characters = node.characters;
    if (node.type === "INSTANCE") {
      const main = await node.getMainComponentAsync();
      out.mainComponent = main ? main.name : null;
    }
    for (const key of ["x", "y", "width", "height", "rotation", "opacity"]) out[key] = node[key];
    if (node.cornerRadius === figma.mixed) for (const corner of RADIUS_CORNERS) out[corner] = node[corner];
    else if (typeof node.cornerRadius === "number") out.cornerRadius = node.cornerRadius;
    if (node.fills !== undefined) out.fills = await paints(node.fills);
    if (node.strokes !== undefined) out.strokes = await paints(node.strokes);
    const bound = {};
    for (const [key, value] of Object.entries(node.boundVariables || {})) {
      if (BOUND_SNAPSHOT_KEYS.includes(key)) bound[key] = await names(value);
    }
    if (Object.keys(bound).length) out.boundVariables = bound;
    if (node.effectStyleId) {
      const style = await figma.getStyleByIdAsync(node.effectStyleId);
      if (style) out.effectStyle = { name: style.name, token: style.getSharedPluginData(NACARA, "token") };
    }
    if (node.effects && node.effects.length) out.effects = node.effects.map(effect);
    if (node.visible === false) out.visible = false;
    if (node.blendMode && node.blendMode !== "PASS_THROUGH" && node.blendMode !== "NORMAL") out.blendMode = node.blendMode;
    for (const key of ["paddingTop", "paddingRight", "paddingBottom", "paddingLeft", "itemSpacing"]) {
      if (typeof node[key] === "number") out[key] = node[key];
    }
    if (node.type === "TEXT") {
      if (node.textStyleId) out.textStyle = node.textStyleId === figma.mixed ? "mixed" : await styleName(node.textStyleId);
      else if (node.fontSize !== undefined) Object.assign(out, { fontSize: plain(node.fontSize), fontName: plain(node.fontName) });
    }
    const selector = node.getSharedPluginData ? node.getSharedPluginData(NACARA, "selector") : "";
    if (selector) out.selector = selector;
    if (node.children) out.children = await Promise.all(node.children.map(layer));
    return out;
  };
  return { layer };
}

/** A component set as plain data, the shape hoversOf reads, with its "nacara" tags. */
async function snapshotSet(set) {
  const { layer } = snapshotReader();
  const out = { id: set.id, name: set.name, description: set.description || "" };
  const selector = set.getSharedPluginData(NACARA, "selector");
  if (selector) out.selector = selector;
  const name = set.getSharedPluginData(NACARA, "motion-name");
  if (name) out.motionName = name;
  out.variants = [];
  for (const variant of set.children) {
    const snapshot = {
      id: variant.id,
      name: variant.name,
      variantProperties: Object.assign({}, variant.variantProperties),
      reactions: (variant.reactions || []).map(r => JSON.parse(JSON.stringify({ trigger: r.trigger, actions: r.actions || (r.action ? [r.action] : []) }))),
    };
    const tag = variant.getSharedPluginData(NACARA, "selector");
    if (tag) snapshot.selector = tag;
    const root = await layer(variant);
    delete root.selector; // the variant's tag is on the variant
    snapshot.layer = root;
    out.variants.push(snapshot);
  }
  return out;
}

/**
 * The motion document of every component set in the file with a reaction, and those sets. It tags the
 * theme's sets first, so a library built before tagging exports the theme's selectors.
 */
async function exportMotion() {
  if (typeof figma.loadAllPagesAsync === "function") await figma.loadAllPagesAsync();
  await tagThemeComponents(figma.root);
  const snapshots = [];
  for (const set of componentSetsIn(figma.root)) {
    if (set.children.some(v => (v.reactions || []).length)) snapshots.push(await snapshotSet(set));
  }
  return {
    document: motionDocument(snapshots),
    sets: snapshots.map(s => ({
      id: s.id,
      name: s.name,
      motionName: s.motionName || motionName(s.name),
      tagged: Boolean(s.selector),
      theme: Object.prototype.hasOwnProperty.call(THEME_SELECTORS, s.name),
      selectors: hoversOf(s).hovers.map(h => h.selector),
    })),
  };
}

/** Names a set's motion, kebab-cased; an empty name goes back to the set's own. */
async function renameMotion(setId, name) {
  const set = await figma.getNodeByIdAsync(setId);
  if (!set || set.type !== "COMPONENT_SET") throw new Error(`No component set ${setId}.`);
  const value = kebab(name || "");
  set.setSharedPluginData(NACARA, "motion-name", value);
  return { id: setId, motionName: value || motionName(set.name) };
}

// ---- plugin entry --------------------------------------------------------------------------

if (typeof __html__ !== "undefined") {
  figma.showUI(__html__, { width: 420, height: 560, themeColors: true });
  figma.ui.onmessage = async message => {
    try {
      if (message.type === "import") figma.ui.postMessage({ type: "imported", result: await importTokens(message.documents) });
      if (message.type === "export") figma.ui.postMessage({ type: "exported", documents: await exportTokens() });
      if (message.type === "build") figma.ui.postMessage({ type: "built", result: await buildLibrary() });
      if (message.type === "export-layouts") figma.ui.postMessage({ type: "exported-layouts", result: await exportLayouts() });
      if (message.type === "motion-scan") figma.ui.postMessage({ type: "motion-scanned", result: await exportMotion() });
      if (message.type === "motion-export") figma.ui.postMessage({ type: "motion-exported", result: await exportMotion() });
      if (message.type === "motion-rename") {
        await renameMotion(message.setId, message.name);
        figma.ui.postMessage({ type: "motion-scanned", result: await exportMotion() });
      }
    } catch (error) {
      figma.ui.postMessage({ type: "error", message: String(error) });
    }
  };
}

if (typeof module !== "undefined") {
  module.exports = {
    THEME_SELECTORS, motionName, applyTags, compareVariants, hoversOf, motionDocument,
    tagThemeComponents, snapshotSet, exportMotion, renameMotion,
  };
}
