#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "${BASH_SOURCE[0]}")/../../.."
source scripts/licensing/p01-foundation-evidence.sh

fixture="$(mktemp -d /tmp/p0115-harness.XXXXXX)"
listener=
finish() {
  if [[ -n "$listener" ]]; then
    kill "$listener" 2>/dev/null || true
    wait "$listener" 2>/dev/null || true
  fi
  rm -r -- "$fixture"
}
trap finish EXIT
die() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

evidence_contract() {
  local log="$fixture/browser.log" secret="$fixture/secret" candidate="$fixture/candidate"
  printf '%s\n' '  1 passed (1s)' >"$log"
  require_pass_count "$log" 1 || die 'a completed browser run was refused'
  printf '%s\n' '  1 skipped' >>"$log"
  if require_pass_count "$log" 1; then die 'partial browser evidence passed'; fi
  : >"$log"
  if require_pass_count "$log" 1; then die 'missing browser count passed'; fi
  printf '%s\n' 'private-fixture-canary' >"$secret"
  printf '%s\n' 'leaked private-fixture-canary' >"$candidate"
  if publish_checked "$candidate" "$secret" "$fixture/public"; then die 'credential was published'; fi
  [[ ! -e "$fixture/public" ]] || die 'credential reached publication path'
  printf '%s\n' 'browser and role assertions passed' >"$candidate"
  publish_checked "$candidate" "$secret" "$fixture/public" || die 'safe evidence was refused'
  grep -Fxq 'PASS fixture credential scan' "$fixture/public" || die 'scan result missing'
  printf '%s\n' 'PASS incomplete or unsafe evidence refuses success'
}

start_doubles() {
  mkdir "$fixture/bin"
  export P0115_TEST_SOCKET="$fixture/local.sock" P0115_TEST_CALLS="$fixture/calls"
  unset DOCKER_HOST
  node -e 'require("node:net").createServer().listen(process.env.P0115_TEST_SOCKET)' &
  listener=$!
  for ((i = 0; i < 20; i++)); do
    [[ ! -S "$P0115_TEST_SOCKET" ]] || break
    sleep 0.1
  done
  [[ -S "$P0115_TEST_SOCKET" ]] || die 'test socket unavailable'
  cat >"$fixture/bin/docker" <<'MOCK'
#!/bin/bash
case "$1:$2" in
  context:show) printf 'test\n' ;;
  context:inspect) printf 'unix://%s\n' "$P0115_TEST_SOCKET" ;;
  ps:*|volume:ls|network:ls) : ;;
  *) exit 97 ;;
esac
MOCK
  cat >"$fixture/bin/bash" <<'MOCK'
#!/bin/bash
[[ "$1" == scripts/dev/test-licensing-stack.sh && "$2" == --case && "$3" == all ]] || exit 96
printf 'child invoked\n' >>"$P0115_TEST_CALLS"
if [[ "${P0115_TEST_MOVE_HEAD:-0}" == 1 ]]; then
  git -c core.hooksPath=/dev/null commit --allow-empty --no-gpg-sign -qm 'Advance fixture source'
fi
exit "$P0115_TEST_CHILD_STATUS"
MOCK
  chmod +x "$fixture/bin/docker" "$fixture/bin/bash"
  export PATH="$fixture/bin:$PATH"
}

start_source_repo() {
  local source_repo="$fixture/source" path
  for path in scripts/dev/licensing-stack-lib.sh scripts/licensing/p01-foundation-evidence.sh scripts/licensing/test-p01-foundation.sh; do
    mkdir -p "$source_repo/$(dirname "$path")"
    cp "$path" "$source_repo/$path"
  done
  cd "$source_repo"
  git init -q
  git config user.name 'P01 fixture'
  git config user.email p0115-fixture@example.test
  git add scripts
  git -c core.hooksPath=/dev/null commit --no-gpg-sign -qm 'Record fixture source'
}

expect_exit() {
  local expected="$1" diagnostic="$2" status=0
  /bin/bash scripts/licensing/test-p01-foundation.sh --case all >"$fixture/output" 2>&1 || status=$?
  [[ "$status" == "$expected" ]] || die "expected exit $expected, got $status"
  grep -Fq "$diagnostic" "$fixture/output" || die "missing stage diagnostic: $diagnostic"
  [[ ! -e "$P0115_ARTIFACTS/summary.txt" ]] || die 'failure published a summary'
}

evidence_contract
start_doubles
start_source_repo
export P0115_PROJECT=default P0115_ARTIFACTS="$fixture"
export DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development
export P0115_TEST_CHILD_STATUS=19
expect_exit 1 'Set a unique P0115_PROJECT'
export P0115_PROJECT=intelibill-p01-05-845-harnesstest
DOTNET_ENVIRONMENT=Production expect_exit 1 'Use Development or Testing.'
DOCKER_HOST=tcp://127.0.0.1:2375 expect_exit 1 'Use a local Docker Unix socket.'
[[ ! -e "$P0115_TEST_CALLS" ]] || die 'invalid input reached the child'
printf '%s\n' 'PASS unsafe project and environment refused before Compose'
printf '\n# Local tracked edit\n' >>scripts/dev/licensing-stack-lib.sh
expect_exit 1 'Commit tracked changes before running acceptance.'
[[ ! -e "$P0115_TEST_CALLS" ]] || die 'dirty source reached the child'
git restore scripts/dev/licensing-stack-lib.sh
printf '%s\n' 'PASS dirty source refused before Compose'
expect_exit 19 'P01-15 failed at combined-stack (exit 19)'
mkdir "$fixture/missing"
export P0115_ARTIFACTS="$fixture/missing" P0115_TEST_CHILD_STATUS=0
expect_exit 1 'Missing P01-05 completion evidence.'
printf '%s\n' 'PASS child exit propagated and absent child evidence refused'
mkdir "$fixture/advanced"
export P0115_ARTIFACTS="$fixture/advanced"
P0115_TEST_MOVE_HEAD=1 expect_exit 1 'Source commit changed during acceptance.'
printf '%s\n' 'PASS changed source commit refused'
