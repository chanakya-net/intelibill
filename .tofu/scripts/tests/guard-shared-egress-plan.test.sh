#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GUARD="${SCRIPT_DIR}/../guard-shared-egress-plan.sh"
FIXTURES="${SCRIPT_DIR}/fixtures"
SHARED_ROOT="$(cd "${SCRIPT_DIR}/../../envs/shared" && pwd)"
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
  [[ "${case_status}" -eq 0 ]] ||
    fail "${name} (expected success, got ${case_status})"
  pass "${name}"
}

expect_failure() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -ne 0 ]] ||
    fail "${name} (expected failure)"
  pass "${name}"
}

mkdir "${TEST_TMP}/fake-bin"
cat >"${TEST_TMP}/fake-bin/tofu" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

{
  separator=""
  for argument in "$@"; do
    printf '%s%s' "${separator}" "${argument}"
    separator=$'\t'
  done
  printf '\n'
} >>"${TOFU_CALL_LOG:?}"

if [[ "${TOFU_SHOW_MODE:-success}" == "failure" ]]; then
  printf 'sensitive planned value must not escape\n' >&2
  exit 23
fi

if (($# == 4)) &&
  [[ "$1" == "-chdir=${EXPECTED_SHARED_ROOT:?}" ]] &&
  [[ "$2" == "show" && "$3" == "-json" && "$4" == "${EXPECTED_PLAN_FILE:?}" ]]; then
  /bin/cat "${TOFU_PLAN_JSON_FILE:?}"
else
  printf 'unexpected OpenTofu arguments\n' >&2
  exit 24
fi
SH
chmod +x "${TEST_TMP}/fake-bin/tofu"

cat >"${TEST_TMP}/fake-bin/az" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

if (($# == 10)) &&
  [[ "$1" == "containerapp" && "$2" == "show" ]] &&
  [[ "$3" == "--name" ]] &&
  [[ "$5" == "--resource-group" && "$6" == "intelibill-shared" ]] &&
  [[ "$7" == "--query" && "$8" == "properties.outboundIpAddresses" ]] &&
  [[ "$9" == "--output" && "${10}" == "json" ]]; then
  case "$4" in
    intelibill-dev-api) printf '["20.10.0.1"]\n' ;;
    intelibill-dev-web) printf '["20.10.0.2"]\n' ;;
    intelibill-prod-api|intelibill-prod-web) printf '["20.20.0.1"]\n' ;;
    *) exit 24 ;;
  esac
elif (($# == 11)) &&
  [[ "$1" == "containerapp" && "$2" == "job" && "$3" == "show" ]] &&
  [[ "$4" == "--name" ]] &&
  [[ "$6" == "--resource-group" && "$7" == "intelibill-shared" ]] &&
  [[ "$8" == "--query" && "$9" == "properties.outboundIpAddresses" ]] &&
  [[ "${10}" == "--output" && "${11}" == "json" ]]; then
  case "$5" in
    intelibill-dev-migrate) printf '["20.10.0.3"]\n' ;;
    intelibill-prod-migrate) printf '["20.20.0.1"]\n' ;;
    *) exit 24 ;;
  esac
else
  printf 'unexpected Azure CLI arguments\n' >&2
  exit 25
fi
SH
chmod +x "${TEST_TMP}/fake-bin/az"

plan_file="${TEST_TMP}/shared.tfplan"
: >"${plan_file}"

cat >"${TEST_TMP}/plan-ok.json" <<'JSON'
{
  "planned_values": {
    "root_module": {
      "child_modules": [
        {
          "address": "module.database",
          "resources": [
            {
              "mode": "managed",
              "type": "azurerm_postgresql_flexible_server_firewall_rule",
              "name": "allowed",
              "values": {
                "name": "container-apps-dev-20-10-0-1",
                "start_ip_address": "20.10.0.1",
                "end_ip_address": "20.10.0.1"
              }
            },
            {
              "mode": "managed",
              "type": "azurerm_postgresql_flexible_server_firewall_rule",
              "name": "allowed",
              "values": {
                "name": "container-apps-dev-20-10-0-2",
                "start_ip_address": "20.10.0.2",
                "end_ip_address": "20.10.0.2"
              }
            },
            {
              "mode": "managed",
              "type": "azurerm_postgresql_flexible_server_firewall_rule",
              "name": "allowed",
              "values": {
                "name": "container-apps-dev-20-10-0-3",
                "start_ip_address": "20.10.0.3",
                "end_ip_address": "20.10.0.3"
              }
            },
            {
              "mode": "managed",
              "type": "azurerm_postgresql_flexible_server_firewall_rule",
              "name": "allowed",
              "values": {
                "name": "container-apps-prod-20-20-0-1",
                "start_ip_address": "20.20.0.1",
                "end_ip_address": "20.20.0.1"
              }
            }
          ]
        }
      ]
    }
  }
}
JSON

