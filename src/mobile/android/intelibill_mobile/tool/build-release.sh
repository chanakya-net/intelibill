#!/usr/bin/env bash
#
# Release APK build. API_BASE_URL has no sensible default here: the emulator
# address the app falls back to during development is unreachable from a real
# device and unencrypted, so this refuses to build without one rather than
# producing an APK that installs and then talks to nothing.
#
#   API_BASE_URL=https://<api-host>/api ./tool/build-release.sh [--split-per-abi ...]
#
set -euo pipefail

if [[ -z "${API_BASE_URL:-}" ]]; then
  echo "API_BASE_URL is not set." >&2
  echo "Use the deployed API origin with the /api suffix, for example:" >&2
  echo "  API_BASE_URL=https://intelibill-prod-api.<region>.azurecontainerapps.io/api $0" >&2
  exit 1
fi

if [[ "${API_BASE_URL}" != https://* ]]; then
  echo "API_BASE_URL must be https: got '${API_BASE_URL}'." >&2
  echo "An access token travels on every request." >&2
  exit 1
fi

cd "$(dirname "$0")/.."

exec flutter build apk --release \
  --dart-define=API_BASE_URL="${API_BASE_URL}" \
  "$@"
