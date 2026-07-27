#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
dockerfile="${repository_root}/src/backend/Dockerfile.migrate"
image_tag="intelibill-migrate-contract:local"

[[ -f "${dockerfile}" ]] || {
  printf 'Migration Dockerfile is missing: %s\n' "${dockerfile}" >&2
  exit 1
}

if ! docker info >/dev/null 2>&1; then
  printf 'Docker daemon unavailable; migration image build requires CI or a running local daemon.\n' >&2
  exit 1
fi

docker_memory_bytes="$(docker info --format '{{.MemTotal}}')"
minimum_build_memory_bytes=$((2 * 1024 * 1024 * 1024))
if (( docker_memory_bytes < minimum_build_memory_bytes )); then
  docker buildx build \
    --check \
    --file "${dockerfile}" \
    "${repository_root}"
  grep -Eq '^USER (\$APP_UID|1654|app)$' "${dockerfile}"
  grep -Fq 'ENTRYPOINT ["/app/efbundle"]' "${dockerfile}"
  printf 'Migration Dockerfile contract passed; full image build deferred because Docker has less than 2 GiB.\n'
  exit 0
fi

docker build \
  --file "${dockerfile}" \
  --tag "${image_tag}" \
  "${repository_root}"

user="$(docker image inspect "${image_tag}" --format '{{.Config.User}}')"
entrypoint="$(docker image inspect "${image_tag}" --format '{{json .Config.Entrypoint}}')"
command="$(docker image inspect "${image_tag}" --format '{{json .Config.Cmd}}')"

[[ "${user}" == "1654" || "${user}" == "\$APP_UID" || "${user}" == "app" ]] || {
  printf 'Expected a non-root app user, got: %s\n' "${user}" >&2
  exit 1
}
[[ "${entrypoint}" == '["/app/efbundle"]' ]] || {
  printf 'Unexpected migration entrypoint: %s\n' "${entrypoint}" >&2
  exit 1
}
[[ "${command}" == "null" || "${command}" == "[]" ]] || {
  printf 'Migration image must not inherit an API command: %s\n' "${command}" >&2
  exit 1
}

printf 'Migration image contract passed.\n'
