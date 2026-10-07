// Plays both levels in both modes against a built web candidate, saving a screenshot at every
// phase change and running an axe-core accessibility scan on each screen it captures.
// Usage: node run.mjs [--build <game dir> | --url <deployed site root>] [--out <dir>] [--headed]
// Drives the locally installed Microsoft Edge through playwright-core; no browser download.
import { chromium } from 'playwright-core';
import axe from 'axe-core';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const option = (name, fallback) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : fallback; };
const buildDir = path.resolve(option('--build', path.join(repo, 'dist', 'web', 'preview')));
const siteDir = path.join(repo, 'web', 'site');
const outDir = path.resolve(option('--out', path.join(repo, 'dist', 'playthrough')));
const headed = process.argv.includes('--headed');
const siteUrl = option('--url', '');
if (!siteUrl && !fs.existsSync(path.join(buildDir, 'index.pck'))) { console.error(`No web build at ${buildDir}. Build one first.`); process.exit(2); }
fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(path.join(outDir, 'shots'), { recursive: true });

// Static server: /game/ is the build, / is the landing site.
const types = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.pck': 'application/octet-stream', '.png': 'image/png', '.css': 'text/css', '.json': 'application/json', '.webmanifest': 'application/manifest+json' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const [root, rel] = url.startsWith('/game/') ? [buildDir, url.slice(6)] : [siteDir, url.slice(1)];
  let file = path.join(root, rel || 'index.html');
  if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!fs.existsSync(file)) { res.writeHead(404).end(); return; }
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(file).pipe(res);
});
// A deployed site (--url) has the same layout: landing page at the root, game under /game/.
if (!siteUrl) await new Promise(r => server.listen(0, '127.0.0.1', r));
const base = siteUrl ? siteUrl.replace(/\/$/, '') : `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch({ channel: 'msedge', headless: !headed, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const shots = [];
const violations = new Map();
const flows = [];

async function scan(page, where) {
  if (!(await page.evaluate(() => typeof window.axe !== 'undefined'))) await page.addScriptTag({ content: axe.source });
  const result = await page.evaluate(() => window.axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } }));
  for (const v of result.violations) {
    const entry = violations.get(v.id) || { id: v.id, impact: v.impact, help: v.help, helpUrl: v.helpUrl, screens: new Set(), examples: new Set() };
    entry.screens.add(where);
    v.nodes.slice(0, 3).forEach(n => entry.examples.add(n.html.slice(0, 160)));
    violations.set(v.id, entry);
  }
}

async function snapshot(page, flow, caption) {
  const n = String(shots.length + 1).padStart(3, '0');
  const file = `shots/${n}-${flow.id}.png`;
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(outDir, file) });
  const info = await page.evaluate(() => ({
    phase: document.querySelector('.phase')?.textContent || 'Menu',
    next: document.querySelector('.next')?.textContent || '',
    feedback: (document.querySelector('.feedback')?.textContent || '').split('\n')[0]
  }));
  shots.push({ flow: flow.id, file, caption, ...info });
  await scan(page, `${flow.title}: ${caption}`);
}

// Clicks the first enabled task-panel button whose accessible name starts with the label.
async function press(page, label) {
  const ok = await page.evaluate(l => {
    const name = b => (b.getAttribute('aria-label') || b.textContent).trim().replace(/\s+/g, ' ');
    const enabled = [...document.querySelectorAll('main button')].filter(x => !x.disabled);
    const b = enabled.find(x => name(x) === l) || enabled.find(x => name(x).startsWith(l));
    if (b) b.click();
    return !!b;
  }, label);
  if (!ok) throw new Error(`No enabled control named "${label}"`);
  await page.waitForTimeout(250);
}

async function runFlow(flow) {
  const record = { id: flow.id, title: flow.title, steps: 0, ok: false, error: '' };
  flows.push(record);
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  try {
    await page.goto(`${base}/game/index.html`);
    await page.waitForSelector('#start-level1-guided', { timeout: 120000 });
    await snapshot(page, flow, 'Level menu');
    await page.click(`#start-${flow.level}-${flow.mode === 'GuidedPractice' ? 'guided' : 'assessment'}`);
    await page.waitForTimeout(500);
    let phase = '';
    for (const step of flow.steps) {
      if (step.startsWith('@')) { await page.click('#' + step.slice(1)); await page.waitForTimeout(250); }
      else await press(page, step);
      record.steps++;
      const now = await page.evaluate(() => document.querySelector('.phase')?.textContent || '');
      if (now !== phase) { phase = now; await snapshot(page, flow, `${now} (after "${step.replace('@', '')}")`); }
    }
    for (const [check, message] of flow.expect) {
      if (!(await page.evaluate(check))) throw new Error(`Expectation failed: ${message}`);
    }
    await page.evaluate(() => { const d = document.querySelector('.card h3#debrief-heading'); if (d) d.scrollIntoView(); });
    await snapshot(page, flow, 'Completed debrief');
    if (errors.length) throw new Error(`Page errors: ${errors.join(' | ')}`);
    record.ok = true;
  } catch (e) {
    record.error = e.message;
    await page.screenshot({ path: path.join(outDir, `shots/FAILED-${flow.id}.png`) }).catch(() => {});
  }
  await context.close();
  console.log(`${record.ok ? 'PASS' : 'FAIL'} ${flow.title} (${record.steps} actions)${record.error ? ': ' + record.error : ''}`);
}

