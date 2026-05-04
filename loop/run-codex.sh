#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROMPT_FILE="${1:-${PROMPT_FILE:-${SCRIPT_DIR}/prompt.md}}"
ISSUE_LIMIT="${ISSUE_LIMIT:-10}"
ISSUE_STATE="${ISSUE_STATE:-open}"
ISSUE_LABEL="${ISSUE_LABEL:-ready-for-agent}"
COMMITS_LIMIT="${COMMITS_LIMIT:-5}"
COPY_PROMPT="${COPY_PROMPT:-1}"
PRINT_PROMPT="${PRINT_PROMPT:-0}"
CODEX_PERMISSION_MODE="${CODEX_PERMISSION_MODE:---dangerously-bypass-approvals-and-sandbox}"
CODEX_EXTRA_ARGS="${CODEX_EXTRA_ARGS:-}"
MAX_ITERATIONS="${MAX_ITERATIONS:-20}"
STOP_MARKER="${STOP_MARKER:-<promise>NO MORE TASKS</promise>}"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_command git
require_command gh
require_command codex

if [[ ! -f "${PROMPT_FILE}" ]]; then
  echo "Prompt file not found: ${PROMPT_FILE}" >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "GitHub CLI is not authenticated. Run 'gh auth login' first." >&2
  exit 1
fi

COMMITS_FILE="$(mktemp -t intelibill-commits.XXXXXX)"
ISSUES_FILE="$(mktemp -t intelibill-issues.XXXXXX)"
PAYLOAD_FILE="$(mktemp -t intelibill-prompt.XXXXXX)"
OUTPUT_FILE="$(mktemp -t intelibill-output.XXXXXX)"

cleanup() {
  rm -f "${COMMITS_FILE}" "${ISSUES_FILE}" "${PAYLOAD_FILE}" "${OUTPUT_FILE}"
}

trap cleanup EXIT

build_payload() {
  : > "${COMMITS_FILE}"
  : > "${ISSUES_FILE}"

  git -C "${REPO_ROOT}" log -n "${COMMITS_LIMIT}" --date=short --format=$'%ad%n%B%n---' > "${COMMITS_FILE}" 2>/dev/null || true

  if [[ ! -s "${COMMITS_FILE}" ]]; then
    printf 'No recent commits found.\n' > "${COMMITS_FILE}"
  fi

  issue_args=(--state "${ISSUE_STATE}" --limit "${ISSUE_LIMIT}")
  if [[ -n "${ISSUE_LABEL}" ]]; then
    issue_args+=(--label "${ISSUE_LABEL}")
  fi

  issue_numbers="$(gh issue list "${issue_args[@]}" --json number --template '{{range .}}{{.number}}{{"\n"}}{{end}}')"

  if [[ -z "${issue_numbers}" && -n "${ISSUE_LABEL}" ]]; then
    echo "No issues found for label '${ISSUE_LABEL}'. Falling back to all open issues." >&2
    issue_numbers="$(gh issue list --state "${ISSUE_STATE}" --limit "${ISSUE_LIMIT}" --json number --template '{{range .}}{{.number}}{{"\n"}}{{end}}')"
  fi

  if [[ -z "${issue_numbers}" ]]; then
    printf 'No GitHub issues found.\n' > "${ISSUES_FILE}"
  else
    while IFS= read -r issue_number; do
      [[ -z "${issue_number}" ]] && continue

      {
        printf '===== ISSUE #%s =====\n' "${issue_number}"
        gh issue view "${issue_number}" --comments
        printf '\n\n'
      } >> "${ISSUES_FILE}"
    done <<< "${issue_numbers}"
  fi

  {
    printf 'Previous commits:\n\n'
    cat "${COMMITS_FILE}"
    printf '\nIssues:\n\n'
    cat "${ISSUES_FILE}"
    printf '\nInstructions:\n\n'
    cat "${PROMPT_FILE}"
    printf '\n'
  } > "${PAYLOAD_FILE}"
}

cd "${REPO_ROOT}"

for ((iteration = 1; iteration <= MAX_ITERATIONS; iteration++)); do
  echo "== Codex iteration ${iteration}/${MAX_ITERATIONS} ==" >&2
  build_payload

  if [[ "${COPY_PROMPT}" == "1" ]] && command -v pbcopy >/dev/null 2>&1; then
    pbcopy < "${PAYLOAD_FILE}"
  fi

  if [[ "${PRINT_PROMPT}" == "1" ]]; then
    cat "${PAYLOAD_FILE}"
    exit 0
  fi

  codex exec -C "${REPO_ROOT}" ${CODEX_PERMISSION_MODE} ${CODEX_EXTRA_ARGS} "$(cat "${PAYLOAD_FILE}")" | tee "${OUTPUT_FILE}"

  if grep -Fq "${STOP_MARKER}" "${OUTPUT_FILE}"; then
    exit 0
  fi
done

echo "Reached MAX_ITERATIONS=${MAX_ITERATIONS} without seeing ${STOP_MARKER}." >&2
exit 1
