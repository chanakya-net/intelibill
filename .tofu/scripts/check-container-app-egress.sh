#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: check-container-app-egress.sh
  [--advertised-file FILE]
  [--firewall-file FILE]
  [--retained-file FILE]
  [--environment dev|prod]
  [--resource-group NAME]
  [--postgres-server NAME]
EOF
}

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

advertised_file=""
firewall_file=""
retained_file=""
resource_group="intelibill-shared"
postgres_server="intelibill-pg-01"
environments=()

while (($# > 0)); do
  case "$1" in
    --advertised-file|--firewall-file|--retained-file|--environment|--resource-group|--postgres-server)
      (($# >= 2)) || fail "option $1 requires a value"
      option="$1"
      value="$2"
      [[ -n "${value}" ]] || fail "option ${option} requires a non-empty value"
      shift 2
      case "${option}" in
        --advertised-file) advertised_file="${value}" ;;
        --firewall-file) firewall_file="${value}" ;;
        --retained-file) retained_file="${value}" ;;
        --resource-group) resource_group="${value}" ;;
        --postgres-server) postgres_server="${value}" ;;
        --environment)
          [[ "${value}" == "dev" || "${value}" == "prod" ]] ||
            fail "invalid environment: ${value}"
          if ((${#environments[@]} > 0)); then
            for existing in "${environments[@]}"; do
              [[ "${existing}" != "${value}" ]] ||
                fail "environment may be selected only once: ${value}"
            done
          fi
          environments+=("${value}")
          ;;
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

if ((${#environments[@]} == 0)); then
  environments=(dev prod)
fi

command -v jq >/dev/null 2>&1 || fail "jq is required"

needs_azure=false
if [[ -z "${advertised_file}" || -z "${firewall_file}" ]]; then
  needs_azure=true
fi
if [[ "${needs_azure}" == true ]]; then
  command -v az >/dev/null 2>&1 || fail "Azure CLI is required for live discovery"
fi

for fixture in "${advertised_file}" "${firewall_file}" "${retained_file}"; do
  if [[ -n "${fixture}" && ! -r "${fixture}" ]]; then
    fail "input file is not readable: ${fixture}"
  fi
done

work_dir="$(mktemp -d)"
cleanup() {
  rm -rf -- "${work_dir}"
}
trap cleanup EXIT HUP INT TERM

environment_json="$(
  printf '%s\n' "${environments[@]}" |
    jq -Rsc 'split("\n") | map(select(length > 0))'
)"

run_azure_json() {
  local destination="$1"
  shift
  if ! az "$@" >"${destination}" 2>"${work_dir}/azure-error"; then
    fail "Azure lookup failed"
  fi
}

if [[ -z "${advertised_file}" ]]; then
  advertised_file="${work_dir}/advertised-live.json"
  jq -n '{dev: [], prod: []}' >"${advertised_file}"

  for environment in "${environments[@]}"; do
    for component in api web migrate; do
      lookup_file="${work_dir}/outbound-${environment}-${component}.json"
      if [[ "${component}" == "migrate" ]]; then
        run_azure_json \
          "${lookup_file}" \
          containerapp job show \
          --name "intelibill-${environment}-migrate" \
          --resource-group "${resource_group}" \
          --query properties.outboundIpAddresses \
          --output json
      else
        run_azure_json \
          "${lookup_file}" \
          containerapp show \
          --name "intelibill-${environment}-${component}" \
          --resource-group "${resource_group}" \
          --query properties.outboundIpAddresses \
          --output json
      fi

      jq -e 'type == "array"' "${lookup_file}" >/dev/null 2>&1 ||
        fail "Azure outbound-address lookup returned an invalid shape"

      jq -S \
        --arg environment "${environment}" \
        --slurpfile addresses "${lookup_file}" \
        '.[$environment] += $addresses[0]' \
        "${advertised_file}" >"${work_dir}/advertised-next.json" ||
        fail "Azure outbound-address lookup returned invalid JSON"
      mv "${work_dir}/advertised-next.json" "${advertised_file}"
    done
  done
fi

if [[ -z "${firewall_file}" ]]; then
  firewall_file="${work_dir}/firewall-live.json"
  run_azure_json \
    "${firewall_file}" \
    postgres flexible-server firewall-rule list \
    --resource-group "${resource_group}" \
    --name "${postgres_server}" \
    --output json
fi

if [[ -z "${retained_file}" ]]; then
  retained_file="${work_dir}/retained-empty.json"
  jq -n '{dev: [], prod: []}' >"${retained_file}"
fi

normalize_address_map() {
  local input_file="$1"
  local label="$2"
  local output_file="$3"

  if ! jq -eS \
    --argjson selected "${environment_json}" \
    '
      def canonical_ipv4:
        . as $ip |
        ($ip | type == "string") and
        ($ip | test("^(0|[1-9][0-9]{0,2})(\\.(0|[1-9][0-9]{0,2})){3}$")) and
        ($ip | split(".") | all(.[]; (tonumber >= 0 and tonumber <= 255))) and
        ($ip != "0.0.0.0") and
        ($ip != "255.255.255.255");

      if type != "object" then error("not an object") else . end |
      if any(keys[]; (["dev", "prod"] | index(.)) == null)
      then error("invalid environment key")
      else .
      end |
      if any(.[]; type != "array" or (all(.[]; canonical_ipv4) | not))
      then error("invalid address array")
      else .
      end |
      . as $input |
      reduce $selected[] as $environment
        ({}; .[$environment] = (($input[$environment] // []) | unique | sort))
    ' "${input_file}" >"${output_file}" 2>/dev/null; then
    fail "${label} input must contain canonical, non-sentinel IPv4 arrays keyed by dev or prod"
  fi
}

normalize_address_map \
  "${advertised_file}" \
  "advertised" \
  "${work_dir}/advertised-normalized.json"
normalize_address_map \
  "${retained_file}" \
  "retained" \
  "${work_dir}/retained-normalized.json"

advertised_count="$(
  jq '[.[] | .[]] | unique | length' "${work_dir}/advertised-normalized.json"
)"
((advertised_count > 0)) ||
  fail "the selected environments advertise no outbound IPv4 addresses"

if ! jq -eS 'if type == "array" then . else error("not an array") end' "${firewall_file}" \
  >"${work_dir}/firewall-normalized.json" 2>/dev/null; then
  fail "firewall input must be a JSON array"
fi

if ! jq -nS \
  --argjson environments "${environment_json}" \
  --slurpfile advertised "${work_dir}/advertised-normalized.json" \
  --slurpfile retained "${work_dir}/retained-normalized.json" \
  '
    [
      $environments[] as $environment |
      (
        ($advertised[0][$environment] // []) +
        ($retained[0][$environment] // [])
      )[] |
      {
        environment: $environment,
        ip: .,
        name: (
          "container-apps-" + $environment + "-" + (gsub("\\."; "-"))
        )
      }
    ] |
    unique_by(.name) |
    sort_by(.name)
  ' >"${work_dir}/expected.json"; then
  fail "could not derive the expected managed firewall rules"
fi

if ! jq -nS \
  --argjson environments "${environment_json}" \
  --slurpfile rules "${work_dir}/firewall-normalized.json" \
  --slurpfile expected "${work_dir}/expected.json" \
  '
    def canonical_ipv4:
      . as $ip |
      ($ip | type == "string") and
      ($ip | test("^(0|[1-9][0-9]{0,2})(\\.(0|[1-9][0-9]{0,2})){3}$")) and
      ($ip | split(".") | all(.[]; (tonumber >= 0 and tonumber <= 255))) and
      ($ip != "0.0.0.0") and
      ($ip != "255.255.255.255");

    def relevant_managed:
      . as $rule |
      ($rule.name | type == "string") and
      ($rule.name | startswith("container-apps-")) and
      (
        (["dev", "prod"] - $environments) as $unselected |
        all(
          $unselected[];
          . as $environment |
          ($rule.name | startswith("container-apps-" + $environment + "-") | not)
        )
      );

    def well_formed_managed:
      . as $rule |
      ($rule.startIpAddress | canonical_ipv4) and
      ($rule.endIpAddress | canonical_ipv4) and
      ($rule.startIpAddress == $rule.endIpAddress) and
      any(
        $environments[];
        . as $environment |
        $rule.name == (
          "container-apps-" +
          $environment +
          "-" +
          ($rule.startIpAddress | gsub("\\."; "-"))
        )
      );

    $rules[0] as $firewall |
    $expected[0] as $wanted |
    {
      missing: [
        $wanted[] as $item |
        select(
          any(
            $firewall[];
            .name == $item.name and
            .startIpAddress == $item.ip and
            .endIpAddress == $item.ip
          ) |
          not
        ) |
        $item.name
      ],
      broad: [
        $firewall[] |
        select(
          .startIpAddress == "0.0.0.0" or
          .startIpAddress == "255.255.255.255" or
          .endIpAddress == "0.0.0.0" or
          .endIpAddress == "255.255.255.255"
        ) |
        (.name // "<unnamed>" | tostring)
      ],
      malformed: [
        $firewall[] |
        select(relevant_managed) |
        select(well_formed_managed | not) |
        (.name // "<unnamed>" | tostring)
      ],
      stale: [
        $firewall[] |
        select(relevant_managed) |
        .name as $name |
        select(any($wanted[]; .name == $name) | not) |
        $name
      ],
      managed_count: [
        $firewall[] |
        select(relevant_managed)
      ] | length
    }
  ' >"${work_dir}/findings.json" 2>/dev/null; then
  fail "firewall input contains an invalid rule structure"
fi

has_findings=false
for finding in missing broad malformed stale; do
  finding_count="$(jq --arg finding "${finding}" '.[$finding] | length' "${work_dir}/findings.json")"
  if ((finding_count > 0)); then
    has_findings=true
    while IFS= read -r rule_name; do
      printf '%s: %s\n' "${finding}" "${rule_name}" >&2
    done < <(jq -r --arg finding "${finding}" '.[$finding][]' "${work_dir}/findings.json")
  fi
done

[[ "${has_findings}" == false ]] ||
  fail "Container Apps egress allowlist drift detected"

expected_count="$(jq 'length' "${work_dir}/expected.json")"
managed_count="$(jq '.managed_count' "${work_dir}/findings.json")"
printf 'Egress allowlist verified: %d expected address(es), %d managed rule(s).\n' \
  "${expected_count}" \
  "${managed_count}"