const cycle = w => ['Press first stop in air', 'Move to selected source', 'Release slowly while immersed', 'Position at ' + w, 'Deliver at first stop', 'Clear residue at second stop', 'Withdraw and release in air'];
const prep = ['Wear PPE', 'Disinfect bench', 'Check materials', 'Verify labels', 'Review plate map'];
const reader = ['Eject the used tip', 'Load plate', 'Configure luminescence reader', 'Run fictional reader', 'Review recorded results'];
const closeout = ['Record waste decision', 'Record bench cleaning', 'Record handoff'];
const level2Setup = ['Acknowledge introduction', 'Label tube T', 'Label tube NC', 'Use fresh conceptual tip', 'Choose DNA source', 'Choose T destination', 'Record actual tube content', 'Use fresh conceptual tip', 'Choose Buffer source', 'Choose NC destination', 'Record actual tube content',
  'P1 · T / LB+amp', 'P2 · NC / LB+amp', 'P3 · T / LB+amp+Ara', 'P4 · NC / LB',
  'P1 growth: Present', 'P1 fluorescence: Not detected', 'P2 growth: Absent', 'P2 fluorescence: No colonies', 'P3 growth: Present', 'P3 fluorescence: Detected', 'P4 growth: Present', 'P4 fluorescence: Not detected',
  'Advance conceptual time', ...['P1', 'P2', 'P3', 'P4'].flatMap(p => [p + ' normal view', p + ' excitation view']),
  'P1: Non-green does not prove no DNA', 'P2: No colonies: not assessable', 'P3: Green is consistent with GFP', 'P4: Non-green does not prove no DNA'];
const caseSteps = id => ['@start-case-' + id, '@start-case-' + id, ...['P1', 'P2', 'P3', 'P4'].flatMap(p => [p + ' normal view', p + ' excitation view']), 'View setup record', 'View control comparison', 'View observation limit'];
const phaseIs = p => `document.querySelector('.phase')?.textContent === '${p}'`;
const mainHas = t => `document.querySelector('main').innerText.includes(${JSON.stringify(t)})`;

await runFlow({ id: 'l1-guided', title: 'Level 1 Guided Practice (valid run)', level: 'level1', mode: 'GuidedPractice',
  steps: [...prep, 'Attach fresh tip', 'Set target volume', 'Select Blank', ...cycle('B1'), 'Repeat cycle', 'Eject the used tip', 'Attach fresh tip', 'Select Vehicle control', 'Repeat cycle', 'Repeat cycle', 'Eject the used tip', 'Attach fresh tip', 'Select Fictional treatment', 'Repeat cycle', 'Repeat cycle', ...reader,
    'Record limited supported conclusion', 'Answer confirm-observation: Repeat with independent', ...closeout,
    'Answer atp-limits: The fictional signal', 'Answer controls: Blanks estimate', 'Answer replicates: Biological', 'Answer matched-wells: The ATP assay'],
  expect: [[phaseIs('Complete'), 'attempt completes'], [mainHas('supports an observed viability-associated difference only'), 'limited conclusion is recorded'], [mainHas('4 of 5 answered correctly'), 'check score is shown'], [mainHas('FICTIONAL DATA'), 'results are labelled fictional']] });

