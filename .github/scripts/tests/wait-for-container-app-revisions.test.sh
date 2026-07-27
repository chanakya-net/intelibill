#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
helper="${script_dir}/../wait-for-container-app-revisions.sh"
test_tmp="$(mktemp -d)"
trap 'rm -rf -- "${test_tmp}"' EXIT

pass_count=0

pass() {
  printf 'ok - %s\n' "$1"
  pass_count=$((pass_count + 1))
}

fail() {
  printf 'not ok - %s\n' "$1" >&2
  if [[ -s "${test_tmp}/stderr" ]]; then
    sed 's/^/  stderr: /' "${test_tmp}/stderr" >&2
  fi
  if [[ -s "${test_tmp}/stdout" ]]; then
    sed 's/^/  stdout: /' "${test_tmp}/stdout" >&2
  fi
  exit 1
}

mkdir "${test_tmp}/fake-bin"

cat >"${test_tmp}/fake-bin/az" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

if [[ "$1 $2 $3" == "containerapp revision list" ]]; then
  name=""
  while (($# > 0)); do
    case "$1" in
      --name)
        name="$2"
        shift 2
        ;;
      *)
        shift
        ;;
    esac
  done

  case "${name}" in
    intelibill-dev-api) printf '%s\n' "${API_REVISIONS_JSON:?}" ;;
    intelibill-dev-web) printf '%s\n' "${WEB_REVISIONS_JSON:?}" ;;
    *) exit 91 ;;
  esac
elif [[ "$1 $2" == "containerapp show" ]]; then
  printf '%s\n' "${WEB_FQDN:?}"
else
  exit 92
fi
SH
chmod +x "${test_tmp}/fake-bin/az"

cat >"${test_tmp}/fake-bin/curl" <<'SH'
#!/usr/bin/env bash
set -euo pipefail

url="${!#}"
case "${url}" in
  https://intelibill-dev-web.example.test/)
    printf '%s' "${ROOT_HTTP_CODE:?}"
    ;;
  https://intelibill-dev-web.example.test/api/ping)
    printf '%s' "${PING_HTTP_CODE:?}"
    ;;
  *)
    exit 93
    ;;
esac
SH
chmod +x "${test_tmp}/fake-bin/curl"

healthy='[{"name":"revision-a","health":"Healthy","running":"Running"}]'
scaled_to_zero='[{"name":"revision-a","health":"Healthy","running":"ScaledToZero"}]'
multiple='[
  {"name":"revision-a","health":"Healthy","running":"Running"},
  {"name":"revision-b","health":"Healthy","running":"Running"}
]'
unhealthy='[{"name":"revision-a","health":"Unhealthy","running":"Running"}]'

run_case() {
  local api_json="$1"
  local web_json="$2"
  local root_code="$3"
  local ping_code="$4"

  : >"${test_tmp}/stdout"
  : >"${test_tmp}/stderr"

  set +e
  env \
    API_REVISIONS_JSON="${api_json}" \
    WEB_REVISIONS_JSON="${web_json}" \
    WEB_FQDN="intelibill-dev-web.example.test" \
    ROOT_HTTP_CODE="${root_code}" \
    PING_HTTP_CODE="${ping_code}" \
    MAX_POLLS=1 \
    POLL_INTERVAL_SECONDS=0 \
    PATH="${test_tmp}/fake-bin:${PATH}" \
    "${helper}" \
    intelibill-shared \
    intelibill-dev-api \
    intelibill-dev-web \
    >"${test_tmp}/stdout" 2>"${test_tmp}/stderr"
  case_status=$?
  set -e
}

expect_success() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -eq 0 ]] || fail "${name} (expected success, got ${case_status})"
  pass "${name}"
}

expect_failure() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -ne 0 ]] || fail "${name} (expected failure)"
  pass "${name}"
}

expect_success \
  "healthy revisions and public routes pass" \
  "${healthy}" "${healthy}" 200 200

expect_success \
  "healthy scale-to-zero revisions are woken by public routes" \
  "${scaled_to_zero}" "${scaled_to_zero}" 200 200

expect_failure \
  "multiple active API revisions fail closed" \
  "${multiple}" "${healthy}" 200 200

expect_failure \
  "unhealthy web revision fails closed" \
  "${healthy}" "${unhealthy}" 200 200

expect_failure \
  "failed public API proxy smoke fails release" \
  "${healthy}" "${healthy}" 200 503

printf '1..%s\n' "${pass_count}"
