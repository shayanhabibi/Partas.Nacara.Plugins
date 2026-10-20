// The motion section of code.js against a real capture: node --test design/figma-plugin/motion.test.js

const test = require("node:test");
const assert = require("node:assert/strict");
const { motionName, hoversOf, motionDocument, applyTags, THEME_SELECTORS, tagThemeComponents, snapshotSet, exportMotion, renameMotion } = require("./code.js");
const capture = require("../../tests/Partas.Nacara.Plugins.Tests/figma-hover.json");

const set = name => JSON.parse(JSON.stringify(capture.componentSets.find(s => s.name === name)));
const tagged = name => applyTags(set(name), THEME_SELECTORS);
const hover = (snapshot, name) => hoversOf(snapshot).hovers.find(h => h.name === name);

test("motionName kebabs the set name", () => {
  assert.equal(motionName("Navbar link"), "navbar-link");
});

test("navbar link hover", () => {
  const { hovers, skipped } = hoversOf(tagged("Navbar link"));
  assert.equal(hovers.length, 1);
  assert.deepEqual(skipped, []);
  const h = hovers[0];
  assert.equal(h.name, "navbar-link");
  assert.equal(h.selector, '.nacara-navbar__link:not([data-active="true"]):not([aria-current])');
  assert.equal(h.trigger, "hover");
  assert.equal(h.duration, 200);
  assert.deepEqual(h.easing, { type: "EASE_OUT" });
  assert.deepEqual(h.changes, [
    { property: "background-color", from: { value: "transparent" }, to: { token: "nacara.color.bg-subtle", value: "#f6f7f9" } },
    { property: "color", from: { token: "nacara.color.text-muted", value: "#5a6470" }, to: { token: "nacara.color.text", value: "#1b1f24" } },
  ]);
  assert.deepEqual(h.notTranslated, []);
});

test("sidebar link plays its preset over 1200ms", () => {
  const h = hover(tagged("Sidebar link"), "sidebar-link");
  assert.equal(h.selector, '.nacara-sidebar__link:not([aria-current="page"])');
  assert.deepEqual(h.easing, { type: "GENTLE" });
  assert.equal(h.duration, 1200);
  assert.deepEqual(h.changes.map(c => c.property), ["background-color", "color"]);
  assert.deepEqual(h.notTranslated, []);
});

test("an untagged theme layer is reported", () => {
  const h = hover(set("Navbar link"), "navbar-link");
  assert.deepEqual(h.changes, [
    { property: "background-color", from: { value: "transparent" }, to: { token: "nacara.color.bg-subtle", value: "#f6f7f9" } },
  ]);
  assert.deepEqual(h.notTranslated, [
    { layer: "Label", property: "color", reason: "no selector: tag the layer or leave it unchanged" },
  ]);
});

test("small card", () => {
  const h = hover(set("Card"), "card[size=small]");
  assert.equal(h.selector, '[data-motion="card"][data-size="small"]');
  assert.deepEqual(h.easing, { type: "CUSTOM_SPRING", mass: 1, stiffness: 300, damping: 20 });
  assert.equal(h.duration, 500);
  const part = '[data-motion-part="accent"]';
  assert.deepEqual(h.changes, [
    { property: "background-color", from: { token: "nacara.color.bg-raised", value: "#ffffff" }, to: { value: "#f7f7ff" } },
    { property: "border-radius", from: { token: "nacara.size.radius", value: "8px" }, to: { value: "12px" } },
    { property: "box-shadow", from: { value: "none" }, to: { token: "nacara.shadow.shadow", value: "0px 1px 2px 0px #1018280f, 0px 4px 12px 0px #1018280f" } },
    { layer: "Accent", part, property: "width", from: { value: "40px" }, to: { value: "80px" } },
    { layer: "Accent", part, property: "opacity", from: { value: "1" }, to: { value: "0.8" } },
  ]);
  assert.deepEqual(h.notTranslated, []);
});

test("a text layer's size is not translated", () => {
  const h = hover(set("Card"), "card[size=large]");
  assert.deepEqual(h.changes.filter(c => c.layer === "Title"), []);
  const title = h.notTranslated.filter(n => n.layer === "Title");
  assert.equal(title.length, 1);
  assert.equal(title[0].property, "text");
});

test("large card bezier is rounded", () => {
  const h = hover(set("Card"), "card[size=large]");
  assert.deepEqual(h.easing, { type: "CUSTOM_CUBIC_BEZIER", x1: 0.4, y1: 0, x2: 0.2, y2: 1 });
  assert.equal(h.duration, 250);
});

