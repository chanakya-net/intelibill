#!/usr/bin/env bash
# Runs the merchant or licensing integration tests against pre-generated Wolverine handlers,
# the way the deployed image runs: the handler classes are generated, compiled
# into the API, and loaded as-is instead of being compiled again in every API
# host the suite starts. Faster and lighter than a plain `dotnet test`, which
# stays correct but compiles handlers at run time.
#
# The generated sources are always rebuilt from the current handlers and removed
# again afterwards, so they can never be stale and never reach a commit.
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

cleanup
dotnet run --project "${api_project}" --no-launch-profile -- codegen write
dotnet build "${tests}"

INTEGRATION_TEST_PREGENERATED_HANDLERS=1 dotnet test "${tests}" --no-build "$@"
