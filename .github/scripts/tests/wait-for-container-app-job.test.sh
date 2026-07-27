#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
helper="${script_dir}/../wait-for-container-app-job.sh"
test_tmp="$(mktemp -d)"
trap 'rm -rf -- "${test_tmp}"' EXIT

pass_count=0

pass() {
  printf 'ok - %s\n' "$1"
  pass_count=$((pass_count + 1))
}

fail() {
  printf 'not ok - %s\n' "$1" >&2
  if [[ -s "${test_tmp}/stderr" ]]; then
    sed 's/^/  stderr: /' "${test_tmp}/stderr" >&2
  fi
  if [[ -s "${test_tmp}/stdout" ]]; then
    sed 's/^/  stdout: /' "${test_tmp}/stdout" >&2
  fi
  exit 1
}

mkdir "${test_tmp}/fake-bin"

cat >"${test_tmp}/fake-bin/az" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

expected=(
  containerapp job execution show
  --resource-group intelibill-shared
  --name intelibill-dev-migrate
  --job-execution-name intelibill-dev-migrate-abc123
  --query properties.status
  --output tsv
)

actual=("$@")
[[ "${#actual[@]}" -eq "${#expected[@]}" ]] || exit 91
for index in "${!expected[@]}"; do
  [[ "${actual[index]}" == "${expected[index]}" ]] || exit 92
done

count=0
if [[ -f "${AZ_CALL_COUNT_FILE:?}" ]]; then
  count="$(<"${AZ_CALL_COUNT_FILE}")"
fi
count=$((count + 1))
printf '%s\n' "${count}" >"${AZ_CALL_COUNT_FILE}"

status="$(sed -n "${count}p" "${AZ_STATUS_FILE:?}")"
if [[ -z "${status}" ]]; then
  status="$(tail -n 1 "${AZ_STATUS_FILE}")"
fi
printf '%s\n' "${status}"
SH
chmod +x "${test_tmp}/fake-bin/az"

run_case() {
  local statuses="$1"
  local max_polls="$2"

  printf '%s\n' "${statuses}" >"${test_tmp}/statuses"
  printf '0\n' >"${test_tmp}/az-count"
  : >"${test_tmp}/stdout"
  : >"${test_tmp}/stderr"

  set +e
  env \
    AZ_CALL_COUNT_FILE="${test_tmp}/az-count" \
    AZ_STATUS_FILE="${test_tmp}/statuses" \
    MAX_POLLS="${max_polls}" \
    POLL_INTERVAL_SECONDS=0 \
    PATH="${test_tmp}/fake-bin:${PATH}" \
    "${helper}" \
    intelibill-shared \
    intelibill-dev-migrate \
    intelibill-dev-migrate-abc123 \
    >"${test_tmp}/stdout" 2>"${test_tmp}/stderr"
  case_status=$?
  set -e
}

expect_success() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -eq 0 ]] || fail "${name} (expected success, got ${case_status})"
  pass "${name}"
}

expect_failure() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -ne 0 ]] || fail "${name} (expected failure)"
  pass "${name}"
}

expect_success \
  "running execution eventually succeeds" \
  $'Running\nSucceeded' \
  3
[[ "$(<"${test_tmp}/az-count")" == "2" ]] ||
  fail "success polls the named execution exactly twice"
pass "success polls the named execution exactly twice"

expect_failure \
  "terminal failed execution fails immediately" \
  $'Running\nFailed\nSucceeded' \
  4
[[ "$(<"${test_tmp}/az-count")" == "2" ]] ||
  fail "failure stops polling at the first terminal failure"
pass "failure stops polling at the first terminal failure"

expect_failure \
  "empty execution status fails closed" \
  $'\n' \
  2
[[ "$(<"${test_tmp}/az-count")" == "1" ]] ||
  fail "malformed status stops after one query"
pass "malformed status stops after one query"

expect_failure \
  "running execution respects the poll budget" \
  $'Running\nRunning\nSucceeded' \
  2
[[ "$(<"${test_tmp}/az-count")" == "2" ]] ||
  fail "timeout uses the exact configured poll budget"
pass "timeout uses the exact configured poll budget"

printf '1..%s\n' "${pass_count}"