test("a shadow with no style is literal", () => {
  const card = set("Card");
  delete card.variants.find(v => v.name === "Size=Small, State=Hover").layer.effectStyle;
  const shadow = hover(card, "card[size=small]").changes.find(c => c.property === "box-shadow");
  assert.deepEqual(shadow.to, { value: "0px 1px 2px 0px #1018280f, 0px 4px 12px 0px #1018280f" });
});

test("a dissolve is skipped", () => {
  const navbar = set("Navbar link");
  navbar.variants[0].reactions[0].actions[0].transition.type = "DISSOLVE";
  const { hovers, skipped } = hoversOf(navbar);
  assert.deepEqual(hovers, []);
  assert.equal(skipped.length, 1);
  assert.match(skipped[0].reason, /DISSOLVE/);
  assert.equal(skipped[0].component, "Navbar link");
  assert.equal(skipped[0].variant, "State=Default");
});

test("variants differing in two properties are skipped", () => {
  const card = set("Card");
  card.variants[0].reactions[0].actions[0].destinationId = "1:196";
  const { hovers, skipped } = hoversOf(card);
  assert.deepEqual(hovers.map(h => h.name), ["card[size=large]"]);
  assert.equal(skipped.length, 1);
  assert.equal(skipped[0].variant, "Size=Small, State=Default");
});

test("the document holds every hover", () => {
  const document = motionDocument(capture.componentSets.map(s => applyTags(s, THEME_SELECTORS)));
  assert.equal(document.version, 1);
  assert.deepEqual(document.hovers.map(h => h.name), ["navbar-link", "sidebar-link", "card[size=small]", "card[size=large]"]);
  assert.deepEqual(document.skipped, []);
});

// ---- fix round 1: nothing a variant changes is dropped ----

const variant = (snapshot, name) => snapshot.variants.find(v => v.name === name).layer;
const child = (layer, name) => layer.children.find(c => c.name === name);
const SELF_ONLY = "a :self layer only carries its colour";
const UNTAGGED = "no selector: tag the layer or leave it unchanged";

test("a :self layer carries only its colour", () => {
  const navbar = tagged("Navbar link");
  child(variant(navbar, "State=Hover"), "Label").opacity = 0.5;
  const h = hover(navbar, "navbar-link");
  assert.deepEqual(h.changes.map(c => c.property), ["background-color", "color"]);
  assert.deepEqual(h.notTranslated, [{ layer: "Label", property: "opacity", reason: SELF_ONLY }]);
});

test("a grandchild of an untagged set chains its parts", () => {
  const card = set("Card");
  const dot = opacity => ({ name: "Dot", type: "ELLIPSE", x: 0, y: 0, width: 4, height: 4, rotation: 0, opacity, fills: [], strokes: [] });
  child(variant(card, "Size=Small, State=Default"), "Accent").children = [dot(1)];
  child(variant(card, "Size=Small, State=Hover"), "Accent").children = [dot(0.5)];
  const h = hover(card, "card[size=small]");
  assert.deepEqual(h.changes.filter(c => c.layer === "Accent/Dot"), [
    { layer: "Accent/Dot", part: '[data-motion-part="accent"] [data-motion-part="dot"]', property: "opacity", from: { value: "1" }, to: { value: "0.5" } },
  ]);
});

test("a grandchild of a tagged set uses its own tag, or is reported", () => {
  const sidebar = tagged("Sidebar link");
  const kids = opacity => [
    { name: "Dot", type: "ELLIPSE", selector: ".nacara-badge__dot", x: 0, y: 0, width: 4, height: 4, rotation: 0, opacity, fills: [], strokes: [] },
    { name: "Count", type: "ELLIPSE", x: 0, y: 0, width: 4, height: 4, rotation: 0, opacity, fills: [], strokes: [] },
  ];
  child(variant(sidebar, "State=Default"), "Badge").children = kids(1);
  child(variant(sidebar, "State=Hover"), "Badge").children = kids(0.5);
  const h = hover(sidebar, "sidebar-link");
  assert.deepEqual(h.changes.filter(c => c.layer === "Badge/Dot"), [
    { layer: "Badge/Dot", part: ".nacara-badge__dot", property: "opacity", from: { value: "1" }, to: { value: "0.5" } },
  ]);
  assert.deepEqual(h.notTranslated, [{ layer: "Badge/Count", property: "opacity", reason: UNTAGGED }]);
});

