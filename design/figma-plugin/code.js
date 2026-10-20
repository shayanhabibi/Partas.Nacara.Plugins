// Imports the theme's design tokens documents (design/tokens/*.tokens.json) as Figma
// variables, and exports Figma variables back as the same documents.
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
  if (type === "dimension" || type === "number") {
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
        const type =
          resolved === "COLOR" ? "color"
          : resolved === "FLOAT" ? "dimension"
          : path.includes("font") ? "fontFamily"
          : "string";
        const value =
          aliased ? `{${aliased.name.split("/").join(".")}}`
          : resolved === "COLOR" ? toHex(raw)
          : resolved === "FLOAT" ? { value: raw, unit: "px" }
          : raw;
        place(document, path, { $type: type, $value: value });
      }
    }
  }
  return documents;
}

// ---- plugin entry --------------------------------------------------------------------------

if (typeof __html__ !== "undefined") {
  figma.showUI(__html__, { width: 420, height: 520, themeColors: true });
  figma.ui.onmessage = async message => {
    try {
      if (message.type === "import") figma.ui.postMessage({ type: "imported", result: await importTokens(message.documents) });
      if (message.type === "export") figma.ui.postMessage({ type: "exported", documents: await exportTokens() });
    } catch (error) {
      figma.ui.postMessage({ type: "error", message: String(error) });
    }
  };
}
