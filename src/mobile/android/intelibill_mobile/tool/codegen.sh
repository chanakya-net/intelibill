#!/usr/bin/env bash
# Regenerates all committed generated Dart sources (l10n + build_runner).
# Run from anywhere; pins to the Flutter version in .flutter-version when using FVM.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if command -v fvm >/dev/null 2>&1 && [[ -f .fvm/fvm_config.json ]]; then
  FLUTTER=(fvm flutter)
  DART=(fvm dart)
else
  FLUTTER=(flutter)
  DART=(dart)
fi

FLUTTER_VERSION_OUTPUT="$("${FLUTTER[@]}" --version | head -1)"
ACTUAL_FLUTTER_VERSION="$(awk '{print $2}' <<<"$FLUTTER_VERSION_OUTPUT")"
EXPECTED_FLUTTER_VERSION="$(tr -d '[:space:]' < .flutter-version)"

echo "Using Flutter: $FLUTTER_VERSION_OUTPUT"
echo "Expected Flutter: $EXPECTED_FLUTTER_VERSION (see .flutter-version)"

if [[ "$ACTUAL_FLUTTER_VERSION" != "$EXPECTED_FLUTTER_VERSION" ]]; then
  echo "Flutter version mismatch: expected $EXPECTED_FLUTTER_VERSION, got $ACTUAL_FLUTTER_VERSION." >&2
  echo "Run 'fvm install && fvm use' or install Flutter $EXPECTED_FLUTTER_VERSION stable." >&2
  exit 1
fi

"${FLUTTER[@]}" pub get
"${FLUTTER[@]}" gen-l10n
"${DART[@]}" run build_runner build
"${DART[@]}" format .

echo "Codegen complete. Commit any changes under lib/ before pushing."
