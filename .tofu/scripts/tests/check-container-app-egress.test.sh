#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECKER="${SCRIPT_DIR}/../check-container-app-egress.sh"
FIXTURES="${SCRIPT_DIR}/fixtures"
TEST_TMP="$(mktemp -d)"
trap 'rm -rf -- "${TEST_TMP}"' EXIT

pass_count=0

pass() {
  printf 'ok - %s\n' "$1"
  pass_count=$((pass_count + 1))
}

fail() {
  printf 'not ok - %s\n' "$1" >&2
  if [[ -s "${TEST_TMP}/stderr" ]]; then
    sed 's/^/  stderr: /' "${TEST_TMP}/stderr" >&2
  fi
  if [[ -s "${TEST_TMP}/stdout" ]]; then
    sed 's/^/  stdout: /' "${TEST_TMP}/stdout" >&2
  fi
  exit 1
}

run_case() {
  : >"${TEST_TMP}/stdout"
  : >"${TEST_TMP}/stderr"
  set +e
  "$@" >"${TEST_TMP}/stdout" 2>"${TEST_TMP}/stderr"
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

fixture_args=(
  --advertised-file "${FIXTURES}/advertised-ok.json"
  --firewall-file "${FIXTURES}/firewall-ok.json"
)

expect_success \
  "matching exact managed rules" \
  "${CHECKER}" "${fixture_args[@]}"

mkdir "${TEST_TMP}/no-az-bin"
for fixture_command in bash jq mktemp rm; do
  ln -s "$(command -v "${fixture_command}")" "${TEST_TMP}/no-az-bin/${fixture_command}"
done
expect_success \
  "fixture mode does not require Azure CLI" \
  env PATH="${TEST_TMP}/no-az-bin" "${CHECKER}" "${fixture_args[@]}"

expect_failure \
  "empty advertised set fails even without retained addresses" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-empty.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

expect_failure \
  "empty advertised set fails even with retained addresses" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-empty.json" \
  --retained-file "${FIXTURES}/retained.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_failure \
  "missing managed address is not satisfied by operator rule" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-missing.json"

expect_failure \
  "broad operator rule fails regardless of name" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-broad.json"

expect_failure \
  "non-identical managed range fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-malformed.json"

expect_failure \
  "unreviewed stale managed rule fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_success \
  "reviewed retained managed rule succeeds" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --retained-file "${FIXTURES}/retained.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_success \
  "dev-only selection ignores prod managed rules" \
  "${CHECKER}" "${fixture_args[@]}" --environment dev

cat >"${TEST_TMP}/firewall-unrelated-managed-prefix.json" <<'JSON'
[
  {
    "name": "container-apps-dev-20-10-0-1",
    "startIpAddress": "20.10.0.1",
    "endIpAddress": "20.10.0.1"
  },
  {
    "name": "container-apps-dev-20-10-0-2",
    "startIpAddress": "20.10.0.2",
    "endIpAddress": "20.10.0.2"
  },
  {
    "name": "container-apps-dev-20-10-0-3",
    "startIpAddress": "20.10.0.3",
    "endIpAddress": "20.10.0.3"
  },
  {
    "name": "container-apps-prod-20-20-0-1",
    "startIpAddress": "20.20.0.1",
    "endIpAddress": "20.20.0.1"
  },
  {
    "name": "container-apps-stage-20-30-0-1",
    "startIpAddress": "20.30.0.1",
    "endIpAddress": "20.30.0.1"
  }
]
JSON
expect_success \
  "dev-only selection ignores unrelated managed prefixes" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${TEST_TMP}/firewall-unrelated-managed-prefix.json" \
  --environment dev

expect_failure \
  "full selection rejects unrelated managed prefixes" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${TEST_TMP}/firewall-unrelated-managed-prefix.json"

expect_failure \
  "dev-only selection still rejects broad operator rules" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-broad.json" \
  --environment dev

expect_failure \
  "invalid environment fails" \
  "${CHECKER}" "${fixture_args[@]}" --environment staging

expect_failure \
  "repeated environment fails" \
  "${CHECKER}" "${fixture_args[@]}" --environment dev --environment dev

cat >"${TEST_TMP}/advertised-leading-zero.json" <<'JSON'
{"dev":["20.10.0.01"],"prod":["20.20.0.1"]}
JSON
expect_failure \
  "non-canonical advertised IPv4 fails" \
  "${CHECKER}" \
  --advertised-file "${TEST_TMP}/advertised-leading-zero.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

cat >"${TEST_TMP}/retained-sentinel.json" <<'JSON'
{"dev":["000.000.000.000"],"prod":[]}
JSON
expect_failure \
  "disguised retained sentinel fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --retained-file "${TEST_TMP}/retained-sentinel.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

cat >"${TEST_TMP}/firewall-empty-object.json" <<'JSON'
[
  {},
  {
    "name": "container-apps-dev-20-10-0-1",
    "startIpAddress": "20.10.0.1",
    "endIpAddress": "20.10.0.1"
  },
  {
    "name": "container-apps-dev-20-10-0-2",
    "startIpAddress": "20.10.0.2",
    "endIpAddress": "20.10.0.2"
  },
  {
    "name": "container-apps-dev-20-10-0-3",
    "startIpAddress": "20.10.0.3",
    "endIpAddress": "20.10.0.3"
  },
  {
    "name": "container-apps-prod-20-20-0-1",
    "startIpAddress": "20.20.0.1",
    "endIpAddress": "20.20.0.1"
  }
]
JSON
expect_failure \
  "firewall element cannot be an empty object" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${TEST_TMP}/firewall-empty-object.json"

cat >"${TEST_TMP}/firewall-missing-field.json" <<'JSON'
[
  {
    "name": "operator-missing-end",
    "startIpAddress": "198.51.100.10"
  },
  {
    "name": "container-apps-dev-20-10-0-1",
    "startIpAddress": "20.10.0.1",
    "endIpAddress": "20.10.0.1"
  },
  {
    "name": "container-apps-dev-20-10-0-2",
    "startIpAddress": "20.10.0.2",
    "endIpAddress": "20.10.0.2"
  },
  {
    "name": "container-apps-dev-20-10-0-3",
    "startIpAddress": "20.10.0.3",
    "endIpAddress": "20.10.0.3"
  },
  {
    "name": "container-apps-prod-20-20-0-1",
    "startIpAddress": "20.20.0.1",
    "endIpAddress": "20.20.0.1"
  }
]
JSON
expect_failure \
  "firewall element requires every address field" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${TEST_TMP}/firewall-missing-field.json"

cat >"${TEST_TMP}/firewall-wrong-field-type.json" <<'JSON'
[
  {
    "name": 42,
    "startIpAddress": "198.51.100.10",
    "endIpAddress": "198.51.100.10"
  },
  {
    "name": "container-apps-dev-20-10-0-1",
    "startIpAddress": "20.10.0.1",
    "endIpAddress": "20.10.0.1"
  },
  {
    "name": "container-apps-dev-20-10-0-2",
    "startIpAddress": "20.10.0.2",
    "endIpAddress": "20.10.0.2"
  },
  {
    "name": "container-apps-dev-20-10-0-3",
    "startIpAddress": "20.10.0.3",
    "endIpAddress": "20.10.0.3"
  },
  {
    "name": "container-apps-prod-20-20-0-1",
    "startIpAddress": "20.20.0.1",
    "endIpAddress": "20.20.0.1"
  }
]
JSON
expect_failure \
  "firewall element fields must be strings" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${TEST_TMP}/firewall-wrong-field-type.json"

mkdir "${TEST_TMP}/fake-bin"
cat >"${TEST_TMP}/fake-bin/az" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
{
  separator=""
  for argument in "$@"; do
    printf '%s%s' "${separator}" "${argument}"
    separator=$'\t'
  done
  printf '\n'
} >>"${AZ_CALL_LOG:?}"

mode="${AZ_TEST_MODE:-success}"

if (($# == 10)) &&
  [[ "$1" == "containerapp" ]] &&
  [[ "$2" == "show" ]] &&
  [[ "$3" == "--name" ]] &&
  [[ "$5" == "--resource-group" && "$6" == "intelibill-shared" ]] &&
  [[ "$7" == "--query" && "$8" == "properties.outboundIpAddresses" ]] &&
  [[ "$9" == "--output" && "${10}" == "json" ]]; then
  if [[ "${mode}" == "lookup-failure" && "$4" == "intelibill-dev-api" ]]; then
    printf 'sensitive lookup detail must not escape\n' >&2
    exit 23
  fi
  case "$4" in
    intelibill-dev-api) printf '["20.10.0.1"]\n' ;;
    intelibill-dev-web) printf '["20.10.0.2"]\n' ;;
    intelibill-prod-api|intelibill-prod-web) printf '["20.20.0.1"]\n' ;;
    *) exit 24 ;;
  esac