test("a field the comparison does not model is reported", () => {
  const sidebar = tagged("Sidebar link");
  const after = variant(sidebar, "State=Hover");
  child(after, "Badge").mainComponent = "Kind=Beta";
  after.boundVariables = Object.assign({}, after.boundVariables, { paddingLeft: "nacara/size/space-4" });
  after.blendMode = "MULTIPLY";
  const h = hover(sidebar, "sidebar-link");
  assert.deepEqual(h.notTranslated, [
    { property: "boundVariables.paddingLeft", reason: "is not animated" },
    { property: "blendMode", reason: "is not animated" },
    { layer: "Badge", property: "mainComponent", reason: "is not animated" },
  ]);
});

test("two layers sharing a name are reported, not compared", () => {
  const card = set("Card");
  const after = variant(card, "Size=Small, State=Hover");
  after.children.push(JSON.parse(JSON.stringify(child(after, "Accent"))));
  const h = hover(card, "card[size=small]");
  assert.deepEqual(h.changes.filter(c => c.layer === "Accent"), []);
  assert.deepEqual(h.notTranslated, [{ layer: "Accent", property: "layer", reason: "two layers share this name; rename one" }]);
});

test("a radius that cannot be read on one side is reported", () => {
  const card = set("Card");
  delete variant(card, "Size=Small, State=Hover").cornerRadius;
  const h = hover(card, "card[size=small]");
  assert.deepEqual(h.changes.filter(c => c.property === "border-radius"), []);
  assert.deepEqual(h.notTranslated.map(n => n.property), ["border-radius"]);
});

test("a shadow on a text layer is reported", () => {
  const card = set("Card");
  child(variant(card, "Size=Small, State=Hover"), "Title").effects = [
    { type: "DROP_SHADOW", color: { r: 0, g: 0, b: 0, a: 0.5 }, offset: { x: 0, y: 1 }, radius: 2, spread: 0 },
  ];
  const h = hover(card, "card[size=small]");
  assert.deepEqual(h.changes.filter(c => c.layer === "Title"), []);
  assert.deepEqual(h.notTranslated.map(n => [n.layer, n.property]), [["Title", "box-shadow"]]);
});

// ---- Task 7: tags, snapshot and export, against fake Figma nodes ----

// A fake Figma node: plain data, children, and shared plugin data in a Map that counts its writes.
let writes = 0;
const pluginData = node => {
  const data = new Map();
  node.getSharedPluginData = (ns, key) => data.get(`${ns}/${key}`) || "";
  node.setSharedPluginData = (ns, key, value) => {
    writes++;
    if (value) data.set(`${ns}/${key}`, value);
    else data.delete(`${ns}/${key}`);
  };
  return node;
};

// The Figma nodes a capture layer was read from: a paint's `variable` is a bound colour, names are ids.
const alias = name => ({ type: "VARIABLE_ALIAS", id: `V:${name}` });
const fakePaint = p => {
  const paint = Object.assign({}, p);
  delete paint.variable;
  if (p.variable) paint.boundVariables = { color: alias(p.variable) };
  return paint;
};
const fakeLayer = layer => {
  const node = pluginData({});
  for (const [key, value] of Object.entries(layer)) {
    if (key === "fills" || key === "strokes") node[key] = value.map(fakePaint);
    else if (key === "boundVariables") {
      node.boundVariables = {};
      for (const [k, v] of Object.entries(value)) node.boundVariables[k] = Array.isArray(v) ? v.map(alias) : alias(v);
    } else if (key === "effectStyle") node.effectStyleId = `S:${value.name}`;
    else if (key === "mainComponent") node.getMainComponentAsync = async () => ({ name: value });
    else if (key === "children") node.children = value.map(fakeLayer);
    else node[key] = value;
  }
  if (!node.effects) node.effects = [];
  if (!node.effectStyleId) node.effectStyleId = "";
  return node;
};
const fakeSet = snapshot => {
  const node = pluginData({ id: snapshot.id, name: snapshot.name, type: "COMPONENT_SET", description: snapshot.description });
  node.children = snapshot.variants.map(v =>
    Object.assign(fakeLayer(v.layer), { id: v.id, name: v.name, type: "COMPONENT", variantProperties: v.variantProperties, reactions: v.reactions }));
  return node;
};
const styles = Object.fromEntries(
  capture.componentSets.flatMap(s => s.variants.map(v => v.layer.effectStyle).filter(Boolean)).map(s => [`S:${s.name}`, s]));
const fakeFigma = root => ({
  mixed: Symbol("mixed"),
  root,
  loadAllPagesAsync: async () => {},
  getNodeByIdAsync: async id => {
    const find = node => (node.id === id ? node : (node.children || []).map(find).find(Boolean));
    return find(root) || null;
  },
  getStyleByIdAsync: async id => {
    const style = styles[id];
    return style ? { name: style.name, getSharedPluginData: (ns, key) => (ns === "nacara" && key === "token" ? style.token : "") } : null;
  },
  variables: { getVariableByIdAsync: async id => ({ name: id.slice(2) }) },
});
const fakePage = sets => ({
  type: "PAGE", name: "Components",
  children: [{ type: "SECTION", name: "Navigation", children: [{ type: "FRAME", name: "Board", children: sets }] }],
});