await runFlow({ id: 'l1-assessment', title: 'Level 1 Assessment (missing vehicle well, escalated)', level: 'level1', mode: 'Assessment',
  steps: [...prep, 'Attach fresh tip', 'Set target volume', 'Select Blank', ...cycle('B1'), ...cycle('B2'), 'Eject the used tip', 'Attach fresh tip', 'Select Vehicle control', ...cycle('B3'), 'Eject the used tip', 'Attach fresh tip', 'Select Fictional treatment', ...cycle('B5'), ...cycle('B6'), ...reader,
    'Record limited supported conclusion', 'Escalate this run', 'Answer confirm-observation: Read the same plate', ...closeout,
    'Answer atp-limits: The fictional signal', 'Answer controls: Blanks estimate', 'Answer replicates: Technical', 'Answer matched-wells: The ATP assay'],
  expect: [[phaseIs('Complete'), 'attempt completes'], [mainHas('Cannot calculate'), 'percentage is withheld for an invalid reference'], [mainHas('Run was escalated for review'), 'escalation is the recorded outcome'], [mainHas('4 of 5 answered correctly'), 'check score is shown']] });

await runFlow({ id: 'l2-guided', title: 'Level 2 Guided Practice (Case A)', level: 'level2', mode: 'GuidedPractice',
  steps: [...level2Setup, ...caseSteps('missing-arabinose'), 'Selection claim: Supported', 'Expression claim: Withheld', 'Uncertainty: The phenotype cannot prove', 'Record simulated cleanup', 'Record waste decision', 'Record debrief note', 'Complete debrief'],
  expect: [[phaseIs('Complete'), 'attempt completes'], [mainHas('induction component was missing'), 'case cause is disclosed in the debrief']] });

await runFlow({ id: 'l2-assessment', title: 'Level 2 Assessment (Case D)', level: 'level2', mode: 'Assessment',
  steps: [...level2Setup, ...caseSteps('failed-antibiotic-negative-control'), 'Selection claim: Withheld', 'Expression claim: Withheld', 'Uncertainty: The phenotype cannot prove', 'Record simulated cleanup', 'Record waste decision', 'Record debrief note', 'Complete debrief'],
  expect: [[phaseIs('Complete'), 'attempt completes'], [mainHas('Case D'), 'case debrief is shown']] });

// Keyboard only: focus the start control, then Enter through bench preparation and traceability.
{
  const record = { id: 'keyboard', title: 'Keyboard only (Level 1 preparation)', steps: 0, ok: false, error: '' };
  flows.push(record);
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  try {
    await page.goto(`${base}/game/index.html`);
    await page.waitForSelector('#start-level1-guided', { timeout: 120000 });
    await page.focus('#start-level1-guided');
    await page.keyboard.press('Enter'); record.steps++;
    await page.waitForTimeout(500);
    const pressed = [];
    for (let i = 0; i < 5; i++) {
      await page.waitForTimeout(300);
      pressed.push(await page.evaluate(() => document.activeElement?.textContent?.trim() || '(nothing focused)'));
      await page.keyboard.press('Enter'); record.steps++;
    }
    await page.waitForTimeout(400);
    const phase = await page.evaluate(() => document.querySelector('.phase')?.textContent);
    await snapshot(page, record, `After Enter on: ${pressed.join(', ')}`);
    if (phase !== 'Transfers') throw new Error(`Expected Transfers after keyboard preparation, got ${phase}; focused controls were ${pressed.join(', ')}`);
    record.ok = true;
  } catch (e) { record.error = e.message; }
  await context.close();
  console.log(`${record.ok ? 'PASS' : 'FAIL'} ${record.title} (${record.steps} key presses)${record.error ? ': ' + record.error : ''}`);
}

