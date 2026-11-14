#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
dockerignore="${repository_root}/.dockerignore"

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

[[ -f "${dockerignore}" ]] || fail "Repository Docker ignore file is missing: ${dockerignore}"
for local_settings in appsettings.Development.json appsettings.Local.json; do
  grep -Fqx "**/${local_settings}" "${dockerignore}" \
    || fail "Docker build context does not exclude local settings: ${local_settings}"
done

# Prints the Dockerfile instruction (RUN, ENV, ...) each line belongs to.
instruction_of_line() {
  awk -v target="$2" '
    /^[A-Z]+[[:space:]]/ && !continued { instruction = $1 }
    NR == target { print instruction; exit }
    { continued = ($0 ~ /\\[[:space:]]*$/) }
  ' "$1"
}

check_static() {
  local name="$1" dockerfile="$2" prefix="$3" password_policy="$4"
  local setting restore_count line

  [[ -f "${dockerfile}" ]] || fail "${name} migration Dockerfile is missing: ${dockerfile}"
  for setting in Host Database Username; do
    grep -Fq "${prefix}__${setting}=" "${dockerfile}" \
      || fail "${name} migration bundle build is missing design-time setting: ${prefix}__${setting}"
  done
  grep -Fq 'COPY ["global.json", "./"]' "${dockerfile}" || fail "${name} migration build ignores the SDK pin."
  grep -Fq -- '--target-runtime "${runtime_id}"' "${dockerfile}" \
    || fail "${name} migration bundle must target the requested runtime architecture."
  grep -Fq 'FROM --platform=$BUILDPLATFORM' "${dockerfile}" \
    || fail "${name} EF design-time code must run on BUILDPLATFORM."
  if grep -Eq -- '^[[:space:]]*--runtime[[:space:]]+"\$\{runtime_id\}"' "${dockerfile}"; then
    fail "${name} EF design-time runtime must remain on BUILDPLATFORM when cross-building the migration bundle."
  fi
  restore_count="$(grep -Ec '^[[:space:]]*(RUN|&&)[[:space:]]+dotnet restore' "${dockerfile}")"
  [[ "${restore_count}" -eq 1 ]] \
    || fail "${name} migration bundle should use the single BUILDPLATFORM restore, found: ${restore_count}"
  grep -Eq '^USER (\$APP_UID|1654|app)$' "${dockerfile}" || fail "${name} migration image must run as the app user."
  grep -Fq 'ENTRYPOINT ["/app/efbundle"]' "${dockerfile}" || fail "${name} migration image entrypoint changed."

  case "${password_policy}" in
    none)
      if grep -Fq "${prefix}__Password=" "${dockerfile}"; then
        fail "${name} migration bundle build must not store a password-shaped value in an image layer."
      fi
      ;;
    build-run-only)
      # The licensing design-time factory refuses an empty password, so the
      # bundle step passes a visibly non-secret value to that one RUN only.
      # An ENV/ARG would persist it into the build stage's configuration.
      grep -n "${prefix}__Password=" "${dockerfile}" | cut -d: -f1 | while read -r line; do
        [[ "$(instruction_of_line "${dockerfile}" "${line}")" == "RUN" ]] \
          || fail "${name} migration password placeholder must be scoped to the bundle RUN (line ${line})."
      done
      grep -Fq "${prefix}__Password=build-only-not-a-secret" "${dockerfile}" \
        || fail "${name} migration build placeholder must be visibly non-secret."
      ;;
  esac
}

check_image() {
  local name="$1" dockerfile="$2" image_tag="$3"
  local user entrypoint command env_values

  docker build --file "${dockerfile}" --tag "${image_tag}" "${repository_root}"

  user="$(docker image inspect "${image_tag}" --format '{{.Config.User}}')"
  entrypoint="$(docker image inspect "${image_tag}" --format '{{json .Config.Entrypoint}}')"
  command="$(docker image inspect "${image_tag}" --format '{{json .Config.Cmd}}')"
  env_values="$(docker image inspect "${image_tag}" --format '{{json .Config.Env}}')"

  [[ "${user}" == "1654" || "${user}" == "\$APP_UID" || "${user}" == "app" ]] \
    || fail "${name}: expected a non-root app user, got: ${user}"
  [[ "${entrypoint}" == '["/app/efbundle"]' ]] || fail "${name}: unexpected migration entrypoint: ${entrypoint}"
  [[ "${command}" == "null" || "${command}" == "[]" ]] \
    || fail "${name}: migration image must not inherit an API command: ${command}"
  if grep -Eq 'Database__|Password' <<<"${env_values}"; then
    fail "${name}: migration image carries database settings in its configuration: ${env_values}"
  fi
}

merchant="${repository_root}/src/backend/Dockerfile.migrate"
licensing="${repository_root}/src/licensing/Dockerfile.migrate"

check_static merchant "${merchant}" Database none
grep -Fq "Database__UseEntraAuth=" "${merchant}" \
  || fail "merchant migration bundle build is missing design-time setting: Database__UseEntraAuth"
check_static licensing "${licensing}" LicensingMigrationDatabase build-run-only
grep -Fq 'src/licensing/Intelibill.Licensing.Infrastructure/Intelibill.Licensing.Infrastructure.csproj' "${licensing}" \
  || fail "licensing migration bundle must build the licensing migrations assembly."
grep -Fq -- '--context LicensingDbContext' "${licensing}" \
  || fail "licensing migration bundle must name LicensingDbContext."

if ! docker info >/dev/null 2>&1; then
  fail 'Docker daemon unavailable; migration image build requires CI or a running local daemon.'
fi

docker_memory_bytes="$(docker info --format '{{.MemTotal}}')"
minimum_build_memory_bytes=$((2 * 1024 * 1024 * 1024))
if (( docker_memory_bytes < minimum_build_memory_bytes )); then
  docker buildx build --check --file "${merchant}" "${repository_root}"
  docker buildx build --check --file "${licensing}" "${repository_root}"
  printf 'Migration Dockerfile contracts passed; full image builds deferred because Docker has less than 2 GiB.\n'
  exit 0
fi

check_image merchant "${merchant}" intelibill-migrate-contract:local
check_image licensing "${licensing}" intelibill-licensing-migrate-contract:local

printf 'Migration image contract passed (merchant, licensing).\n'
