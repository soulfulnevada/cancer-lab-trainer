# Browser parity oracle

`csharp-oracle.json` is a local generated artifact, not a checked-in fixture. Generate it with `scripts/web/generate-parity-oracle.ps1`; the script verifies its SHA-256 against `csharp-oracle.sha256` before Godot parity tests run.

Update the expected hash only when an intentional C# oracle change has been reviewed: generate the oracle twice from a clean build, verify the hashes match, inspect the changed trace/report coverage, then update this file in the same reviewed change. The generator never updates the expected hash.
