// Prints the motion document of the real capture, tagged as the theme's sets are:
// node design/figma-plugin/motion-fixture.js. The F# tests parse its output.

const { motionDocument, applyTags, THEME_SELECTORS } = require("./code.js");
const capture = require("../../tests/Partas.Nacara.Plugins.Tests/figma-hover.json");

process.stdout.write(JSON.stringify(motionDocument(capture.componentSets.map(s => applyTags(s, THEME_SELECTORS))), null, 2));
