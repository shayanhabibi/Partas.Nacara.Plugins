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
