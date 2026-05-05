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
AGENT_NAME_PREFIX="${AGENT_NAME_PREFIX:-codex-agent}"
HEARTBEAT_INTERVAL_SECONDS="${HEARTBEAT_INTERVAL_SECONDS:-15}"
COLOR_OUTPUT="${COLOR_OUTPUT:-auto}"

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

is_tty() {
  [[ -t 1 ]]
}

should_color() {
  case "${COLOR_OUTPUT}" in
    always) return 0 ;;
    never) return 1 ;;
    auto) is_tty ;;
    *) is_tty ;;
  esac
}

colorize_stream() {
  if ! should_color; then
    cat
    return
  fi

  awk '
    BEGIN {
      reset = "\033[0m"
      palette[1] = "\033[38;5;39m"
      palette[2] = "\033[38;5;46m"
      palette[3] = "\033[38;5;220m"
      palette[4] = "\033[38;5;198m"
      palette[5] = "\033[38;5;51m"
      palette[6] = "\033[38;5;208m"
      palette[7] = "\033[38;5;141m"
      palette[8] = "\033[38;5;82m"
      palette_count = 8
      next_palette = 1
      runner = "\033[1;38;5;250m"
    }

    function color_for_agent(agent,   c) {
      if (!(agent in agent_colors)) {
        agent_colors[agent] = palette[next_palette]
        next_palette++
        if (next_palette > palette_count) {
          next_palette = 1
        }
      }
      c = agent_colors[agent]
      return c
    }

    {
      line = $0

      if (line ~ /^STATUS\|/) {
        if (match(line, /\|agent=[^|]+/)) {
          agent = substr(line, RSTART + 7, RLENGTH - 7)
          c = color_for_agent(agent)
          print c line reset
        } else {
          print runner line reset
        }
      } else if (line ~ /^\[[0-9]{2}:[0-9]{2}:[0-9]{2}\]/ || line ~ /^== /) {
        print runner line reset
      } else {
        print line
      }

      fflush()
    }
  '
}

run_with_status() {
  local run_name="$1"
  shift

  : > "${OUTPUT_FILE}"

  "$@" > >(tee -a "${OUTPUT_FILE}" | colorize_stream) 2> >(tee -a "${OUTPUT_FILE}" | colorize_stream >&2) &
  local cmd_pid=$!
  local started_at
  started_at="$(date +%s)"

  echo "[$(date '+%H:%M:%S')] ${run_name}: started (pid=${cmd_pid})" >&2

  while kill -0 "${cmd_pid}" >/dev/null 2>&1; do
    sleep "${HEARTBEAT_INTERVAL_SECONDS}"

    if kill -0 "${cmd_pid}" >/dev/null 2>&1; then
      local now elapsed output_lines
      now="$(date +%s)"
      elapsed="$((now - started_at))"
      output_lines="$(wc -l < "${OUTPUT_FILE}" | tr -d ' ')"
      echo "[$(date '+%H:%M:%S')] ${run_name}: running (elapsed=${elapsed}s, output_lines=${output_lines})" >&2
    fi
  done

  wait "${cmd_pid}"
  local exit_code=$?
  local finished_at elapsed_total
  finished_at="$(date +%s)"
  elapsed_total="$((finished_at - started_at))"
  echo "[$(date '+%H:%M:%S')] ${run_name}: finished (exit=${exit_code}, elapsed=${elapsed_total}s)" >&2

  return "${exit_code}"
}

cd "${REPO_ROOT}"

for ((iteration = 1; iteration <= MAX_ITERATIONS; iteration++)); do
  run_name="${AGENT_NAME_PREFIX}-iter-${iteration}"
  echo "== Codex iteration ${iteration}/${MAX_ITERATIONS} [${run_name}] ==" >&2
  build_payload

  if [[ "${COPY_PROMPT}" == "1" ]] && command -v pbcopy >/dev/null 2>&1; then
    pbcopy < "${PAYLOAD_FILE}"
  fi

  if [[ "${PRINT_PROMPT}" == "1" ]]; then
    cat "${PAYLOAD_FILE}"
    exit 0
  fi

  run_with_status "${run_name}" codex exec -C "${REPO_ROOT}" ${CODEX_PERMISSION_MODE} ${CODEX_EXTRA_ARGS} "$(cat "${PAYLOAD_FILE}")"

  if grep -Fq "${STOP_MARKER}" "${OUTPUT_FILE}"; then
    exit 0
  fi
done

echo "Reached MAX_ITERATIONS=${MAX_ITERATIONS} without seeing ${STOP_MARKER}." >&2
exit 1