// Landing page.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${base}/index.html`);
  await page.waitForTimeout(800);
  const flow = { id: 'landing', title: 'Landing page' };
  await snapshot(page, flow, 'Landing page');
  await context.close();
}
const version = browser.version();
await browser.close();
if (!siteUrl) server.close();

// Report.
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const allOk = flows.every(f => f.ok);
const a11y = [...violations.values()].map(v => ({ ...v, screens: [...v.screens], examples: [...v.examples] }));
const summary = { generated: new Date().toISOString(), browser: `Microsoft Edge ${version} (headless, SwiftShader WebGL)`, build: siteUrl || buildDir, flows, accessibility: a11y.map(v => ({ id: v.id, impact: v.impact, help: v.help, screens: v.screens.length })) };
fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify(summary, null, 2));
const flowHtml = [...flows, { id: 'landing', title: 'Landing page', ok: true, steps: 0 }].map(f => `<section><h2>${esc(f.title)} — ${f.ok ? 'completed' : 'FAILED'}</h2>${f.steps ? `<p>${f.steps} learner actions.${f.error ? ' ' + esc(f.error) : ''}</p>` : ''}<div class="grid">${shots.filter(s => s.flow === f.id).map(s => `<figure><a href="${s.file}"><img src="${s.file}" alt="${esc(f.title + ': ' + s.caption)}" loading="lazy"></a><figcaption><strong>${esc(s.caption)}</strong>${s.next ? '<br>' + esc(s.next) : ''}${s.feedback ? '<br><span>' + esc(s.feedback) + '</span>' : ''}</figcaption></figure>`).join('')}</div></section>`).join('');
const a11yHtml = a11y.length ? `<table><tr><th>Rule</th><th>Impact</th><th>Problem</th><th>Screens</th><th>Example</th></tr>${a11y.map(v => `<tr><td><a href="${esc(v.helpUrl)}">${esc(v.id)}</a></td><td>${esc(v.impact)}</td><td>${esc(v.help)}</td><td>${v.screens.length}</td><td><code>${esc(v.examples[0] || '')}</code></td></tr>`).join('')}</table>` : '<p>No WCAG 2.1 A/AA violations detected by axe-core on the captured screens.</p>';
fs.writeFileSync(path.join(outDir, 'index.html'), `<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Cancer Lab Trainer playthrough</title><style>body{font-family:system-ui;margin:2rem auto;max-width:1200px;padding:0 1rem;color:#14212b}h1{margin-bottom:.2rem}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:1rem}figure{margin:0;border:1px solid #c9d6de;border-radius:.4rem;overflow:hidden;background:#f6f9fb}img{width:100%;display:block}figcaption{padding:.5rem;font-size:.85rem}figcaption span{color:#4b5e6b}table{border-collapse:collapse;width:100%;font-size:.88rem}td,th{border:1px solid #c9d6de;padding:.4rem;text-align:left;vertical-align:top}code{font-size:.75rem;word-break:break-all}</style><h1>Cancer Lab Trainer — automated playthrough</h1><p>${allOk ? 'All learner journeys completed.' : 'One or more journeys FAILED.'} Generated ${esc(summary.generated)} with ${esc(summary.browser)}. Values shown are fictional training data.</p><p>This is automated evidence that each level can be completed and that the results and debrief screens render. It is not a usability study, a screen-reader test, or evidence that learners understand the content.</p><h2>Accessibility scan (axe-core, WCAG 2.1 A/AA)</h2>${a11yHtml}<p>Automated scans find only part of accessibility problems; the 3D canvas is decorative and every outcome is also shown as text in the task panel.</p>${flowHtml}`);
console.log(`${allOk ? 'PASS' : 'FAIL'}: report at ${path.join(outDir, 'index.html')} (${shots.length} screenshots, ${a11y.length} accessibility rule(s) flagged)`);
process.exit(allOk ? 0 : 1);
