# Web publishing readiness — 2026-09-28

## Decision and boundary

The desktop C# Godot project remains ineligible for direct web export. The browser candidate is a separate standard-GDScript Godot 4.7.2 project under `web/`; its model is the authority for browser learner actions, state, reports, and saves. Desktop executables, application configuration, packages, saved learner records, and local evidence are outside the browser candidate and public staging scope. The public parity stage intentionally includes the pure C# simulation reference, `PipettingQualityTeaching.cs`, `Level2ClaimDisplay.cs`, and the test/oracle projects so the browser model can be independently checked; it does not include the desktop executable or desktop project configuration.

The candidate is an offline, fictional teaching simulation. It is not a laboratory protocol, calibrated instrument model, clinical tool, proof of hands-on competence, or prediction of a real cancer response. Browser attempts use the `cancer-lab-trainer:web:v1` IndexedDB origin only. They are not desktop saves and are not imported from desktop records.

## Current readiness

| Area | Result | Evidence and limit |
| --- | --- | --- |
| Separate GDScript model parity | PASS | The checked model seams compare complete snapshots, reports, feedback, action ledgers, replay, rejected actions, and persistence contracts against the C# oracle. The local generated oracle is verified against `tests/parity/csharp-oracle.sha256`; the generated JSON itself stays ignored. |
| Browser learner flow | PASS (targeted local browser checks) | On immutable `web-explain-fix-20260929`, local in-app Chromium completed four server-stopped keyboard sessions: Level 1 Guided normal, Level 1 Assessment recovery/escalation, Level 2 Guided main run plus Case C, and Level 2 Assessment main run plus Case B. It also renewed Guided label-mismatch correction/checkpoint retry, save/resume, scoped deletion, original-prediction versus revision display, Explain focus/unique IDs, downloads, and 800×600 layout. The detailed local evidence stays under `.scratch/`; these checks do not establish every browser, assistive technology, or device result. |
| Screen-reader validation | PENDING manual operator session | A local Narrator executable is available, while standard NVDA locations were absent during the local inventory. No Narrator or NVDA session has been run. The [Narrator check script](web-narrator-checklist.md) is ready for a named immutable candidate; its absence is an evidence gap, not a claim that a screen reader is unavailable. |
| Immutable web candidate build | PASS | `scripts/web/build-web.ps1` rejects an existing release ID, validates standard Godot 4.7.2, compares all eight canonical data hashes, inventories the exported PCK, writes relative release and cache manifests, and writes a ZIP plus SHA-256 sidecar. Exporter timestamps and compression mean this is a rebuild recipe, not a byte-for-byte reproducibility claim. |
| Offline cache implementation | PASS (targeted local browser check) | The `web-explain-fix-20260929` service worker reported a complete cache. After its local server stopped, the landing and first game load both succeeded, and the four targeted sessions plus CSV/HTML (and Level 2 JSON) exports completed offline. Cache eviction, other browsers, and future release cache behavior remain unverified. |
| Public-source staging | PASS (local staging) | `scripts/web/stage-public.py` copies only the files listed in `scripts/web/public-source-allowlist.json`, writes hashes, rejects local absolute path markers, and excludes learner records, packages, caches, generated oracle JSON, and temporary evidence. This local stage is not publication. |
| Licenses and notices | PASS (source attribution) | Project `LICENSE` is MIT. `NOTICE` and `THIRD_PARTY_NOTICES.md` point to full Godot copyright and third-party notices plus the retained .NET Foundation MIT/notices for the derived seeded-random reference behavior. |
| CI and Pages configuration | CONFIGURED, unexecuted remotely | `.github/workflows/web.yml` pins setup actions, checks models and web seams, builds/stages an immutable candidate, and deploys only on a future push to `main`. No workflow has been committed, pushed, or run remotely from this record. |
| Publication | BLOCKED | The intended GitHub account is `soulfulnevada`. Publishing, repository visibility, Pages activation, and any push require explicit final approval. |

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

## Required gates before publication

1. Review the final allowlisted stage and its manifest, including notices and science citations. Do not use root-level `git add .`.
2. Complete the pending manual screen-reader operator session or retain it as an explicit evidence gap.
3. Run the configured Linux CI after an approved commit/push; it has not run remotely from this local record.
4. Confirm intended repository visibility for `soulfulnevada` and obtain explicit final approval before any commit, push, Pages activation, or publication.
