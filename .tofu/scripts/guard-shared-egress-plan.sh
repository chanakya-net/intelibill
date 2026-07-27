#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_ROOT="$(cd "${SCRIPT_DIR}/../envs/shared" && pwd)"
CHECKER="${SCRIPT_DIR}/check-container-app-egress.sh"

usage() {
  cat <<'EOF'
Usage: guard-shared-egress-plan.sh
  --plan-file FILE
  [--resource-group NAME]
  [--postgres-server NAME]
EOF
}

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

plan_file=""
resource_group="intelibill-shared"
postgres_server="intelibill-pg-01"

while (($# > 0)); do
  case "$1" in
    --plan-file|--resource-group|--postgres-server)
      (($# >= 2)) || fail "option $1 requires a value"
      option="$1"
      value="$2"
      [[ -n "${value}" ]] || fail "option ${option} requires a non-empty value"
      shift 2
      case "${option}" in
        --plan-file) plan_file="${value}" ;;
        --resource-group) resource_group="${value}" ;;
        --postgres-server) postgres_server="${value}" ;;
      esac
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "unknown option: $1"
      ;;
  esac
done

[[ -n "${plan_file}" ]] || fail "--plan-file is required"
[[ -r "${plan_file}" ]] || fail "saved plan is not readable: ${plan_file}"
[[ -x "${CHECKER}" ]] || fail "egress checker is not executable: ${CHECKER}"
command -v tofu >/dev/null 2>&1 || fail "OpenTofu is required"
command -v jq >/dev/null 2>&1 || fail "jq is required"

plan_directory="$(cd "$(dirname "${plan_file}")" && pwd)"
plan_file="${plan_directory}/$(basename "${plan_file}")"

work_dir="$(mktemp -d)"
cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT HUP INT TERM

if ! tofu "-chdir=${SHARED_ROOT}" show -json "${plan_file}" \
  >"${work_dir}/plan.json" 2>"${work_dir}/tofu-error"; then
  fail "OpenTofu could not render the saved shared plan"
fi

if ! jq -eSs '
  if
    length == 1 and
    (.[0] | type == "object") and
    (.[0].planned_values | type == "object") and
    (.[0].planned_values.root_module | type == "object")
  then
    [
      .[0].planned_values.root_module |
      .. |
      objects |
      select(
        .mode? == "managed" and
        .type? == "azurerm_postgresql_flexible_server_firewall_rule"
      ) |
      .values |
      {
        name: .name,
        startIpAddress: .start_ip_address,
        endIpAddress: .end_ip_address
      }
    ]
  else
    error("invalid saved-plan JSON")
  end
' "${work_dir}/plan.json" >"${work_dir}/planned-firewall.json" 2>/dev/null; then
  fail "saved plan does not contain valid planned values"
fi

checker_args=(
  --firewall-file "${work_dir}/planned-firewall.json"
  --resource-group "${resource_group}"
  --postgres-server "${postgres_server}"
)

"${CHECKER}" "${checker_args[@]}"
