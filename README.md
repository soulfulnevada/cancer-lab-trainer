# Cancer Lab Trainer — Browser Edition

Cancer Lab Trainer is an offline, fictional teaching simulation for reasoning about controls, observations, and supported conclusions. The browser edition contains two independent learning levels:

- **Level 1 — ATP Viability Reasoning:** follow a six-well fictional workflow, inspect background-adjusted readings and replicate variation, then decide what the simulated controls support.
- **Level 2 — Transformation, Controls, and Gene Expression:** trace a fictional DNA-to-GFP concept model, compare qualitative plate observations, and investigate a fictional diagnostic case.

## Browser deployment

[Play Cancer Lab Trainer](https://soulfulnevada.github.io/cancer-lab-trainer/)

The Pages release is live. CI run `36602564992` deployed commit `4a55fa981b58c92f29423ab1e0b400bf75056190`; its emitted release manifest was checked against all 25 published asset hashes. Targeted live-browser checks completed Guided and Assessment sessions for both levels, including recovery, resume, scoped deletion, downloads, keyboard navigation, and a compact viewport.

This is focused browser evidence, not a claim of coverage across every browser, device, assistive technology, laboratory, or learning setting. The live service worker reported a ready cache; a disconnected live-origin session has not been repeated.

## Scope and scientific boundaries

The simulation is educational and fictional. It is not a laboratory protocol, calibrated instrument model, clinical tool, proof of hands-on competence, or prediction of a real cancer response.

Level 1 uses ATP-associated luminescence as a fictional viability-related metabolic signal. It does not prove cell death or identify a mechanism. Level 2 fluorescence supports simulated GFP expression only under the shown conditions. The data and responses are fictional instructional examples.

Guided Practice provides optional recovery and science context. Assessment defers coaching until debrief while preserving the same modeled outcomes and constraints.

## Build the browser candidate

Use a standard, non-Mono Godot 4.7.2 executable and matching no-thread web templates:

```powershell
./scripts/web/build-web.ps1 `
  -ReleaseId local-web-candidate `
  -GodotPath '<path-to-Godot-console-executable>' `
  -WebTemplateDebug '<path-to-web_nothreads_debug.zip>' `
  -WebTemplateRelease '<path-to-web_nothreads_release.zip>'
```

The immutable browser output is written to `dist/web/<release-id>/`. Browser attempts stay in IndexedDB for the current browser origin; the application has no account, telemetry, or learner-data transfer.

## Verify source and parity

```powershell
dotnet run --project tests/CancerLabTrainer.Tests.csproj
dotnet run --project tests/Transformation.Tests.csproj
./scripts/web/generate-parity-oracle.ps1
```

The browser models replay the generated C# oracle with complete snapshots, reports, feedback, action ledgers, persistence checks, and rejected actions. The generated oracle JSON is intentionally excluded from public source staging; its reviewed hash is retained in [the parity test notes](tests/parity/README.md).

## Sources and notices

Read the [science and source boundaries](docs/science-and-sources.md) and the [Level 2 source notes](docs/LEVEL2_SCIENCE_SOURCES.md). Project licensing is [MIT](LICENSE); [NOTICE](NOTICE) and [third-party notices](THIRD_PARTY_NOTICES.md) identify retained Godot and .NET notices.

© 2026 Jacob Carter. Cancer Lab Trainer is an independent educational simulation. It is not affiliated with or endorsed by cited organizations, and completion does not establish laboratory competence.
