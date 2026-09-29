# Cancer Lab Trainer

An offline, fictional training simulation with two independent learning levels: **Level 1 — ATP Viability Reasoning** and **Level 2 — Transformation, Controls, and Gene Expression**. It teaches conceptual controls and interpretation through simplified virtual workflows. It is **not a laboratory protocol, calibrated physical replica, clinical decision tool, or proof of hands-on competence**.

Level 1 has a paced 20–30 minute design target: prepare a workspace, verify a small active subset of a 96-well plate, make illustrative pipette transfers, configure a luminescence reader, interpret controls and replicate variation, and document closeout. Level 2 has a separate 15–20 minute design target: trace DNA to GFP expression, reason through transformation controls and plate observations, investigate one fictional diagnostic case, and document closeout. Neither target is a measured completion time.

## What is included

- Procedural 3D lab scene with an original plate reader, bench, pipette, and visible 96-well plate.
- Guided Practice and Assessment modes. Guided mode gives immediate recovery advice; Assessment holds the debrief until the end.
- Deterministic, versioned fictional data. The same scenario version and seed yield the same readings.
- State tracking for preparation, labels, nominal named-source quantities, explicit forward pipette stages, frozen in-flight destination wells, tips, plate orientation, reader mode, interpretation, waste, cleanup, and handoff.
- Local anonymous attempt files, deletion confirmation, CSV measurements, and an HTML debrief. Attempts use separate IDs and nothing is sent over a network.

## Play the Windows demo

Extract the newest `dist/releases/<release-id>/CancerLabTrainer-win64.zip` and double-click `CancerLabTrainer.exe`. Older packages remain preserved. The ZIP includes the runtime, scenario, player guide, and third-party notices; no Godot editor or installed .NET runtime is required. See [the player guide](docs/PLAYER_GUIDE.md) for the six-well workflow, keyboard controls, and local save location.

The 20–30 minute Level 1 target and 15–20 minute Level 2 target are learning-design targets, not measured completion times. Simulated minutes are action counters, not experimentally validated incubation times.

## Level 2 release-candidate evidence

The current Level 2 implementation and package identity are recorded in the [2026-09-17 validation addendum](docs/validation/level2-validation-2026-09-17-addendum.md). It links the release, source fingerprint, package manifests, deterministic checks, and the remaining native keyboard, Narrator, and full-session performance matrix. Those native sessions are incomplete, so the app is not validated for workplace use or learner competence.

## Develop from source

Install the Godot .NET editor 4.7.2 and .NET SDK 8, then open `project.godot` in the editor, build, and press **F5**. From PowerShell (substitute your installation paths):

```powershell
$env:DOTNET_ROOT = 'C:\path\to\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
& 'C:\path\to\Godot.exe' --editor --path .
```

The run button opens the menu. Mouse buttons are focusable and can be activated with Tab/Enter. The focused workflow actions also have short keyboard alternatives displayed in the panel.

## Build the separate browser candidate

The web implementation is a separate standard-GDScript project in `web/`; it does not load the desktop C# project or desktop saves. It runs two authoritative GDScript models and uses only browser-local IndexedDB for browser attempts. No account, telemetry, or learner-data transfer is included.

Use a standard non-Mono Godot 4.7.2 executable. Optional no-thread templates are passed as portable inputs rather than stored in the preset:

```powershell
./scripts/web/build-web.ps1 `
  -ReleaseId local-web-candidate `
  -GodotPath 'C:\path\to\Godot_v4.7.2-stable_console.exe' `
  -WebTemplateDebug 'C:\path\to\web_nothreads_debug.zip' `
  -WebTemplateRelease 'C:\path\to\web_nothreads_release.zip'
```

The immutable output is `dist/web/local-web-candidate/`, with a relative release manifest, generated offline cache list, PCK inventory, and ZIP checksum. It is a local candidate, not a deployment. `scripts/web/stage-public.py --release-id local-source-review` creates a separate, hashed allowlisted source review under `dist/public-stage/`; it excludes learner records, desktop packages, caches, and the generated parity oracle JSON. See [web publishing readiness](docs/validation/web-publishing-readiness-2026-09-28.md) for the remaining browser/offline/publication gates.

## Tests

The public `LabSimulation.Submit` interface is the test seam. The test project links the real simulation source and avoids Godot or UI internals:

```powershell
dotnet run --project tests/CancerLabTrainer.Tests.csproj
```

Expected outcome: `PASS: all simulation contract tests`.

The engine-level smoke test drives both modes through the UI action adapter, checks the explicit forward sequence and fast-release recovery, verifies deferred Assessment explanations, verifies read-only historical fixtures without touching their source bytes, reloads new saves, exports CSV/HTML/JSON, and deletes only its own disposable attempt. It requires an isolated data directory:

```powershell
$env:LAB_TRAINER_DATA_DIR = Join-Path $env:TEMP ('lab-qa-' + [guid]::NewGuid())
& 'C:\path\to\Godot.exe' --headless --path . -- --smoke-test
```

The packaged executable accepts the same `--headless -- --smoke-test` arguments. This verifies application integration; it does not replace native mouse/keyboard testing. New saves use format 3/model 1.1.0 and are validated by replaying the action history, including detailed pipette stage/targets and nominal quantities. Exact format 2/model 1.0.1 saves are preserved as read-only historical records and are never replayed, migrated, overwritten, resumed, or exported by the current model. Incompatible or inconsistent files are preserved and rejected rather than silently altered. These checks establish internal consistency, not tamper-proof identity or certification.

## Package Windows x64

Install Godot export templates for the same editor version. Then run:

```powershell
./scripts/package-windows.ps1 -Godot 'C:\path\to\Godot.exe' -Dotnet 'C:\path\to\dotnet.exe'
```

It creates a fresh `dist/releases/<timestamp-guid>/CancerLabTrainer-win64.zip`, with a source-input manifest, build identity, and release manifest. This is a local package only; it does not publish or upload the app.

## Verification

The macOS Universal 2 packaging route and its opening/signing boundary are documented in [docs/MACOS_OPENING.md](docs/MACOS_OPENING.md) and [the 2026-09-28 local build record](docs/validation/macos-local-build-2026-09-28.md). This is static Windows-host evidence; Mac launch and Apple trust checks remain required.

To reproduce the package, use `scripts/package-macos.ps1` with `-Godot`, `-Dotnet`, `-Python`, and `-TemplateArchive`; the exact validated command and tool paths are retained in that local build record.

Run the automated simulation tests, then follow [docs/manual-verification.md](docs/manual-verification.md) and the [validation kit](docs/validation/README.md). The manual script covers Guided and Assessment flows, keyboard alternatives, save/resume/delete, exports, incorrect actions, recovery, and a viewport screenshot through Godot’s `--path` run. Frame-rate support and physical comparability require representative-device and paired laboratory validation respectively.

## Safety and scientific limits

All volumes, timings, response values, handling effects, and quality thresholds are illustrative training choices. Follow local SOPs, biosafety requirements, equipment manuals, and supervisor instructions in a real laboratory. The claim register in [docs/science-and-sources.md](docs/science-and-sources.md) labels sourced facts, training simplifications, and intentionally fictional content.


