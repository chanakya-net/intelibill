#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

[[ "$#" -eq 3 ]] ||
  fail "usage: wait-for-container-app-job.sh RESOURCE_GROUP JOB_NAME EXECUTION_NAME"

resource_group="$1"
job_name="$2"
execution_name="$3"
max_polls="${MAX_POLLS:-120}"
poll_interval_seconds="${POLL_INTERVAL_SECONDS:-5}"

[[ -n "${resource_group}" ]] || fail "resource group must not be empty"
[[ -n "${job_name}" ]] || fail "job name must not be empty"
[[ -n "${execution_name}" ]] || fail "execution name must not be empty"
[[ "${max_polls}" =~ ^[1-9][0-9]*$ ]] || fail "MAX_POLLS must be a positive integer"
[[ "${poll_interval_seconds}" =~ ^[0-9]+$ ]] ||
  fail "POLL_INTERVAL_SECONDS must be a non-negative integer"
command -v az >/dev/null 2>&1 || fail "Azure CLI is required"

for ((poll = 1; poll <= max_polls; poll++)); do
  if ! execution_status="$(
    az containerapp job execution show \
      --resource-group "${resource_group}" \
      --name "${job_name}" \
      --job-execution-name "${execution_name}" \
      --query properties.status \
      --output tsv 2>/dev/null
  )"; then
    fail "could not read migration execution status"
  fi

  execution_status="${execution_status//$'\r'/}"
  case "${execution_status}" in
    Succeeded)
      printf 'Migration execution %s succeeded.\n' "${execution_name}"
      exit 0
      ;;
    Running|Processing|Pending)
      ;;
    "")
      fail "migration execution returned an empty status"
      ;;
    *)
      fail "migration execution ${execution_name} entered terminal status ${execution_status}"
      ;;
  esac

  if ((poll < max_polls && poll_interval_seconds > 0)); then
    sleep "${poll_interval_seconds}"
  fi
done

fail "migration execution ${execution_name} did not finish within ${max_polls} polls"