cat >"${TEST_TMP}/plan-dev-missing.json" <<'JSON'
{
  "planned_values": {
    "root_module": {
      "child_modules": [
        {
          "address": "module.database",
          "resources": [
            {
              "mode": "managed",
              "type": "azurerm_postgresql_flexible_server_firewall_rule",
              "name": "allowed",
              "values": {
                "name": "container-apps-prod-20-20-0-1",
                "start_ip_address": "20.20.0.1",
                "end_ip_address": "20.20.0.1"
              }
            }
          ]
        }
      ]
    }
  }
}
JSON

jq \
  '.planned_values.root_module.child_modules[0].resources += [{
    "mode": "managed",
    "type": "azurerm_postgresql_flexible_server_firewall_rule",
    "name": "allowed",
    "values": {
      "name": "operator-anywhere",
      "start_ip_address": "0.0.0.0",
      "end_ip_address": "255.255.255.255"
    }
  }]' \
  "${TEST_TMP}/plan-ok.json" >"${TEST_TMP}/plan-broad.json"

cat >"${TEST_TMP}/plan-invalid.json" <<'JSON'
{"format_version":"1.2"}
JSON

tofu_call_log="${TEST_TMP}/tofu-calls"

run_guard() {
  local plan_json="$1"
  shift
  env \
    EXPECTED_PLAN_FILE="${plan_file}" \
    EXPECTED_SHARED_ROOT="${SHARED_ROOT}" \
    TOFU_CALL_LOG="${tofu_call_log}" \
    TOFU_PLAN_JSON_FILE="${plan_json}" \
    PATH="${TEST_TMP}/fake-bin:${PATH}" \
    "${GUARD}" \
    --plan-file "${plan_file}" \
    "$@"
}

: >"${tofu_call_log}"
expect_success \
  "matching final saved-plan firewall inventory passes" \
  run_guard "${TEST_TMP}/plan-ok.json"
[[ "$(<"${tofu_call_log}")" == "-chdir=${SHARED_ROOT}"$'\tshow\t-json\t'"${plan_file}" ]] ||
  fail "guard invokes only the exact OpenTofu show command"
pass "guard invokes only the exact OpenTofu show command"

: >"${tofu_call_log}"
expect_failure \
  "saved plan missing an environment inventory fails" \
  run_guard "${TEST_TMP}/plan-dev-missing.json"

: >"${tofu_call_log}"
expect_failure \
  "saved plan containing a broad rule fails" \
  run_guard "${TEST_TMP}/plan-broad.json"

: >"${tofu_call_log}"
expect_failure \
  "saved plan without planned values fails closed" \
  run_guard "${TEST_TMP}/plan-invalid.json"

: >"${tofu_call_log}"
expect_failure \
  "normal plan guard rejects retained transition input" \
  run_guard "${TEST_TMP}/plan-ok.json" \
  --retained-file "${FIXTURES}/retained.json"

: >"${tofu_call_log}"
expect_failure \
  "normal plan guard cannot override live advertised discovery" \
  run_guard "${TEST_TMP}/plan-ok.json" \
  --advertised-file "${FIXTURES}/advertised-ok.json"

: >"${tofu_call_log}"
expect_failure \
  "OpenTofu show failure fails closed" \
  env \
    EXPECTED_PLAN_FILE="${plan_file}" \
    EXPECTED_SHARED_ROOT="${SHARED_ROOT}" \
    TOFU_CALL_LOG="${tofu_call_log}" \
    TOFU_PLAN_JSON_FILE="${TEST_TMP}/plan-ok.json" \
    TOFU_SHOW_MODE=failure \
    PATH="${TEST_TMP}/fake-bin:${PATH}" \
    "${GUARD}" \
    --plan-file "${plan_file}"
if rg -q 'sensitive planned value' "${TEST_TMP}/stdout" "${TEST_TMP}/stderr"; then
  fail "OpenTofu show failure redacts diagnostics"
fi
pass "OpenTofu show failure redacts diagnostics"

printf '1..%d\n' "${pass_count}"