test("tagging reaches sets that already exist", async () => {
  const navbar = fakeSet(set("Navbar link"));
  const page = fakePage([navbar]);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  await tagThemeComponents(page);
  const of = name => navbar.children.find(v => v.name === name);
  assert.equal(navbar.getSharedPluginData("nacara", "selector"), ".nacara-navbar__link");
  assert.equal(of("State=Default").getSharedPluginData("nacara", "selector"), '.nacara-navbar__link:not([data-active="true"]):not([aria-current])');
  assert.equal(of("State=Active").getSharedPluginData("nacara", "selector"), '.nacara-navbar__link:is([data-active="true"],[aria-current])');
  assert.equal(of("State=Hover").getSharedPluginData("nacara", "selector"), "");
  assert.equal(of("State=Default").children[0].getSharedPluginData("nacara", "selector"), ":self");
  writes = 0;
  await tagThemeComponents(page);
  assert.equal(writes, 0);
});

test("a renamed motion names the hover", () => {
  const card = Object.assign(set("Card"), { motionName: "promo" });
  const { hovers } = hoversOf(card);
  assert.deepEqual(hovers.map(h => h.name), ["promo[size=small]", "promo[size=large]"]);
  assert.equal(hovers[0].selector, '[data-motion="promo"][data-size="small"]');
});

test("every theme set with a hover has its selectors", () => {
  assert.deepEqual(THEME_SELECTORS["Icon button"], { set: ".nacara-icon-button", variants: {}, layers: { Icon: ":self" } });
  assert.deepEqual(THEME_SELECTORS["TOC link"], {
    set: ".nacara-toc__link",
    variants: {
      "Level=2, State=Default": '.nacara-toc__link:not([data-level="3"]):not([data-active="true"])',
      "Level=2, State=Active": '.nacara-toc__link:not([data-level="3"])[data-active="true"]',
      "Level=3, State=Default": '.nacara-toc__link[data-level="3"]:not([data-active="true"])',
      "Level=3, State=Active": '.nacara-toc__link[data-level="3"][data-active="true"]',
    },
    layers: { Label: ":self" },
  });
});

test("snapshotSet reads the capture's shape", async () => {
  const sets = capture.componentSets.map(fakeSet);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [fakePage(sets)] });
  for (const [i, node] of sets.entries()) assert.deepEqual(await snapshotSet(node), capture.componentSets[i]);
});

test("a tagged set snapshots as applyTags tags it", async () => {
  const sets = capture.componentSets.map(fakeSet);
  const page = fakePage(sets);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  await tagThemeComponents(page);
  for (const [i, node] of sets.entries()) {
    assert.deepEqual(await snapshotSet(node), applyTags(set(capture.componentSets[i].name), THEME_SELECTORS));
  }
});

test("an icon's colour comes across through its vectors", () => {
  const variantOf = (id, name, fill, color, reactions) => ({
    id, name, variantProperties: { State: name.split("=")[1] }, reactions,
    layer: {
      name, type: "COMPONENT", x: 0, y: 0, width: 36, height: 36, rotation: 0, opacity: 1, cornerRadius: 6, strokes: [],
      fills: fill ? [{ type: "SOLID", color: { r: 1, g: 1, b: 1 }, opacity: 1, variable: fill }] : [],
      children: [{
        name: "Icon", type: "INSTANCE", mainComponent: "Icon/GitHub", x: 9, y: 9, width: 18, height: 18, rotation: 0, opacity: 1, fills: [], strokes: [],
        children: ["Vector", "Vector"].map(n => ({
          name: n, type: "VECTOR", x: 0, y: 0, width: 18, height: 18, rotation: 0, opacity: 1, strokes: [],
          fills: [{ type: "SOLID", color: { r: 0, g: 0, b: 0 }, opacity: 1, variable: color }],
        })),
      }],
    },
  });
  const transition = { type: "SMART_ANIMATE", duration: 0.2, easing: { type: "EASE_OUT" } };
  const hoverTo = id => [{ trigger: { type: "ON_HOVER" }, actions: [{ type: "NODE", destinationId: id, navigation: "CHANGE_TO", transition }] }];
  const iconButton = {
    id: "1:1", name: "Icon button",
    variants: [
      variantOf("1:2", "State=Default", null, "nacara/color/text-muted", hoverTo("1:3")),
      variantOf("1:3", "State=Hover", "nacara/color/bg-subtle", "nacara/color/text", []),
    ],
  };
  const h = hover(applyTags(iconButton, THEME_SELECTORS), "icon-button");
  assert.equal(h.selector, ".nacara-icon-button");
  assert.deepEqual(h.changes, [
    { property: "background-color", from: { value: "transparent" }, to: { token: "nacara.color.bg-subtle", value: "#ffffff" } },
    { property: "color", from: { token: "nacara.color.text-muted", value: "#000000" }, to: { token: "nacara.color.text", value: "#000000" } },
  ]);
  assert.deepEqual(h.notTranslated, []);
});

