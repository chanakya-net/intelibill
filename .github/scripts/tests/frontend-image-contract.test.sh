#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

# Every web image installs from the root Bun workspace: the lockfile and every
# workspace manifest arrive before the frozen install, so a shared package edit
# resolves the same way in both images.
check_workspace_install() {
  local name="$1" dockerfile="$2"
  local build_stage install_line manifest manifest_line
  build_stage="$(awk '/^FROM .* AS runtime$/ { exit } { print }' "${dockerfile}")"
  install_line="$(grep -nF 'RUN bun install --frozen-lockfile' <<<"${build_stage}" | cut -d: -f1)"
  [[ -n "${install_line}" ]] || fail "${name} image does not run a frozen root workspace install."
  grep -Fq 'curl -fsSL https://bun.sh/install | bash -s -- bun-v1.3.11' <<<"${build_stage}" \
    || fail "${name} image does not pin Bun 1.3.11."
  for manifest in 'package.json bun.lock' src/frontend/package.json src/admin-frontend/package.json \
    src/shared/frontend/api/package.json src/shared/frontend/ui/package.json; do
    manifest_line="$(grep -nF "COPY ${manifest}" <<<"${build_stage}" | head -1 | cut -d: -f1)"
    [[ -n "${manifest_line}" && "${manifest_line}" -lt "${install_line}" ]] \
      || fail "${name} image must copy ${manifest} before the frozen install."
  done
}

check_runtime() {
  local name="$1" dockerfile="$2" command="$3"
  local runtime_stage cleanup_line user_line
  runtime_stage="$(awk '/^FROM .* AS runtime$/ { in_runtime = 1 } in_runtime { print }' "${dockerfile}")"
  [[ -n "${runtime_stage}" ]] || fail "${name} Dockerfile is missing its runtime stage."
  grep -Eq '^FROM node:24-slim@sha256:[0-9a-f]{64} AS runtime$' <<<"${runtime_stage}" \
    || fail "${name} runtime must use the digest-pinned Node 24 image."
  grep -Fq 'rm -rf /usr/local/lib/node_modules/npm' <<<"${runtime_stage}" \
    || fail "${name} runtime image retains npm and its unused vulnerable dependency tree."
  cleanup_line="$(grep -nF 'rm -rf /usr/local/lib/node_modules/npm' <<<"${runtime_stage}" | cut -d: -f1)"
  user_line="$(grep -nE '^USER node$' <<<"${runtime_stage}" | cut -d: -f1)"
  [[ -n "${user_line}" && "${cleanup_line}" -lt "${user_line}" ]] \
    || fail "${name} runtime image must remove npm before switching to the node user."
  grep -Fq "${command}" <<<"${runtime_stage}" || fail "${name} runtime command changed: expected ${command}"
  if grep -Eq '^(COPY|ADD) .*node_modules' <<<"${runtime_stage}"; then
    fail "${name} runtime image must not ship node_modules."
  fi
}

merchant="${repository_root}/src/frontend/Dockerfile"
admin="${repository_root}/src/admin-frontend/Dockerfile"
[[ -f "${merchant}" ]] || fail "Frontend Dockerfile is missing: ${merchant}"
[[ -f "${admin}" ]] || fail "Admin web Dockerfile is missing: ${admin}"

check_workspace_install merchant "${merchant}"
check_runtime merchant "${merchant}" 'CMD ["node", "dist/INVENTORY/server/server.mjs"]'

check_workspace_install admin "${admin}"
check_runtime admin "${admin}" 'CMD ["node", "server.mjs"]'
grep -Fq 'COPY --from=build /app/src/admin-frontend/dist/ADMIN/browser /app/browser' "${admin}" \
  || fail "admin image must ship only the ADMIN browser output."
grep -Fq 'COPY src/admin-frontend/server.mjs /app/server.mjs' "${admin}" \
  || fail "admin image must ship its dependency-free runtime server."
if grep -Eq 'ngsw|service-worker' "${admin}"; then
  fail "admin image must not ship a service worker."
fi

printf 'Frontend image contract passed (merchant web, admin web).\n'
