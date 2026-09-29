# Web publishing readiness — 2026-09-29

## Decision and boundary

The desktop C# Godot project remains ineligible for direct web export. The browser candidate is a separate standard-GDScript Godot 4.7.2 project under `web/`; its model is the authority for browser learner actions, state, reports, and saves. Desktop executables, application configuration, packages, saved learner records, and local evidence are outside the browser candidate and public staging scope. The public parity stage intentionally includes the pure C# simulation reference, `PipettingQualityTeaching.cs`, `Level2ClaimDisplay.cs`, and the test/oracle projects so the browser model can be independently checked; it does not include the desktop executable or desktop project configuration.

The candidate is an offline, fictional teaching simulation. It is not a laboratory protocol, calibrated instrument model, clinical tool, proof of hands-on competence, or prediction of a real cancer response. Browser attempts use the `cancer-lab-trainer:web:v1` IndexedDB origin only. They are not desktop saves and are not imported from desktop records.

## Current readiness

| Area | Result | Evidence and limit |
| --- | --- | --- |
| Separate GDScript model parity | PASS | CI run `36602564992` passed the 38 Level 1 C# contracts, Transformation contracts, the LF-canonical C# oracle (`67323791FDE034977EDA755C444E51B0A1EB885A0ADCF9873DF3189E08569A4F`), Level 1 full snapshot/report parity, and all 8 Level 2 traces with 448 action checkpoints. The generated oracle JSON stays ignored. |
| Browser learner flow | PASS (targeted live-browser checks) | At the Pages URL, in-app Chromium completed Guided and Assessment sessions for both levels. The checks covered normal and recovery/escalation paths, save/resume, scoped deletion with Cancel/Escape focus return, downloads, prediction revision retention, keyboard operation, mouse start/delete, and 800×600 layout without horizontal overflow. Console warnings and errors were empty. These checks do not establish every browser, assistive technology, device, or learner result. |
| Screen-reader validation | PENDING manual operator session | A local Narrator executable is available, while standard NVDA locations were absent during the local inventory. No Narrator or NVDA session has been run. The [Narrator check script](web-narrator-checklist.md) is ready for a named immutable candidate; its absence is an evidence gap, not a claim that a screen reader is unavailable. |
| Immutable web candidate build | PASS | `scripts/web/build-web.ps1` rejects an existing release ID, validates standard Godot 4.7.2, compares all eight canonical data hashes, inventories the exported PCK, writes relative release and cache manifests, and writes a ZIP plus SHA-256 sidecar. Exporter timestamps and compression mean this is a rebuild recipe, not a byte-for-byte reproducibility claim. |
| Offline cache implementation | PASS (live cache ready; disconnected live run not renewed) | The live-origin service worker reported a complete cache. A prior local server-stopped check covered landing, first game load, four targeted sessions, and exports. Cache eviction, other browsers, future release cache behavior, and a disconnected live-origin run remain unverified. |
| Public-source staging | PASS (published source and CI staging) | `scripts/web/stage-public.py` copies only the files listed in `scripts/web/public-source-allowlist.json`, writes hashes, rejects local absolute path markers, and excludes learner records, packages, caches, generated oracle JSON, and temporary evidence. CI ran the staged-checkout contract before deployment. |
| Licenses and notices | PASS (source attribution) | Project `LICENSE` is MIT. `NOTICE` and `THIRD_PARTY_NOTICES.md` point to full Godot copyright and third-party notices plus the retained .NET Foundation MIT/notices for the derived seeded-random reference behavior. |
| CI and Pages deployment | PASS | CI run `36602564992` deployed commit `4a55fa981b58c92f29423ab1e0b400bf75056190` to [GitHub Pages](https://soulfulnevada.github.io/cancer-lab-trainer/). It passed the reference tests, production seams, staging, cache contract, and build before deployment. The 25 emitted release-manifest files were downloaded and hash-checked from the live URL. |
| Publication | LIVE | The public repository and Pages release are live at [soulfulnevada/cancer-lab-trainer](https://github.com/soulfulnevada/cancer-lab-trainer) and [the browser simulation](https://soulfulnevada.github.io/cancer-lab-trainer/). This publication does not establish laboratory competence or workplace readiness. |

## Build and staging commands

Use a standard non-Mono Godot 4.7.2 executable. The no-thread web templates are optional portable inputs; pass both paths or set both matching environment variables. They are never stored as machine-specific paths in the tracked preset.

```powershell
./scripts/web/build-web.ps1 `
  -ReleaseId local-web-candidate `
  -GodotPath 'C:\path\to\Godot_v4.7.2-stable_console.exe' `
  -WebTemplateDebug 'C:\path\to\web_nothreads_debug.zip' `
  -WebTemplateRelease 'C:\path\to\web_nothreads_release.zip'

python ./scripts/web/stage-public.py --release-id local-public-source-review
```

The build output is `dist/web/<release-id>/`; its root has the landing page, generated service worker, cache list, PCK inventory, release manifest, and `game/` runtime. `dist/` is ignored. The public-source review is separately written to `dist/public-stage/<release-id>/` and must be reviewed by file before any source is made public.

## Post-publication evidence limits

1. Complete a manual screen-reader operator session or retain it as an explicit evidence gap.
2. Test other browsers, representative devices, cache eviction, and a disconnected live-origin session before making broader compatibility claims.
3. Obtain qualified scientific, trainee, target-device, and physical-comparison review before any workplace-use claim. Publication does not remove those holds.