test("a bound width is reported once", () => {
  const card = set("Card");
  const before = child(variant(card, "Size=Small, State=Default"), "Accent");
  const after = child(variant(card, "Size=Small, State=Hover"), "Accent");
  before.boundVariables = { width: "nacara/size/a" };
  after.boundVariables = { width: "nacara/size/b" };
  const h = hover(card, "card[size=small]");
  assert.equal(h.changes.filter(c => c.property === "width").length, 1);
  assert.deepEqual(h.notTranslated, []);
  after.width = before.width;
  const unchanged = hover(card, "card[size=small]");
  assert.deepEqual(unchanged.notTranslated, [{ layer: "Accent", property: "boundVariables.width", reason: "is not animated" }]);
});

test("exportMotion lists the sets with reactions, and renameMotion names one", async () => {
  const sets = capture.componentSets.map(fakeSet);
  const page = fakePage(sets.concat([fakeSet({ id: "9:9", name: "Badge", variants: [] })]));
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  await tagThemeComponents(page);
  const before = await exportMotion();
  const selectors = {
    "Navbar link": ['.nacara-navbar__link:not([data-active="true"]):not([aria-current])'],
    "Sidebar link": ['.nacara-sidebar__link:not([aria-current="page"])'],
    Card: ['[data-motion="card"][data-size="small"]', '[data-motion="card"][data-size="large"]'],
  };
  assert.deepEqual(before.sets, capture.componentSets.map(s => ({
    id: s.id, name: s.name, motionName: motionName(s.name), tagged: s.name !== "Card", theme: s.name !== "Card", selectors: selectors[s.name],
  })));
  assert.deepEqual(before.document, motionDocument(capture.componentSets.map(s => applyTags(s, THEME_SELECTORS))));
  const card = capture.componentSets.find(s => s.name === "Card").id;
  assert.deepEqual(await renameMotion(card, "Promo card"), { id: card, motionName: "promo-card" });
  const after = await exportMotion();
  assert.equal(after.sets.find(s => s.id === card).motionName, "promo-card");
  assert.deepEqual(after.document.hovers.map(h => h.name).slice(2), ["promo-card[size=small]", "promo-card[size=large]"]);
});

// ---- Task 7 fix round 1 ----

test("exporting tags theme sets built before tagging", async () => {
  // Two pages, no tags anywhere, and a document with no findAllWithCriteria: export walks and tags.
  const sets = capture.componentSets.map(fakeSet);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [fakePage(sets.slice(0, 1)), fakePage(sets.slice(1))] });
  const { document, sets: listed } = await exportMotion();
  assert.deepEqual(document, motionDocument(capture.componentSets.map(s => applyTags(s, THEME_SELECTORS))));
  assert.equal(document.hovers[0].selector, '.nacara-navbar__link:not([data-active="true"]):not([aria-current])');
  assert.deepEqual(listed.map(s => [s.name, s.tagged, s.theme]), [["Navbar link", true, true], ["Sidebar link", true, true], ["Card", false, false]]);
});

test("exporting uses the document's own search when it has one", async () => {
  const sets = capture.componentSets.map(fakeSet);
  const root = { type: "DOCUMENT", children: [], findAllWithCriteria: ({ types }) => (types[0] === "COMPONENT_SET" ? sets : []) };
  global.figma = fakeFigma(root);
  const { document } = await exportMotion();
  assert.deepEqual(document.hovers.map(h => h.name), ["navbar-link", "sidebar-link", "card[size=small]", "card[size=large]"]);
  assert.equal(document.hovers[1].selector, '.nacara-sidebar__link:not([aria-current="page"])');
});

