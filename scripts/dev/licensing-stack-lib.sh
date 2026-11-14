# Helpers for scripts/dev/test-licensing-stack.sh. Sourced, not executed.
#
# Every helper acts on the isolated Compose project named by P0105_PROJECT; the
# entry script refuses any other name before anything here runs.

log() { printf '[p01-05] %s\n' "$*" | tee -a "${evidence}/run.log" >&2; }
fail() {
  log "FAIL: $*"
  exit 1
}
pass() { log "PASS: $*"; }

compose() {
  docker compose -p "${project}" --profile full "$@"
}

# Refuse to touch anything this run does not positively own.
guard_project() {
  [[ "${project}" =~ ^intelibill-p01-05-[a-z0-9-]+$ ]] \
    || fail "P0105_PROJECT must match intelibill-p01-05-*; got '${project}'"
  local foreign
  foreign="$(docker ps -a --filter "name=^${project}_" --format '{{.Names}} {{.Label "com.docker.compose.project"}}' \
    | awk -v p="${project}" '$2 != p { print $1 }')"
  [[ -z "${foreign}" ]] || fail "containers named ${project}_* belong to another project: ${foreign}"
  foreign="$(docker volume ls --filter "name=^${project}_" --format '{{.Name}} {{.Label "com.docker.compose.project"}}' \
    | awk -v p="${project}" '$2 != p { print $1 }')"
  [[ -z "${foreign}" ]] || fail "volumes named ${project}_* belong to another project: ${foreign}"
}

# Host ports for this project; refuses ports something else already listens on.
assign_ports() {
  local base="$1" name port
  export INTELIBILL_CONTAINER_PREFIX="${project}"
  export INTELIBILL_POSTGRES_PORT="127.0.0.1:$((base + 432))"
  export INTELIBILL_MERCHANT_API_PORT="$((base + 277))"
  export INTELIBILL_WEB_PORT="$((base + 0))"
  export INTELIBILL_LICENSING_API_PORT="$((base + 278))"
  export INTELIBILL_ADMIN_WEB_PORT="$((base + 1))"
  export INTELIBILL_MAILPIT_SMTP_PORT="$((base + 25))"
  export INTELIBILL_MAILPIT_UI_PORT="$((base + 825))"
  merchant_api="http://localhost:${INTELIBILL_MERCHANT_API_PORT}"
  merchant_web="http://localhost:${INTELIBILL_WEB_PORT}"
  licensing_api="http://localhost:${INTELIBILL_LICENSING_API_PORT}"
  admin_web="http://localhost:${INTELIBILL_ADMIN_WEB_PORT}"
  mailpit_ui="http://localhost:${INTELIBILL_MAILPIT_UI_PORT}"
  if [[ -z "$(compose ps -q 2>/dev/null)" ]]; then
    for name in MERCHANT_API WEB LICENSING_API ADMIN_WEB MAILPIT_SMTP MAILPIT_UI; do
      port="INTELIBILL_${name}_PORT"
      port="${!port}"
      ! lsof -nP -iTCP:"${port}" -sTCP:LISTEN >/dev/null 2>&1 || fail "host port ${port} is already in use"
    done
  fi
}

container_of() { compose ps -a -q "$1" 2>/dev/null | head -1; }

state_of() {
  local id
  id="$(container_of "$1")"
  [[ -n "${id}" ]] || {
    echo absent
    return
  }
  docker inspect -f '{{.State.Status}} {{.State.ExitCode}} {{if .State.Health}}{{.State.Health.Status}}{{end}} {{.State.StartedAt}}' "${id}"
}

wait_for() {
  local service="$1" want="$2" deadline=$((SECONDS + ${3:-300})) state
  while ((SECONDS < deadline)); do
    state="$(state_of "${service}")"
    case "${want}:${state}" in
      healthy:"running 0 healthy"*) return 0 ;;
      exited0:"exited 0 "*) return 0 ;;
      exited0:"exited "*) fail "${service} exited nonzero: ${state}" ;;
    esac
    sleep 3
  done
  fail "${service} did not become ${want} (last state: ${state})"
}

wait_stack() {
  local job
  for job in db-bootstrap merchant-migrate db-runtime-role licensing-migrate licensing-runtime-role; do
    wait_for "${job}" exited0
  done
  wait_for backend healthy
  wait_for licensing-api healthy
  wait_http "${merchant_web}/" 200
  wait_http "${admin_web}/" 200
}

http_status() { curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$1" || true; }

wait_http() {
  local url="$1" want="$2" deadline=$((SECONDS + 120)) got
  while ((SECONDS < deadline)); do
    got="$(http_status "${url}")"
    [[ "${got}" == "${want}" ]] && return 0
    sleep 2
  done
  fail "${url} answered ${got}, expected ${want}"
}

expect_http() {
  local url="$1" want="$2" got
  got="$(http_status "${url}")"
  [[ "${got}" == "${want}" ]] || fail "${url} answered ${got}, expected ${want}"
  pass "${url} -> ${got}"
}

# psql as the compose superuser inside the fixture's own PostgreSQL container.
sql() {
  local database="$1"
  shift
  compose exec -T postgres psql -X -q -t -A -v ON_ERROR_STOP=1 -U intelibill -d "${database}" "$@"
}

# psql as an arbitrary login over TCP inside the container, so CONNECT
# privileges (not peer trust shortcuts) decide; prints psql's error on failure.
sql_as() {
  local user="$1" password="$2" database="$3" statement="$4"
  compose exec -T -e PGPASSWORD="${password}" postgres \
    psql -X -q -t -A -v ON_ERROR_STOP=1 -h 127.0.0.1 -U "${user}" -d "${database}" -c "${statement}" 2>&1
}

expect_denied() {
  local label="$1" pattern="$2" output
  shift 2
  if output="$(sql_as "$@")"; then
    fail "${label}: succeeded but must be refused (${output})"
  fi
  grep -Eqi "${pattern}" <<<"${output}" || fail "${label}: refused for an unexpected reason: ${output}"
  pass "${label}: ${output//$'\n'/ }"
}

record() {
  printf '%s\n' "$2" >"${evidence}/$1"
}
