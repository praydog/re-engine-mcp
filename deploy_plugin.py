"""Symlink the re-engine-mcp plugin sources into a game's reframework directory.

Creates symlinks so the game's hot-reload picks up edits in-place:
  reframework/plugins/source/TestWebAPI.cs  -> <gamedir>/reframework/plugins/source/TestWebAPI.cs
  reframework/plugins/source/WebAPI/        -> <gamedir>/reframework/plugins/source/WebAPI/

Usage:
  python deploy_plugin.py <gamedir>
  python deploy_plugin.py "I:\\SteamLibrary\\steamapps\\common\\RESIDENT EVIL 9"

On Windows, requires Developer Mode enabled or an elevated prompt for symlinks.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent
PLUGIN_SOURCE = REPO_ROOT / "reframework" / "plugins" / "source"


def collect_entries(src: Path) -> list[tuple[Path, bool]]:
    """Return (relative_path, is_dir) for every top-level item under *src*.

    Files are symlinked individually. Directories are symlinked as a whole
    (not recursed) so the game sees the full subtree via one directory symlink.
    """
    entries: list[tuple[Path, bool]] = []
    for child in sorted(src.iterdir()):
        entries.append((child.relative_to(src), child.is_dir()))
    return entries


def create_symlink(src: Path, dst: Path, is_dir: bool) -> None:
    """Create a symlink at *dst* pointing to *src*, replacing stale links."""
    if dst.is_symlink():
        existing_target = dst.resolve()
        if existing_target == src.resolve():
            print(f"  skip (already linked): {dst}")
            return
        print(f"  removing stale symlink: {dst} -> {existing_target}")
        dst.unlink()
    elif dst.exists():
        kind = "directory" if dst.is_dir() else "file"
        print(f"  ERROR: {dst} already exists as a real {kind}. Remove it first.", file=sys.stderr)
        sys.exit(1)

    dst.parent.mkdir(parents=True, exist_ok=True)
    os.symlink(src, dst, target_is_directory=is_dir)
    arrow = " (dir)" if is_dir else ""
    print(f"  linked{arrow}: {dst} -> {src}")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Symlink re-engine-mcp plugin sources into a game directory.",
    )
    parser.add_argument(
        "gamedir",
        type=Path,
        help="Root of the game install (e.g. the folder containing the .exe).",
    )
    args = parser.parse_args()

    gamedir: Path = args.gamedir.resolve()
    if not gamedir.is_dir():
        print(f"ERROR: {gamedir} is not a directory.", file=sys.stderr)
        sys.exit(1)

    if not PLUGIN_SOURCE.is_dir():
        print(f"ERROR: repo plugin source not found at {PLUGIN_SOURCE}", file=sys.stderr)
        sys.exit(1)

    dest_source = gamedir / "reframework" / "plugins" / "source"
    entries = collect_entries(PLUGIN_SOURCE)
    if not entries:
        print(f"Nothing to deploy under {PLUGIN_SOURCE}.")
        return

    print(f"Deploying {len(entries)} item(s) from {PLUGIN_SOURCE}")
    print(f"  -> {dest_source}")
    print()

    for rel, is_dir in entries:
        src = PLUGIN_SOURCE / rel
        dst = dest_source / rel
        create_symlink(src, dst, is_dir)

    print()
    print("Done. REFramework will hot-reload on next source change.")


if __name__ == "__main__":
    main()