elif (($# == 11)) &&
  [[ "$1" == "containerapp" ]] &&
  [[ "$2" == "job" && "$3" == "show" ]] &&
  [[ "$4" == "--name" ]] &&
  [[ "$6" == "--resource-group" && "$7" == "intelibill-shared" ]] &&
  [[ "$8" == "--query" && "$9" == "properties.outboundIpAddresses" ]] &&
  [[ "${10}" == "--output" && "${11}" == "json" ]]; then
  if [[ "${mode}" == "non-array" && "$5" == "intelibill-dev-migrate" ]]; then
    printf '{"unexpected":"shape"}\n'
    exit 0
  fi
  if [[ "${mode}" == "workload-multiple-docs" && "$5" == "intelibill-dev-migrate" ]]; then
    printf '["20.10.0.3"]\n["203.0.113.99"]\n'
    exit 0
  fi
  case "$5" in
    intelibill-dev-migrate) printf '["20.10.0.3"]\n' ;;
    intelibill-prod-migrate) printf '["20.20.0.1"]\n' ;;
    *) exit 24 ;;
  esac
elif (($# == 10)) &&
  [[ "$1" == "postgres" ]] &&
  [[ "$2" == "flexible-server" && "$3" == "firewall-rule" && "$4" == "list" ]] &&
  [[ "$5" == "--resource-group" && "$6" == "intelibill-shared" ]] &&
  [[ "$7" == "--server-name" && "$8" == "intelibill-pg-01" ]] &&
  [[ "$9" == "--output" && "${10}" == "json" ]]; then
  if [[ "${mode}" == "firewall-failure" ]]; then
    printf 'sensitive firewall detail must not escape\n' >&2
    exit 26
  fi
  if [[ "${mode}" == "firewall-non-array" ]]; then
    printf '{"unexpected":"shape"}\n'
    exit 0
  fi
  printf '%s\n' \
    '[{"name":"container-apps-dev-20-10-0-1","startIpAddress":"20.10.0.1","endIpAddress":"20.10.0.1"},{"name":"container-apps-dev-20-10-0-2","startIpAddress":"20.10.0.2","endIpAddress":"20.10.0.2"},{"name":"container-apps-dev-20-10-0-3","startIpAddress":"20.10.0.3","endIpAddress":"20.10.0.3"},{"name":"container-apps-prod-20-20-0-1","startIpAddress":"20.20.0.1","endIpAddress":"20.20.0.1"}]'
  if [[ "${mode}" == "firewall-multiple-docs" ]]; then
    printf '%s\n' \
      '[{"name":"operator-anywhere","startIpAddress":"0.0.0.0","endIpAddress":"255.255.255.255"},{"name":"container-apps-legacy-198-51-100-7","startIpAddress":"198.51.100.7","endIpAddress":"198.51.100.7"}]'
  fi
else
  printf 'unexpected Azure CLI arguments\n' >&2
  exit 25
fi
SH
chmod +x "${TEST_TMP}/fake-bin/az"

AZ_CALL_LOG="${TEST_TMP}/az-calls"
AZ_EXPECTED_CALL_LOG="${TEST_TMP}/az-calls-expected"

append_expected_az_call() {
  local separator=""
  local argument
  for argument in "$@"; do
    printf '%s%s' "${separator}" "${argument}"
    separator=$'\t'
  done
  printf '\n'
}

append_expected_app_call() {
  append_expected_az_call \
    containerapp show \
    --name "$1" \
    --resource-group intelibill-shared \
    --query properties.outboundIpAddresses \
    --output json
}

append_expected_job_call() {
  append_expected_az_call \
    containerapp job show \
    --name "$1" \
    --resource-group intelibill-shared \
    --query properties.outboundIpAddresses \
    --output json
}

append_expected_firewall_call() {
  append_expected_az_call \
    postgres flexible-server firewall-rule list \
    --resource-group intelibill-shared \
    --server-name intelibill-pg-01 \
    --output json
}

expect_az_call_sequence() {
  local scenario="$1"
  local name="$2"
  : >"${AZ_EXPECTED_CALL_LOG}"
  case "${scenario}" in
    first-dev-app)
      append_expected_app_call intelibill-dev-api
      ;;
    dev-workloads)
      append_expected_app_call intelibill-dev-api
      append_expected_app_call intelibill-dev-web
      append_expected_job_call intelibill-dev-migrate
      ;;
    dev-full)
      append_expected_app_call intelibill-dev-api
      append_expected_app_call intelibill-dev-web
      append_expected_job_call intelibill-dev-migrate
      append_expected_firewall_call
      ;;
    all-full)
      append_expected_app_call intelibill-dev-api
      append_expected_app_call intelibill-dev-web
      append_expected_job_call intelibill-dev-migrate
      append_expected_app_call intelibill-prod-api
      append_expected_app_call intelibill-prod-web
      append_expected_job_call intelibill-prod-migrate
      append_expected_firewall_call
      ;;
    *)
      fail "unknown expected Azure call scenario: ${scenario}"
      ;;
  esac >"${AZ_EXPECTED_CALL_LOG}"

  if ! cmp -s "${AZ_EXPECTED_CALL_LOG}" "${AZ_CALL_LOG}"; then
    diff -u "${AZ_EXPECTED_CALL_LOG}" "${AZ_CALL_LOG}" >&2 || true
    fail "${name}"
  fi
  pass "${name}"
}

