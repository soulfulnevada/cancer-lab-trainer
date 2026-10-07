# Automated browser playthrough and accessibility scan — 2026-10-06

**Scope:** automated evidence that each learner journey in the web build can be completed, and that the captured screens pass an automated WCAG 2.1 A/AA scan. This is not a usability study, a screen-reader session, a learner pilot, or evidence that learners understand the content.

## What runs

`tools/playthrough/run.mjs` serves a local web build and drives the installed Microsoft Edge (headless, SwiftShader WebGL) through `playwright-core`. Each journey starts from a fresh browser profile, clicks task-panel controls by their accessible names, saves a screenshot at every phase change, and runs axe-core on every captured screen.

| Journey | What it exercises | Checked at the end |
| --- | --- | --- |
| Level 1 Guided, valid run | Preparation, a manual pipetting cycle, Repeat cycle, tip changes, reader, limited conclusion, follow-up question, closeout, four check questions (one answered wrongly) | Attempt complete; limited conclusion recorded; "4 of 5" score; FICTIONAL DATA label |
| Level 1 Assessment, invalid run | Vehicle well B4 skipped; unsupported conclusion recorded; escalation; questions | Attempt complete; percentage shows "Cannot calculate"; escalation is the outcome; score shown |
| Level 2 Guided, Case A | Tubes, plate assignment, eight predictions, observations, explanations, case evidence, claims, closeout | Attempt complete; case cause disclosed in debrief |
| Level 2 Assessment, Case D | Same path in Assessment with different claims | Attempt complete; case debrief shown |
| Keyboard only | Start Level 1 from the keyboard, then Enter through bench preparation and traceability | Focus lands on the next task control each time; phase reaches Transfers |
| Landing page | Static page | Accessibility scan only |

Any uncaught page error fails the journey.

## Result on this date

Web build `1.2.1-web.4`, Microsoft Edge 154 headless: all five journeys passed on two consecutive runs; 38 screenshots; **0 axe-core WCAG 2.1 A/AA violations** on the captured screens.

The run found and led to these fixes, now in the build:

- Plate-assignment and Lab Detective claim buttons had accessible names that did not contain their visible text (`label-content-name-mismatch`, serious). Names now start from the visible text's context, for example "P1 · T / LB" and "Uncertainty: Not sure".
- An intermittent page error: the loading-progress callback could run after the loading bar was removed. It now checks that the bar exists.

The same playthrough was then run against the deployed GitHub Pages site (`--url`, build `1.2.1-web.4` served): all five journeys passed with 0 axe-core violations. Its menu screenshot showed the enlarged Level 2 plates overlapping the Level 1 plate on the level menu; `1.2.1-web.5` shows only the Level 1 bench on the menu.

## How to rerun

```
cd tools/playthrough
npm ci
node run.mjs --build ../../dist/web/<release-id>/game
```

The report is written to `dist/playthrough/index.html` with `summary.json` and the screenshots beside it. `--url https://soulfulnevada.github.io/cancer-lab-trainer/` runs it against the deployed site instead of a local build. `--headed` shows the browser. The exit code is non-zero if any journey fails.

## Still needed from people

1. **Screen-reader session** — follow `docs/validation/web-narrator-checklist.md` with Narrator or NVDA on a named build and record the results. Automated scans cover only part of accessibility.
2. **Learner pilot** — 3–5 people from the target group play Level 1 Guided once. The five check questions and their first answers are recorded in each attempt and in the CSV/HTML downloads, so results can be collected without extra tooling. Collect only the downloads learners choose to share.
3. **Qualified re-review** — the reviewer receives the current build link and this playthrough report, and reviews the check-question wording in `data/rules.v1.json` and the claim register in `docs/science-and-sources.md`.
