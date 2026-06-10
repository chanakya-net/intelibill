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

echo "Using Flutter: $("${FLUTTER[@]}" --version | head -1)"
echo "Expected Flutter: $(cat .flutter-version) (see .flutter-version)"

"${FLUTTER[@]}" pub get
"${FLUTTER[@]}" gen-l10n
"${DART[@]}" run build_runner build
"${DART[@]}" format .

echo "Codegen complete. Commit any changes under lib/ before pushing."