test("a copied theme tag is cleared", async () => {
  const copy = fakeSet(Object.assign(set("Navbar link"), { name: "Navbar link copy" }));
  const page = fakePage([copy]);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  copy.setSharedPluginData("nacara", "selector", ".nacara-navbar__link");
  copy.children[0].setSharedPluginData("nacara", "selector", '.nacara-navbar__link:not([data-active="true"]):not([aria-current])');
  copy.children[0].children[0].setSharedPluginData("nacara", "selector", ":self");
  await tagThemeComponents(page);
  assert.equal(copy.getSharedPluginData("nacara", "selector"), "");
  assert.equal(copy.children[0].getSharedPluginData("nacara", "selector"), "");
  // ":self" is not the theme's own value: it stays, and without the set's selector it is ignored.
  assert.equal(copy.children[0].children[0].getSharedPluginData("nacara", "selector"), ":self");
  const h = hoversOf(await snapshotSet(copy)).hovers[0];
  assert.equal(h.selector, '[data-motion="navbar-link-copy"]');
  assert.deepEqual(h.changes.map(c => [c.layer, c.part, c.property]), [
    [undefined, undefined, "background-color"],
    ["Label", '[data-motion-part="label"]', "color"],
  ]);
});

test("a selector of your own on your own set survives tagging", async () => {
  const promo = fakeSet(Object.assign(set("Navbar link"), { name: "Promo link" }));
  const page = fakePage([promo]);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  promo.setSharedPluginData("nacara", "selector", ".promo-link");
  promo.children[0].setSharedPluginData("nacara", "selector", ".promo-link:not(.is-active)");
  promo.children[0].children[0].setSharedPluginData("nacara", "selector", ":self");
  await tagThemeComponents(page);
  assert.equal(promo.getSharedPluginData("nacara", "selector"), ".promo-link");
  assert.equal(promo.children[0].getSharedPluginData("nacara", "selector"), ".promo-link:not(.is-active)");
  const h = hoversOf(await snapshotSet(promo)).hovers[0];
  assert.equal(h.selector, ".promo-link:not(.is-active)");
  assert.deepEqual(h.changes.map(c => [c.layer, c.property]), [[undefined, "background-color"], [undefined, "color"]]);
  assert.deepEqual(h.notTranslated, []);
});

test("a text range binding alone is not reported", async () => {
  const navbar = fakeSet(set("Sidebar link"));
  const label = name => navbar.children.find(v => v.name === name).children[0];
  label("State=Default").boundVariables.textRangeFills = [alias("nacara/color/text-muted")];
  label("State=Hover").boundVariables.textRangeFills = [alias("nacara/color/text")];
  for (const v of navbar.children) v.boundVariables.componentProperties = { Label: alias(`nacara/x/${v.name}`) };
  const page = fakePage([navbar]);
  global.figma = fakeFigma({ type: "DOCUMENT", children: [page] });
  await tagThemeComponents(page);
  const h = hoversOf(await snapshotSet(navbar)).hovers[0];
  assert.deepEqual(h.changes.map(c => c.property), ["background-color", "color"]);
  assert.deepEqual(h.notTranslated, []);
});

test("mixed values snapshot as mixed", async () => {
  global.figma = fakeFigma({ type: "DOCUMENT", children: [] });
  const mixed = figma.mixed;
  const styled = pluginData({ name: "Styled", type: "TEXT", characters: "Hi", x: 0, y: 0, width: 10, height: 10, rotation: 0, opacity: 1,
    fills: mixed, strokes: [], effects: [], effectStyleId: "", textStyleId: mixed, fontSize: 12 });
  const plain = pluginData({ name: "Plain", type: "TEXT", characters: "Hi", x: 0, y: 0, width: 10, height: 10, rotation: 0, opacity: 1,
    fills: [], strokes: [], effects: [], effectStyleId: "", textStyleId: "", fontSize: mixed, fontName: mixed });
  const box = pluginData({ name: "Box", type: "RECTANGLE", x: 0, y: 0, width: 10, height: 10, rotation: 0, opacity: 1, fills: [], strokes: [], effects: [],
    effectStyleId: "", cornerRadius: mixed, topLeftRadius: 1, topRightRadius: 2, bottomRightRadius: 3, bottomLeftRadius: 4 });
  const variant = Object.assign(pluginData({ id: "1:2", name: "State=Default", type: "COMPONENT", x: 0, y: 0, width: 10, height: 10, rotation: 0, opacity: 1,
    fills: [], strokes: [], effects: [], effectStyleId: "", variantProperties: { State: "Default" }, reactions: [] }), { children: [styled, plain, box] });
  const setNode = Object.assign(pluginData({ id: "1:1", name: "Mixed", type: "COMPONENT_SET", description: "" }), { children: [variant] });
  const [s, p, b] = (await snapshotSet(setNode)).variants[0].layer.children;
  assert.deepEqual(s.fills, [{ type: "MIXED" }]);
  assert.equal(s.textStyle, "mixed");
  assert.equal(s.fontSize, undefined);
  assert.equal(p.fontSize, "mixed");
  assert.equal(p.fontName, "mixed");
  assert.equal(b.cornerRadius, undefined);
  assert.deepEqual([b.topLeftRadius, b.topRightRadius, b.bottomRightRadius, b.bottomLeftRadius], [1, 2, 3, 4]);
  JSON.stringify(s); // a symbol left in the snapshot would vanish silently from the JSON
  assert.ok(![s, p, b].some(layer => Object.values(layer).includes(mixed)));
});

