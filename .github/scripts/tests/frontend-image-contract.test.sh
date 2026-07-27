#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
dockerfile="${repository_root}/src/frontend/Dockerfile"

[[ -f "${dockerfile}" ]] || {
  printf 'Frontend Dockerfile is missing: %s\n' "${dockerfile}" >&2
  exit 1
}

runtime_stage="$(
  awk '
    /^FROM .* AS runtime$/ { in_runtime = 1 }
    in_runtime { print }
  ' "${dockerfile}"
)"

[[ -n "${runtime_stage}" ]] || {
  printf 'Frontend Dockerfile is missing its runtime stage.\n' >&2
  exit 1
}

grep -Fq 'rm -rf /usr/local/lib/node_modules/npm' <<<"${runtime_stage}" || {
  printf 'Frontend runtime image retains npm and its unused vulnerable dependency tree.\n' >&2
  exit 1
}

cleanup_line="$(grep -nF 'rm -rf /usr/local/lib/node_modules/npm' <<<"${runtime_stage}" | cut -d: -f1)"
user_line="$(grep -nE '^USER node$' <<<"${runtime_stage}" | cut -d: -f1)"

[[ -n "${user_line}" && "${cleanup_line}" -lt "${user_line}" ]] || {
  printf 'Frontend runtime image must remove npm before switching to the node user.\n' >&2
  exit 1
}

printf 'Frontend image contract passed.\n'
