#!/usr/bin/env bash
# Exercise real Compose resolution through a Docker stub that cannot mutate.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT
export REAL_DOCKER="$(command -v docker)" RECORDS="${scratch}/mutations"
export P0105_PROJECT=intelibill-p01-05-harness-contract
export P0105_ARTIFACTS="${P0105_HARNESS_TEST_ARTIFACTS:-${scratch}/evidence}"
fixture="${scratch}/fixture"
mkdir -p "${scratch}/bin" "${fixture}/scripts/dev"
cp "${root}/scripts/dev/"*licensing-stack*.sh "${fixture}/scripts/dev/"
cp "${root}/docker-compose.yml" "${fixture}/docker-compose.yml"
export PATH="${scratch}/bin:${PATH}"

cat >"${scratch}/bin/docker" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  ps|network) exit 0 ;;
  volume)
    if [[ "${FOREIGN_LABEL:-}" == yes ]]; then
      printf '%s|foreign\n' "${P0105_PROJECT}_postgres_data"
    fi
    exit 0
    ;;
  compose) shift ;;
  *) exit 99 ;;
esac
args=()
while (($#)); do
  case "$1" in
    -p|--profile|-f|--env-file|--project-directory) args+=("$1" "$2"); shift 2 ;;
    ps) exit 0 ;;
    config) exec "${REAL_DOCKER}" compose "${args[@]}" "$@" ;;
    up|down)
      printf '%s\n' "$1" >>"${RECORDS}"
      if [[ ! -e "${RECORDS}-$1.json" ]]; then
        "${REAL_DOCKER}" compose "${args[@]}" config --format json >"${RECORDS}-$1.json"
      fi
      [[ "$1" == down ]] && exit 0
      exit 17
      ;;
    *) printf '%s\n' "$1" >>"${RECORDS}"; exit 99 ;;
  esac
done
exit 99
STUB
chmod +x "${scratch}/bin/docker"
for helper in lsof sleep; do
  printf '#!/usr/bin/env bash\nexit 1\n' >"${scratch}/bin/${helper}"
  chmod +x "${scratch}/bin/${helper}"
done

case_status() {
  local status=0
  rm -f "${P0105_ARTIFACTS}/run.log"
  bash "${fixture}/scripts/dev/test-licensing-stack.sh" --case "$1" >"${scratch}/case.log" 2>&1 || status=$?
  return "${status}"
}

ambient_overrides() {
  cat >"${fixture}/docker-compose.override.yml" <<'YAML'
volumes:
  postgres_data:
    name: foreign_developer_data
YAML
  printf 'COMPOSE_FILE=docker-compose.yml:docker-compose.override.yml\nINTELIBILL_CONTAINER_PREFIX=foreign\n' >"${fixture}/.env"
  local case_name verb
  for case_name in fresh cleanup; do
    verb=up
    [[ "${case_name}" == fresh ]] || verb=down
    rm -f "${RECORDS}" "${RECORDS}-${verb}.json"
    # Exercise both explicit ambient settings and auto-discovery of .env/override.
    COMPOSE_FILE="${fixture}/docker-compose.yml:${fixture}/docker-compose.override.yml" \
      COMPOSE_ENV_FILES="${fixture}/.env" INTELIBILL_CONTAINER_PREFIX=foreign case_status "${case_name}" || true
    assert_isolated_or_refused "${verb}"
    rm -f "${RECORDS}" "${RECORDS}-${verb}.json"
    case_status "${case_name}" || true
    assert_isolated_or_refused "${verb}"
  done
  printf 'Ambient Compose files and .env cannot redirect fresh startup or cleanup.\n'
}

assert_isolated_or_refused() {
  local verb="$1"
  if [[ -e "${RECORDS}" ]]; then
    [[ "$(sort -u "${RECORDS}")" == "${verb}" ]]
    jq -e --arg project "${P0105_PROJECT}" '
      .name == $project and
      .networks.default.name == ($project + "_default") and
      .volumes.postgres_data.name == ($project + "_postgres_data") and
      .services.postgres.container_name == ($project + "_postgres")
    ' "${RECORDS}-${verb}.json" >/dev/null || {
      printf 'FAIL: %s reached a mutating Docker call with non-fixture resources\n' "${verb}" >&2
      exit 1
    }
  else
    grep -q 'FAIL:' "${P0105_ARTIFACTS}/run.log"
  fi
}

foreign_resources() {
  local case_name
  rm -f "${RECORDS}" "${fixture}/.env" "${fixture}/docker-compose.override.yml"
  printf '    name: foreign_developer_data\n' >>"${fixture}/docker-compose.yml"
  for case_name in fresh cleanup; do
    if case_status "${case_name}" || [[ -e "${RECORDS}" ]]; then
      printf 'FAIL: %s allowed a foreign volume in the resolved base model\n' "${case_name}" >&2
      exit 1
    fi
    grep -q 'resolved Compose model targets non-fixture' "${P0105_ARTIFACTS}/run.log"
  done
  cp "${root}/docker-compose.yml" "${fixture}/docker-compose.yml"
  for case_name in fresh cleanup; do
    if FOREIGN_LABEL=yes case_status "${case_name}" || [[ -e "${RECORDS}" ]]; then
      printf 'FAIL: %s allowed a fixture-named volume with a foreign project label\n' "${case_name}" >&2
      exit 1
    fi
    grep -q 'owned by another project' "${P0105_ARTIFACTS}/run.log"
  done
  printf 'Foreign resolved volumes and ownership labels block startup and cleanup before mutation.\n'
}

failed_build() (
  source "${root}/scripts/dev/licensing-stack-lib.sh"
  evidence="${P0105_ARTIFACTS}"
  project="${P0105_PROJECT}"
  mkdir -p "${evidence}"
  compose() { printf 'simulated compose build failure\n' >&2; return 17; }
  wait_stack() { touch "${scratch}/waited"; return 0; }
  local status=0 compose_log="${evidence}/${project}-compose.log"
  stack_up 2>"${scratch}/stack-up.log" || status=$?
  [[ "${status}" -eq 17 && ! -e "${scratch}/waited" ]] || {
    printf 'FAIL: compose exit 17 became stack_up exit %s\n' "${status}" >&2
    exit 1
  }
  grep -q 'simulated compose build failure' "${compose_log}"
  grep -Fq "exit 17" "${scratch}/stack-up.log"
  grep -Fq "${compose_log}" "${scratch}/stack-up.log"
  printf 'Compose exit 17 propagated before health inspection. Failure log: %s\n' "${compose_log}"
  cat "${scratch}/stack-up.log"
)

case "${1:-all}" in
  ambient) ambient_overrides ;;
  foreign) foreign_resources ;;
  build) failed_build ;;
  all) ambient_overrides; foreign_resources; failed_build ;;
  *) exit 2 ;;
esac
