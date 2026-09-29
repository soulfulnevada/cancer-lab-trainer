#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const candidate = process.argv[2];
if (!candidate) {
  throw new Error("Usage: node web/tests/pwa_cache_contract.js <immutable-release-directory>");
}

const release = path.resolve(candidate);
const worker = fs.readFileSync(path.join(release, "sw.js"), "utf8");
const match = worker.match(/const ASSETS = (\[[^;]+\]);/);
if (!match) {
  throw new Error("Service worker does not expose a literal ASSETS cache list.");
}
const assets = JSON.parse(match[1]);
const unique = new Set(assets);
if (unique.size !== assets.length) {
  throw new Error("Service worker cache list contains duplicate canonical URLs.");
}

const manifest = JSON.parse(fs.readFileSync(path.join(release, "asset-manifest.json"), "utf8"));
for (const relative of manifest) {
  const cacheUrl = `./${relative}`;
  if (!unique.has(cacheUrl)) {
    throw new Error(`Emitted asset is missing from the offline cache: ${relative}`);
  }
  if (!fs.statSync(path.join(release, relative)).isFile()) {
    throw new Error(`Manifest asset is not an emitted file: ${relative}`);
  }
}

for (const required of ["./", "./asset-manifest.json", "./release-manifest.json", "./sw.js", "./offline.html"]) {
  if (!unique.has(required)) {
    throw new Error(`Required offline-cache URL is missing: ${required}`);
  }
}

const emitted = fs.readdirSync(release, { recursive: true })
  .filter(entry => fs.statSync(path.join(release, entry)).isFile())
  .map(entry => entry.split(path.sep).join("/"));
for (const relative of emitted) {
  if (!unique.has(`./${relative}`)) {
    throw new Error(`Release file is missing from the offline cache: ${relative}`);
  }
}

console.log(`PASS: offline cache contract covers ${assets.length} unique URLs and ${emitted.length} emitted files`);
