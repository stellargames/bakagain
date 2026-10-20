#!/usr/bin/env bash
# Smoke test for bakmod; run from the bakagain root: bash src/BakMod/smoke.sh
set -euo pipefail
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
bakmod() { dotnet run --project src/BakMod -- "$@"; }

mkdir -p "$tmp/good/SCX" "$tmp/bad"
echo '{"schema":1,"id":"smoke-mod","name":"Smoke","version":"1.0.0"}' > "$tmp/good/mod.json"
# 1x1 PNG
base64 -d > "$tmp/good/SCX/TEST.png" <<< 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='
echo '{}' > "$tmp/bad/mod.json"

bakmod pack "$tmp/good" -o "$tmp/out"
test -f "$tmp/out/smoke-mod-1.0.0.zip"
bakmod validate "$tmp/out/smoke-mod-1.0.0.zip"

rc=0; bakmod pack "$tmp/bad" -o "$tmp/out" 2> "$tmp/err" || rc=$?
[ "$rc" = 1 ] || { echo "bad folder: expected exit 1, got $rc"; exit 1; }
grep -q '^error:' "$tmp/err"
rc=0; bakmod > /dev/null 2>&1 || rc=$?
[ "$rc" = 2 ] || { echo "no args: expected exit 2, got $rc"; exit 1; }
echo OK
