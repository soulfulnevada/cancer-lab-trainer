#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const shell = fs.readFileSync(path.join(__dirname, "..", "web-template", "game-shell.html"), "utf8");

assert.doesNotMatch(
  shell,
  /RevisePrediction','P1','growth:NotSure','revise-prediction'/,
  "Explain must not emit the obsolete fixed-P1 revision action."
);
assert.doesNotMatch(
  shell,
  /function l2Extras\(/,
  "Level 2 Explain actions must have one renderer, not a second appended renderer."
);
assert.match(
  shell,
  /revise-growth-'\+id\+'-'\+v/,
  "The production renderer must retain learner-selected growth revisions for every plate."
);
assert.match(
  shell,
  /revise-fluoro-'\+id\+'-'\+v\[0\]/,
  "The production renderer must retain learner-selected fluorescence revisions for every plate."
);

console.log("PASS: production Level 2 Explain renderer has one learner-selected revision control set");