test("mixed text fills that differ are reported", () => {
  const navbar = tagged("Navbar link");
  child(variant(navbar, "State=Hover"), "Label").fills = [{ type: "MIXED" }];
  const h = hover(navbar, "navbar-link");
  assert.deepEqual(h.notTranslated.map(n => [n.layer, n.property]), [["Label", "fill"]]);
});

// ---- final review: auto layout, rotation, token values, duplicate names ----

// A set "Chip" whose State=Default hovers to State=Hover; rest and hovered are merged into each root.
const chipSet = (rest, hovered, name = "Chip", id = "5") => {
  const transition = { type: "SMART_ANIMATE", duration: 0.2, easing: { type: "EASE_OUT" } };
  const variantOf = (n, state, layer, reactions) => ({
    id: `${id}:${n}`, name: `State=${state}`, variantProperties: { State: state }, reactions,
    layer: Object.assign({ name: `State=${state}`, type: "COMPONENT", x: 0, y: 0, width: 100, height: 40, rotation: 0, opacity: 1, fills: [], strokes: [] }, layer),
  });
  const hoverTo = [{ trigger: { type: "ON_HOVER" }, actions: [{ type: "NODE", destinationId: `${id}:3`, navigation: "CHANGE_TO", transition }] }];
  return { id: `${id}:1`, name, variants: [variantOf(2, "Default", rest, hoverTo), variantOf(3, "Hover", hovered, [])] };
};
const dot = extra => Object.assign({ name: "Dot", type: "RECTANGLE", x: 10, y: 10, width: 40, height: 20, rotation: 0, opacity: 1, fills: [], strokes: [] }, extra);
const chip = (rest, hovered) => hoversOf(chipSet(rest, hovered)).hovers[0];
const DOT = '[data-motion-part="dot"]';
const FOLLOWS = "follows auto layout";

test("a layer auto layout places is not translated", () => {
  const h = chip(
    { layoutMode: "HORIZONTAL", children: [dot({ layoutPositioning: "AUTO" })] },
    { layoutMode: "HORIZONTAL", children: [dot({ layoutPositioning: "AUTO", x: 30 })] });
  assert.deepEqual(h.changes, []);
  assert.deepEqual(h.notTranslated, [{ layer: "Dot", property: "translate", reason: FOLLOWS }]);
});

test("an absolute layer in auto layout, or a layer outside it, translates", () => {
  const moved = { property: "translate", from: { value: "0px 0px" }, to: { value: "20px 0px" } };
  const absolute = chip(
    { layoutMode: "VERTICAL", children: [dot({ layoutPositioning: "ABSOLUTE" })] },
    { layoutMode: "VERTICAL", children: [dot({ layoutPositioning: "ABSOLUTE", x: 30 })] });
  assert.deepEqual(absolute.changes, [Object.assign({ layer: "Dot", part: DOT }, moved)]);
  const free = chip({ layoutMode: "NONE", children: [dot()] }, { layoutMode: "NONE", children: [dot({ x: 30 })] });
  assert.deepEqual(free.changes, [Object.assign({ layer: "Dot", part: DOT }, moved)]);
  assert.deepEqual(free.notTranslated, []);
});

test("a size on a hug or fill axis follows auto layout; a fixed one comes across", () => {
  const h = chip(
    { layoutMode: "HORIZONTAL", layoutSizingHorizontal: "HUG", children: [
      dot({ layoutSizingHorizontal: "FIXED", layoutSizingVertical: "FILL" })] },
    { layoutMode: "HORIZONTAL", layoutSizingHorizontal: "HUG", width: 140, children: [
      dot({ layoutSizingHorizontal: "FIXED", layoutSizingVertical: "FILL", width: 80, height: 30 })] });
  assert.deepEqual(h.changes, [{ layer: "Dot", part: DOT, property: "width", from: { value: "40px" }, to: { value: "80px" } }]);
  assert.deepEqual(h.notTranslated, [
    { property: "width", reason: FOLLOWS },
    { layer: "Dot", property: "height", reason: FOLLOWS },
  ]);
});

