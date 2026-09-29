"""Create a reviewable, allowlisted public-source staging directory.

This script never reads or copies files outside public-source-allowlist.json.
It is intentionally separate from publishing and does not run git commands.
"""

from __future__ import annotations

import argparse
import glob
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys


ROOT = Path(__file__).resolve().parents[2]
ALLOWLIST = Path(__file__).with_name("public-source-allowlist.json")
_WINDOWS_DRIVE = b"c:"
PRIVATE_PATH_MARKERS = (
    _WINDOWS_DRIVE + b"\\" + b"users" + b"\\",
    _WINDOWS_DRIVE + b"/" + b"users" + b"/",
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def allowed_files() -> list[Path]:
    config = json.loads(ALLOWLIST.read_text(encoding="utf-8"))
    files: set[Path] = set()
    for pattern in config["include"]:
        matches = glob.glob(str(ROOT / pattern), recursive=True)
        if not matches:
            raise RuntimeError(f"Allowlisted path has no match: {pattern}")
        for match in matches:
            candidate = Path(match)
            if candidate.is_file():
                files.add(candidate.resolve())
    return sorted(files, key=lambda item: item.relative_to(ROOT).as_posix())


def main() -> int:
    parser = argparse.ArgumentParser(description="Create an allowlisted public-source staging candidate.")
    parser.add_argument("--release-id", required=True, help="Lowercase hyphenated immutable staging identifier.")
    args = parser.parse_args()
    if not __import__("re").fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", args.release_id):
        raise RuntimeError("release id must be a lowercase hyphenated identifier")
    output_root = (ROOT / "dist" / "public-stage").resolve()
    destination = (output_root / args.release_id).resolve()
    if output_root not in destination.parents or destination.exists():
        raise RuntimeError(f"Refusing to overwrite or escape immutable staging directory: {destination}")
    destination.mkdir(parents=True)

    config = json.loads(ALLOWLIST.read_text(encoding="utf-8"))
    public_readme = (ROOT / config["publicReadme"]).resolve()
    try:
        public_readme.relative_to(ROOT)
    except ValueError as error:
        raise RuntimeError("Public README leaves the repository.") from error
    sources = allowed_files()
    if public_readme not in sources:
        raise RuntimeError("The public README must be included by the explicit allowlist.")

    staged: list[dict[str, object]] = []
    for source in sources:
        relative = source.relative_to(ROOT)
        payload = source.read_bytes()
        if any(marker in payload.lower() for marker in PRIVATE_PATH_MARKERS):
            raise RuntimeError(f"Public staging refused a local absolute path marker in {relative.as_posix()}")
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        staged.append({"path": relative.as_posix(), "bytes": len(payload), "sha256": sha256(source)})
        if source == public_readme:
            readme_target = destination / "README.md"
            shutil.copyfile(source, readme_target)
            staged.append({"path": "README.md", "bytes": len(payload), "sha256": sha256(source)})

    manifest = {
        "schemaVersion": 1,
        "releaseId": args.release_id,
        "allowlist": ALLOWLIST.relative_to(ROOT).as_posix(),
        "files": staged,
        "excluded": [
            "learner records and saves",
            "desktop packages and build output",
            "generated parity fixture JSON",
            "editor caches and temporary evidence",
        ],
    }
    (destination / "public-source-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: staged {len(staged)} allowlisted public-source files at {destination}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
