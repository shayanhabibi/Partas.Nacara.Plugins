// The motion section of code.js against a real capture: node --test design/figma-plugin/motion.test.js

const test = require("node:test");
const assert = require("node:assert/strict");
const { motionName, hoversOf, motionDocument, applyTags, THEME_SELECTORS } = require("./code.js");
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
    { property: "background-color", from: { value: "transparent" }, to: { token: "nacara.color.bg-subtle" } },
    { property: "color", from: { token: "nacara.color.text-muted" }, to: { token: "nacara.color.text" } },
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
    { property: "background-color", from: { value: "transparent" }, to: { token: "nacara.color.bg-subtle" } },
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
    { property: "background-color", from: { token: "nacara.color.bg-raised" }, to: { value: "#f7f7ff" } },
    { property: "border-radius", from: { token: "nacara.size.radius" }, to: { value: "12px" } },
    { property: "box-shadow", from: { value: "none" }, to: { token: "nacara.shadow.shadow" } },
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
