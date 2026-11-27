#!/usr/bin/env bash
# Runs the merchant or licensing integration tests against pre-generated Wolverine handlers,
# the way the deployed image runs: the handler classes are generated, compiled
# into the API, and loaded as-is instead of being compiled again in every API
# host the suite starts. Faster and lighter than a plain `dotnet test`, which
# stays correct but compiles handlers at run time.
#
# The generated sources are rebuilt whenever the merchant backend changes and removed
# again afterwards, so they can never be stale and never reach a commit. Generation
# output is cached under .cache/wolverine-codegen/, keyed by a fingerprint of every
# input that can change it (merchant and shared backend sources, project and package
# files, the SDK pin), so repeated runs of an unchanged backend skip the generation step.
# INTEGRATION_CODEGEN_CACHE=0 always regenerates.
#
# Usage: scripts/dev/test-integration.sh [--licensing] [test runner options]
# This script builds Debug binaries. Pass test filters, loggers or xUnit settings here.
#   scripts/dev/test-integration.sh --filter "FullyQualifiedName~SalesControllerTests"
#   scripts/dev/test-integration.sh --licensing --filter "FullyQualifiedName~P0217Tests"
#   INTEGRATION_TEST_PARALLELISM=2 scripts/dev/test-integration.sh
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
api_project="${repository_root}/src/backend/Intelibill.Api"
generated="${api_project}/Internal/Generated"
cache_root="${repository_root}/.cache/wolverine-codegen"
tests="${repository_root}/tests/backend/integration/Intelibill.Integration.Tests"
if [[ "${1:-}" == "--licensing" ]]; then
  tests="${repository_root}/tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj"
  shift
fi

cleanup() {
  rm -rf "${generated}"
  # codegen creates Internal/ when it is missing; leave no empty folder behind.
  rmdir "${api_project}/Internal" 2>/dev/null || true
}
trap cleanup EXIT

# Hash of every file that can change the generated handlers. Paths are part of the
# hash, so a rename or move invalidates the cache as well as an edit.
codegen_fingerprint() {
  (
    cd "${repository_root}"
    {
      find src/backend src/shared/backend \
        \( -name bin -o -name obj -o -path "src/backend/Intelibill.Api/Internal" \) -prune -o \
        -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' -o -name '*.json' \) -print0
      printf '%s\0' Directory.Build.props Directory.Packages.props global.json
    } | LC_ALL=C sort -z | xargs -0 shasum -a 256 | shasum -a 256 | cut -c1-32
  )
}

generate_handlers() {
  dotnet run --project "${api_project}" --no-launch-profile -- codegen write
}

cleanup
if [[ "${INTEGRATION_CODEGEN_CACHE:-1}" == "0" ]]; then
  generate_handlers
else
  entry="${cache_root}/$(codegen_fingerprint)"
  if [[ -f "${entry}/complete" ]]; then
    echo "Reusing Wolverine handlers generated for this backend (${entry})."
    mkdir -p "$(dirname "${generated}")"
    cp -Rp "${entry}/Generated" "${generated}"
  else
    generate_handlers
    mkdir -p "${cache_root}"
    staging="$(mktemp -d "${cache_root}/.staging.XXXXXX")"
    cp -Rp "${generated}" "${staging}/Generated"
    touch "${staging}/complete"
    # Another run may have published the same entry meanwhile; either copy is identical.
    if [[ -e "${entry}" ]] || ! mv "${staging}" "${entry}" 2>/dev/null; then
      rm -rf "${staging}"
    fi
    # Keep the five most recent backends.
    ls -1td "${cache_root}"/*/ 2>/dev/null | tail -n +6 | while IFS= read -r stale; do
      rm -rf "${stale}"
    done
  fi
fi
dotnet build "${tests}"

INTEGRATION_TEST_PREGENERATED_HANDLERS=1 dotnet test "${tests}" --no-build "$@"
