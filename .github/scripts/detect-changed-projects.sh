#!/usr/bin/env bash
# Maps changed paths to the validation each project owns, so a change runs the
# tests of the projects it can affect and nothing else.
#
# Reads changed paths on stdin, one per line, and prints GitHub Actions outputs:
#
#   merchant_web=true|false
#   admin_web=true|false
#   backend_suites=[{"name":"merchant","roots":"tests/backend/intelibill ..."}]
#   images=[{"name":"api","dockerfile":"...","tag":"intelibill-api-pr"}]
#
# Run with --all to select everything (manual runs, or when the changed range
# cannot be determined).
#
# Ownership follows the directory layout, with shared code fanning out to every
# consumer:
#
#   merchant backend    src/backend/intelibill, tests/backend/intelibill
#   licensing backend   src/backend/licensing, tests/backend/licensing,
#                       tests/contracts/licensing
#   both backends       src/backend/shared, scripts/db, scripts/dev,
#                       docker-compose.yml
#   merchant web        src/frontend/intelibill
#   admin web           src/frontend/licensing
#   both web apps       src/frontend/shared, package.json, bun.lock
#   everything          the workflow and its scripts, root MSBuild/SDK files,
#                       the root solution, .dockerignore
#
# The consumer regression suites in tests/backend/shared run with any backend
# suite. Documentation never selects anything.
set -euo pipefail

merchant_backend=false
licensing_backend=false
shared_regressions=false
merchant_web=false
admin_web=false

select_all() {
  merchant_backend=true
  licensing_backend=true
  shared_regressions=true
  merchant_web=true
  admin_web=true
}

classify() {
  local path="$1"

  case "${path}" in
    *.md | docs/* | */architecture_doc/*) return ;;
  esac

  case "${path}" in
    .github/workflows/deploy.yml | .github/scripts/* | \
      Directory.Build.props | Directory.Packages.props | global.json | \
      Intelibill.slnx | .dockerignore)
      select_all
      ;;
    src/backend/shared/* | scripts/db/* | scripts/dev/* | docker-compose.yml)
      merchant_backend=true
      licensing_backend=true
      ;;
    src/backend/intelibill/* | tests/backend/intelibill/*)
      merchant_backend=true
      ;;
    src/backend/licensing/* | tests/backend/licensing/* | tests/contracts/licensing/*)
      licensing_backend=true
      ;;
    tests/backend/shared/*)
      shared_regressions=true
      ;;
    src/frontend/shared/* | package.json | bun.lock)
      merchant_web=true
      admin_web=true
      ;;
    src/frontend/intelibill/*)
      merchant_web=true
      ;;
    src/frontend/licensing/*)
      admin_web=true
      ;;
  esac
}

if [ "${1:-}" = "--all" ]; then
  select_all
else
  while IFS= read -r path || [ -n "${path}" ]; do
    [ -n "${path}" ] || continue
    classify "${path}"
  done
fi

# The consumer regression suites compile against both backends, so they ride
# along with whichever backend suite runs first instead of paying for a build
# of their own. They get their own suite only when nothing else is selected.
suites=()
shared_roots=" tests/backend/shared"
if [ "${merchant_backend}" = true ]; then
  suites+=("{\"name\":\"merchant\",\"roots\":\"tests/backend/intelibill${shared_roots}\"}")
  shared_roots=""
fi
if [ "${licensing_backend}" = true ]; then
  suites+=("{\"name\":\"licensing\",\"roots\":\"tests/backend/licensing${shared_roots}\"}")
  shared_roots=""
fi
if [ -n "${shared_roots}" ] && [ "${shared_regressions}" = true ]; then
  suites+=('{"name":"shared","roots":"tests/backend/shared"}')
fi

images=()
image() {
  images+=("{\"name\":\"$1\",\"dockerfile\":\"$2\",\"tag\":\"$3\"}")
}
if [ "${merchant_backend}" = true ]; then
  image api src/backend/intelibill/Dockerfile intelibill-api-pr
  image migrate src/backend/intelibill/Dockerfile.migrate intelibill-migrate-pr
fi
if [ "${licensing_backend}" = true ]; then
  image licensing-api src/backend/licensing/Dockerfile intelibill-licensing-api-pr
  image licensing-migrate src/backend/licensing/Dockerfile.migrate intelibill-licensing-migrate-pr
fi
if [ "${merchant_web}" = true ]; then
  image web src/frontend/intelibill/Dockerfile intelibill-web-pr
fi
if [ "${admin_web}" = true ]; then
  image admin-web src/frontend/licensing/Dockerfile intelibill-admin-web-pr
fi

join() {
  local IFS=,
  printf '[%s]' "$*"
}

printf 'merchant_web=%s\n' "${merchant_web}"
printf 'admin_web=%s\n' "${admin_web}"
printf 'backend_suites=%s\n' "$(join "${suites[@]+"${suites[@]}"}")"
printf 'images=%s\n' "$(join "${images[@]+"${images[@]}"}")"