test("a changed auto layout setting is reported", () => {
  const h = chip({ layoutMode: "HORIZONTAL" }, { layoutMode: "VERTICAL" });
  assert.deepEqual(h.notTranslated, [{ property: "layoutMode", reason: "is not animated" }]);
});

test("a layer turned about its centre rotates and does not translate", () => {
  // At rest the Dot's centre is (30, 20). Turned 90deg anticlockwise about it, its top-left corner is
  // at (20, 40): x + 20cos90 + 10sin90 = 30 and y - 20sin90 + 10cos90 = 20.
  const turned = chip({ children: [dot()] }, { children: [dot({ x: 20, y: 40, rotation: 90 })] });
  assert.deepEqual(turned.changes, [{ layer: "Dot", part: DOT, property: "rotate", from: { value: "0deg" }, to: { value: "-90deg" } }]);
  assert.deepEqual(turned.notTranslated, []);
  const moved = chip({ children: [dot()] }, { children: [dot({ x: 25, y: 40, rotation: 90 })] });
  assert.deepEqual(moved.changes.map(c => [c.property, c.to.value]), [["rotate", "-90deg"], ["translate", "5px 0px"]]);
});

test("a token carries the value Figma resolves it to", () => {
  const corners = { topLeftRadius: "nacara/size/radius", topRightRadius: "nacara/size/radius", bottomRightRadius: "nacara/size/radius", bottomLeftRadius: "nacara/size/radius" };
  const h = chip(
    { cornerRadius: 6, boundVariables: corners, fills: [{ type: "SOLID", color: { r: 1, g: 0, b: 0 }, opacity: 0.5, variable: "nacara/color/primary" }] },
    { cornerRadius: 10, fills: [{ type: "SOLID", color: { r: 0, g: 0, b: 1 }, opacity: 1 }] });
  assert.deepEqual(h.changes, [
    { property: "background-color", from: { token: "nacara.color.primary", value: "#ff000080" }, to: { value: "#0000ff" } },
    { property: "border-radius", from: { token: "nacara.size.radius", value: "6px" }, to: { value: "10px" } },
  ]);
});

test("two hovers with one name: the second is skipped, naming the first's set", () => {
  const first = chipSet({ opacity: 1 }, { opacity: 0.5 }, "Promo card", "6");
  const second = chipSet({ opacity: 1 }, { opacity: 0.8 }, "Promo-card", "7");
  const document = motionDocument([first, second]);
  assert.deepEqual(document.hovers.map(h => [h.name, h.changes[0].to.value]), [["promo-card", "0.5"]]);
  assert.deepEqual(document.skipped, [{
    component: "Promo-card", variant: "State=Default",
    reason: 'Promo card already has a hover named "promo-card"; give this set another motion name.',
  }]);
});

test("the panel shows the markup of each part that comes across", () => {
  // ui.html's script, run against a document of plain objects that keep their text and children.
  const vm = require("node:vm");
  const html = require("node:fs").readFileSync(require("node:path").join(__dirname, "ui.html"), "utf8");
  const script = html.slice(html.indexOf("<script>") + "<script>".length, html.lastIndexOf("</script>"));
  const make = tag => ({
    tag, children: [], textContent: "",
    appendChild(c) { this.children.push(c); return c; },
    append(...cs) { this.children.push(...cs); },
    replaceChildren() { this.children = []; },
  });
  const byId = new Map();
  const document = {
    getElementById: id => byId.get(id) || byId.set(id, make("div")).get(id),
    createElement: make,
    createDocumentFragment: () => make("#fragment"),
  };
  const context = { document, parent: { postMessage() {} } };
  vm.runInNewContext(script, context);
  const card = set("Card");
  const nested = opacity => [{ name: "Dot", type: "ELLIPSE", x: 0, y: 0, width: 4, height: 4, rotation: 0, opacity, fills: [], strokes: [] }];
  child(variant(card, "Size=Small, State=Default"), "Accent").children = nested(1);
  child(variant(card, "Size=Small, State=Hover"), "Accent").children = nested(0.5);
  context.onmessage({ data: { pluginMessage: { type: "motion-scanned", result: { document: motionDocument([card]), sets: [] } } } });
  const text = node => [node.textContent, ...node.children.map(text)].join("\n");
  const shown = text(byId.get("motion-hovers"));
  assert.match(shown, /Accent \(data-motion-part="accent"\): width: 40px → 80px/);
  assert.match(shown, /background-color: nacara\.color\.bg-raised \(#ffffff\) → #f7f7ff/);
  assert.match(shown, /Accent\/Dot \(data-motion-part="dot" inside data-motion-part="accent"\): opacity: 1 → 0.5/);
});
