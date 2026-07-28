#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

[[ "$#" -eq 3 ]] ||
  fail "usage: wait-for-container-app-revisions.sh RESOURCE_GROUP API_NAME WEB_NAME"

resource_group="$1"
api_name="$2"
web_name="$3"
max_polls="${MAX_POLLS:-60}"
poll_interval_seconds="${POLL_INTERVAL_SECONDS:-5}"

[[ -n "${resource_group}" ]] || fail "resource group must not be empty"
[[ -n "${api_name}" ]] || fail "API app name must not be empty"
[[ -n "${web_name}" ]] || fail "web app name must not be empty"
[[ "${max_polls}" =~ ^[1-9][0-9]*$ ]] || fail "MAX_POLLS must be a positive integer"
[[ "${poll_interval_seconds}" =~ ^[0-9]+$ ]] ||
  fail "POLL_INTERVAL_SECONDS must be a non-negative integer"
command -v az >/dev/null 2>&1 || fail "Azure CLI is required"
command -v curl >/dev/null 2>&1 || fail "curl is required"
command -v jq >/dev/null 2>&1 || fail "jq is required"

revision_query='[?properties.active].{name:name,health:properties.healthState,running:properties.runningState}'

healthy_revision() {
  jq -e '
    type == "array" and
    length == 1 and
    .[0].name != null and
    .[0].health == "Healthy" and
    (
      .[0].running == "Running" or
      .[0].running == "RunningAtMaxScale" or
      .[0].running == "ScaledToZero"
    )
  ' >/dev/null 2>&1
}

for ((poll = 1; poll <= max_polls; poll++)); do
  api_revisions="$(
    az containerapp revision list \
      --resource-group "${resource_group}" \
      --name "${api_name}" \
      --query "${revision_query}" \
      --output json 2>/dev/null || true
  )"
  web_revisions="$(
    az containerapp revision list \
      --resource-group "${resource_group}" \
      --name "${web_name}" \
      --query "${revision_query}" \
      --output json 2>/dev/null || true
  )"

  if healthy_revision <<<"${api_revisions}" &&
    healthy_revision <<<"${web_revisions}"; then
    web_fqdn="$(
      az containerapp show \
        --resource-group "${resource_group}" \
        --name "${web_name}" \
        --query properties.configuration.ingress.fqdn \
        --output tsv 2>/dev/null || true
    )"

    if [[ -n "${web_fqdn}" ]]; then
      root_status="$(
        curl \
          --silent \
          --show-error \
          --max-time 15 \
          --output /dev/null \
          --write-out '%{http_code}' \
          "https://${web_fqdn}/" 2>/dev/null || true
      )"
      ping_status="$(
        curl \
          --silent \
          --show-error \
          --max-time 15 \
          --output /dev/null \
          --write-out '%{http_code}' \
          "https://${web_fqdn}/api/ping" 2>/dev/null || true
      )"

      if [[ "${root_status}" == "200" && "${ping_status}" == "200" ]]; then
        printf 'Healthy API/web revisions and public routes verified at https://%s.\n' \
          "${web_fqdn}"
        exit 0
      fi
    fi
  fi

  if ((poll < max_polls && poll_interval_seconds > 0)); then
    sleep "${poll_interval_seconds}"
  fi
done

fail "API/web revisions or public routes did not become healthy within ${max_polls} polls"
