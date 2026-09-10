#!/usr/bin/env bash
# Build release artifacts for every supported Jellyfin ABI.
#
#   ./scripts/package.sh [version]
#
# Produces, for each ABI, an installable plugin archive under dist/:
#   dist/<abi-name>/                                 unpacked plugin directory
#   dist/apple-music_<version>_<abi-name>.zip        archive
#   dist/apple-music_<version>_<abi-name>.zip.md5    checksum (Jellyfin manifests use MD5)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PROJECT="Jellyfin.Plugin.AppleMusic/Jellyfin.Plugin.AppleMusic.csproj"
DIST="$ROOT/dist"

# (target-framework, jellyfin-abi, archive-suffix)
TARGETS=(
  "net9.0:10.11.0.0:jellyfin-10.11"
  "net10.0:12.0.0.0:jellyfin-12.0"
)

VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)}"
if [ -z "$VERSION" ]; then
  echo "error: could not determine version" >&2
  exit 1
fi

TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# The changelog is substituted into a JSON template with sed, so it has to
# survive BOTH passes: this sanitising pass, and the later `s|__CHANGELOG__|...|`
# whose replacement text it becomes.
#   - backslashes and double quotes are dropped, so no JSON escaping is needed
#   - newlines/tabs and the sed delimiter '|' become spaces
#   - '&' is turned into '\&' so that the LATER sed emits a literal '&'
#     instead of expanding it to the whole match
CHANGELOG="$(printf '%s' "${CHANGELOG:-}" \
  | tr -d '\\"' \
  | tr '\n\r\t|' '    ' \
  | sed -e 's:&:\\&:g')"

rm -rf "$DIST"
mkdir -p "$DIST"

for target in "${TARGETS[@]}"; do
  tfm="${target%%:*}"
  rest="${target#*:}"
  abi="${rest%%:*}"
  name="${rest#*:}"

  echo "==> building $name ($tfm, ABI $abi)"
  outdir="$DIST/$name"
  dotnet publish "$PROJECT" \
    --configuration Release \
    --framework "$tfm" \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION" \
    -p:FileVersion="$VERSION" \
    --output "$outdir"

  # Jellyfin only needs the plugin assembly (and its pdb for stack traces).
  find "$outdir" -type f \
    ! -name 'Jellyfin.Plugin.AppleMusic.dll' \
    ! -name 'Jellyfin.Plugin.AppleMusic.pdb' \
    -delete
  find "$outdir" -type d -empty -delete

  sed -e "s|__TARGET_ABI__|$abi|" \
      -e "s|__VERSION__|$VERSION|" \
      -e "s|__TIMESTAMP__|$TIMESTAMP|" \
      -e "s|__CHANGELOG__|$CHANGELOG|" \
      scripts/meta.template.json > "$outdir/meta.json"

  archive="$DIST/apple-music_${VERSION}_${name}.zip"
  (cd "$outdir" && zip -q -r "$archive" .)

  if command -v md5sum >/dev/null 2>&1; then
    (cd "$DIST" && md5sum "$(basename "$archive")" | awk '{print $1}' > "$archive.md5")
  else
    (cd "$DIST" && md5 -q "$(basename "$archive")" > "$archive.md5")
  fi

  echo "    $archive"
done

echo
echo "artifacts in $DIST:"
ls -1 "$DIST"
