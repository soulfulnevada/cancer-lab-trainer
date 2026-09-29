#!/usr/bin/env python3
"""Exercise the production public stage from its own staged checkout."""

from __future__ import annotations

import json
import importlib.util
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid


ROOT = Path(__file__).resolve().parents[2]
STAGER = ROOT / "scripts" / "web" / "stage-public.py"
STAGED_STAGER = Path("scripts/web/stage-public.py")
OUTPUT_ROOT = ROOT / "dist" / "public-stage"
WORKFLOW_REQUIRED_FILES = (
    ".github/workflows/web.yml",
    "scripts/web/build-web.ps1",
    "scripts/web/generate-parity-oracle.ps1",
    "scripts/web/stage-public.py",
    "tests/CancerLabTrainer.Tests.csproj",
    "tests/Transformation.Tests.csproj",
    "web/tests/storage_adapter_contract.js",
    "web/tests/renderer_id_contract.js",
    "web/tests/pwa_cache_contract.js",
    "web/tests/oracle_command_contract.ps1",
    "web/tests/oracle_serialization_contract.ps1",
    "web/tests/public_stage_contract.py",
)
PUBLIC_README_TITLE = "# Cancer Lab Trainer — Browser Edition"
MARKDOWN_LINK = re.compile(r"\[[^\]]+\]\(([^)]+)\)")


def run_python(script: Path, release_id: str, cwd: Path) -> None:
    subprocess.run(
        [sys.executable, str(script), "--release-id", release_id],
        cwd=cwd,
        check=True,
        text=True,
        capture_output=True,
    )


def remove_stage(path: Path) -> None:
    assert path.parent == OUTPUT_ROOT or path.parent == path.parents[3] / "public-stage"
    if path.exists():
        shutil.rmtree(path)


def assert_public_readme_links(stage: Path) -> None:
    readme = stage / "README.md"
    text = readme.read_text(encoding="utf-8")
    assert text.startswith(PUBLIC_README_TITLE), "Public stage must use the browser-scoped README."
    assert "https://soulfulnevada.github.io/cancer-lab-trainer/" in text
    for target in MARKDOWN_LINK.findall(text):
        local_target = target.split("#", 1)[0]
        if not local_target or "://" in local_target or local_target.startswith("mailto:"):
            continue
        destination = (readme.parent / local_target).resolve()
        assert destination.is_relative_to(stage.resolve()), f"README link escapes staged checkout: {target}"
        assert destination.exists(), f"README link is not staged: {target}"


def main() -> None:
    spec = importlib.util.spec_from_file_location("stage_public", STAGER)
    assert spec and spec.loader
    stage_public = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(stage_public)
    private_windows_path = b"c:" + b"\\" + b"users" + b"\\" + b"example"
    private_uri_path = b"c:" + b"/" + b"users" + b"/" + b"example"
    assert any(marker in private_windows_path for marker in stage_public.PRIVATE_PATH_MARKERS)
    assert any(marker in private_uri_path for marker in stage_public.PRIVATE_PATH_MARKERS)

    suffix = uuid.uuid4().hex[:12]
    source_id = f"stage-contract-{suffix}"
    source_stage = OUTPUT_ROOT / source_id
    nested_id = f"staged-contract-{suffix}"
    nested_stage = source_stage / "dist" / "public-stage" / nested_id
    try:
        run_python(STAGER, source_id, ROOT)
        staged_stager = source_stage / STAGED_STAGER
        assert staged_stager.is_file(), "The public stage must include the workflow-invoked staging script."
        assert_public_readme_links(source_stage)
        for relative in WORKFLOW_REQUIRED_FILES:
            assert (source_stage / relative).is_file(), f"Workflow dependency is absent from staged checkout: {relative}"
        run_python(staged_stager, nested_id, source_stage)
        manifest = json.loads((nested_stage / "public-source-manifest.json").read_text(encoding="utf-8"))
        staged_paths = {entry["path"] for entry in manifest["files"]}
        assert STAGED_STAGER.as_posix() in staged_paths
        assert "README.md" in staged_paths
        assert "web/README.public.md" in staged_paths
        assert_public_readme_links(nested_stage)

        workflow = (ROOT / ".github" / "workflows" / "web.yml").read_text(encoding="utf-8")
        assert "--install-export-templates" not in workflow
        assert "web_nothreads_debug.zip" in workflow
        assert "web_nothreads_release.zip" in workflow
        assert "$dotnetExecutable = Get-Command dotnet -CommandType Application | Select-Object -First 1 -ExpandProperty Source" in workflow
        assert "-DotnetPath $dotnetExecutable" in workflow
        assert "web/tests/oracle_serialization_contract.ps1" in workflow
        assert "--headless --editor --path web --import" in workflow
        assert "Godot project import failed" in workflow
        assert "web/tests/oracle_command_contract.ps1" in workflow
        assert "python web/tests/public_stage_contract.py" in workflow
        assert workflow.count("$LASTEXITCODE -ne 0") >= 8
    finally:
        remove_stage(source_stage)


if __name__ == "__main__":
    main()
    print("PASS: public stage is self-contained and CI uses portable checked commands")