: >"${AZ_CALL_LOG}"
expect_success \
  "live discovery unions api web and migration arrays" \
  env AZ_CALL_LOG="${AZ_CALL_LOG}" PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  all-full \
  "full live discovery invokes each exact Azure CLI call once"

: >"${AZ_CALL_LOG}"
expect_success \
  "dev-only live discovery limits workload lookups" \
  env AZ_CALL_LOG="${AZ_CALL_LOG}" PATH="${TEST_TMP}/fake-bin:${PATH}" \
  "${CHECKER}" --environment dev
expect_az_call_sequence \
  dev-full \
  "dev-only live discovery invokes each exact Azure CLI call once"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live lookup failure fails closed" \
  env AZ_TEST_MODE=lookup-failure AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  first-dev-app \
  "failed first workload lookup records only the exact dev API call"
if rg -q 'sensitive lookup detail' "${TEST_TMP}/stdout" "${TEST_TMP}/stderr"; then
  fail "live lookup failure redacts Azure CLI diagnostics"
fi
pass "live lookup failure redacts Azure CLI diagnostics"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live non-array response fails closed" \
  env AZ_TEST_MODE=non-array AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  dev-workloads \
  "non-array migration response records each exact dev workload call once"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live workload multiple JSON documents fail closed" \
  env AZ_TEST_MODE=workload-multiple-docs AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  dev-workloads \
  "multiple-document migration response records each exact dev workload call once"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live firewall lookup failure fails closed" \
  env AZ_TEST_MODE=firewall-failure AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  all-full \
  "firewall failure follows each exact workload call once"
if rg -q 'sensitive firewall detail' "${TEST_TMP}/stdout" "${TEST_TMP}/stderr"; then
  fail "live firewall failure redacts Azure CLI diagnostics"
fi
pass "live firewall failure redacts Azure CLI diagnostics"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live firewall non-array response fails closed" \
  env AZ_TEST_MODE=firewall-non-array AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  all-full \
  "firewall non-array response follows each exact workload call once"

: >"${AZ_CALL_LOG}"
expect_failure \
  "live firewall multiple JSON documents fail closed" \
  env AZ_TEST_MODE=firewall-multiple-docs AZ_CALL_LOG="${AZ_CALL_LOG}" \
  PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
expect_az_call_sequence \
  all-full \
  "firewall multiple documents follow each exact workload call once"

printf '1..%d\n' "${pass_count}"
