#!/usr/bin/env bash
# Point the site's download section at a release: set-release.sh <tag> <dir with the release files>.
# Run by the release workflow; the VPS picks the change up from main.
set -euo pipefail
tag=$1 dist=$2
page="$(dirname "$0")/index.html"
old=$(grep -o 'data-release="[^"]*"' "$page" | cut -d'"' -f2)
sed -i "s/${old//./\\.}/$tag/g" "$page"
for kind in windows-x64 linux-x64 apk; do
  case $kind in apk) f="$dist/BaK-Again-$tag.apk" ;; *) f="$dist/BaK-Again-$tag-$kind.zip" ;; esac
  mb=$(( ($(stat -c%s "$f") + 500000) / 1000000 ))
  sed -i "s|\(data-size=\"$kind\">\)[^<]*|\1$mb MB|" "$page"
done
