#!/usr/bin/env python3
"""Build Jellyfin plugin repository manifests from the GitHub releases.

The output directory is published as-is to GitHub Pages by deployment.yaml:

    OUTPUT_DIR/
    ├── index.html                        links to the manifests below
    ├── manifest.json                     stable, Jellyfin 12.0
    ├── manifest-jellyfin-10.11.json      stable, Jellyfin 10.11
    └── dev/
        ├── manifest.json                 stable + pre-releases, Jellyfin 12.0
        └── manifest-jellyfin-10.11.json  stable + pre-releases, Jellyfin 10.11

One manifest per Jellyfin ABI. They must stay separate: Jellyfin keeps every
version whose targetAbi is <= the running server, so a 12.0 server would also
consider the 10.11 build. Listing both under one manifest would let a 12.0
server install the net9.0 assembly.

Stable and dev are separate for the same reason: a dev build 0.1.0.42 sorts
above the stable 0.1.0.0, so a combined manifest would auto-update stable
users onto develop.

Usage:
    scripts/manifest.py OWNER/REPO OUTPUT_DIR
"""

import html
import json
import os
import re
import sys
import urllib.error
import urllib.request

API = "https://api.github.com"

# (archive suffix, Jellyfin ABI, manifest file name)
TARGETS = [
    ("jellyfin-12.0", "12.0.0.0", "manifest.json"),
    ("jellyfin-10.11", "10.11.0.0", "manifest-jellyfin-10.11.json"),
]

# (sub-directory, include pre-releases)
CHANNELS = [
    ("", False),
    ("dev", True),
]

ASSET_RE = re.compile(r"^apple-music_(?P<version>[0-9.]+)_(?P<target>jellyfin-[0-9.]+)\.zip$")


def request(url):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "apple-music-manifest"}
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers)) as response:
        return response.read()


def read_meta():
    """Plugin-level fields come from the same template package.sh uses."""
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "meta.template.json"), encoding="utf-8") as handle:
        return json.load(handle)


def collect(repo):
    """Return {target: [version entry, ...]} newest first, drafts excluded."""
    releases = json.loads(request(f"{API}/repos/{repo}/releases?per_page=100"))
    found = {target: [] for target, _, _ in TARGETS}

    for release in releases:
        if release.get("draft"):
            continue

        assets = {asset["name"]: asset for asset in release.get("assets", [])}
        for name, asset in assets.items():
            match = ASSET_RE.match(name)
            if not match:
                continue

            target = match.group("target")
            if target not in found:
                continue

            checksum_asset = assets.get(name + ".md5")
            if checksum_asset is None:
                print(f"  skipping {name}: no checksum published", file=sys.stderr)
                continue

            checksum = request(checksum_asset["browser_download_url"]).decode().strip()
            found[target].append({
                "version": match.group("version"),
                "changelog": (release.get("body") or release.get("name") or "").strip(),
                "targetAbi": next(abi for suffix, abi, _ in TARGETS if suffix == target),
                "sourceUrl": asset["browser_download_url"],
                "checksum": checksum,
                "timestamp": release.get("published_at") or release.get("created_at"),
                "prerelease": bool(release.get("prerelease")),
            })

    for entries in found.values():
        entries.sort(key=lambda e: [int(part) for part in e["version"].split(".")], reverse=True)
    return found


def write_manifest(path, meta, versions):
    plugin = {
        "guid": meta["guid"],
        "name": meta["name"],
        "description": meta["description"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "category": meta["category"],
        # "prerelease" is our own bookkeeping, not part of the Jellyfin schema.
        "versions": [{k: v for k, v in entry.items() if k != "prerelease"} for entry in versions],
    }
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump([plugin], handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def write_index(out_dir, meta, repo, manifests):
    """A plain landing page so the Pages root is not a 404."""
    rows = "\n".join(
        f'      <tr><td>{html.escape(channel)}</td><td>Jellyfin {html.escape(jellyfin)}</td>'
        f'<td><a href="{html.escape(path)}">{html.escape(path)}</a></td>'
        f'<td>{count}</td></tr>'
        for channel, jellyfin, path, count in manifests
    )
    page = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>{html.escape(meta["name"])} — Jellyfin plugin repository</title>
  <style>
    body {{ font-family: system-ui, sans-serif; max-width: 48rem; margin: 3rem auto; padding: 0 1rem; }}
    table {{ border-collapse: collapse; }}
    td, th {{ padding: .3rem .8rem; border-bottom: 1px solid #ccc; text-align: left; }}
    code {{ background: #eee; padding: .1em .3em; }}
  </style>
</head>
<body>
  <h1>{html.escape(meta["name"])}</h1>
  <p>{html.escape(meta["overview"])}</p>
  <p>Add one of the manifest URLs below under
    <em>Dashboard → Plugins → Repositories</em>. Pick the one matching your
    Jellyfin version; the <code>dev</code> channel also lists pre-release
    builds from the <code>develop</code> branch.</p>
  <table>
    <thead><tr><th>Channel</th><th>Jellyfin</th><th>Manifest</th><th>Versions</th></tr></thead>
    <tbody>
{rows}
    </tbody>
  </table>
  <p>Source: <a href="https://github.com/{html.escape(repo)}">github.com/{html.escape(repo)}</a></p>
</body>
</html>
"""
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as handle:
        handle.write(page)


def main():
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    repo, out_dir = sys.argv[1], sys.argv[2]
    meta = read_meta()
    found = collect(repo)
    os.makedirs(out_dir, exist_ok=True)

    manifests = []
    for subdir, include_prerelease in CHANNELS:
        for target, abi, filename in TARGETS:
            versions = [
                entry for entry in found[target]
                if include_prerelease or not entry["prerelease"]
            ]
            relative = os.path.join(subdir, filename) if subdir else filename
            write_manifest(os.path.join(out_dir, relative), meta, versions)
            manifests.append((subdir or "stable", abi.rsplit(".", 2)[0], relative, len(versions)))
            print(f"{relative}: {len(versions)} version(s)")

    write_index(out_dir, meta, repo, manifests)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"GitHub API error: {error}", file=sys.stderr)
        sys.exit(1)
