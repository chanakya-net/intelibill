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

failure_override=""

compose() (
  validate_project_name
  local variable
  for variable in ${!COMPOSE_@}; do unset "${variable}"; done
  export INTELIBILL_CONTAINER_PREFIX="${project}"
  local args=(--env-file /dev/null -f "${repository_root}/docker-compose.yml" -p "${project}" --profile full)
  if [[ -n "${failure_override}" ]]; then
    args+=(-f "${failure_override}")
  fi
  case "$1" in
    config|ps) ;;
    *) guard_project "${args[@]}" ;;
  esac
  docker compose "${args[@]}" "$@"
)

validate_model() {
  jq -e --arg p "${project}" --arg db_scripts "${repository_root}/scripts/db" '
    .name == $p and
    .volumes == {postgres_data: {name: ($p + "_postgres_data")}} and
    (.networks | keys == ["default"] and .default.name == ($p + "_default") and
      (.default | del(.name) == {} or del(.name) == {ipam: {}})) and
    (.services | length > 0 and (to_entries | all(
      .key as $service | .value |
      .container_name == ($p + "_" + ($service | gsub("-"; "_"))) and
      (.networks == {default: null} or .networks == {default: {}}) and
      ((has("volumes_from") or has("network_mode") or has("external_links")) | not) and
      all(.volumes[]?;
        if $service == "postgres" then
          .type == "volume" and .source == "postgres_data" and .target == "/var/lib/postgresql/data"
        else
          .type == "bind" and .source == $db_scripts and .target == "/scripts" and .read_only == true
        end
      )
    )))
  ' >/dev/null
}

check_resource_labels() {
  local kind="$1" rows name owner
  rows="$(docker "${kind}" ls --filter "name=^${project}_" --format '{{.Name}}|{{.Label "com.docker.compose.project"}}')" \
    || fail "could not inspect fixture ${kind} ownership"
  while IFS='|' read -r name owner; do
    [[ -z "${name}" || "${owner}" == "${project}" ]] || fail "${kind} ${name} is owned by another project"
  done <<<"${rows}"
}

check_live_containers() {
  local by_name by_label id containers anonymous ids=()
  by_name="$(docker ps -aq --filter "name=^/${project}(_|-[a-z-]+-run-[a-f0-9]+$)")" \
    || fail "could not list fixture container names"
  by_label="$(docker ps -aq --filter "label=com.docker.compose.project=${project}")" || fail "could not list fixture container labels"
  while IFS= read -r id; do
    ids+=("${id}")
  done < <(printf '%s\n' "${by_name}" "${by_label}" | awk 'NF && !seen[$0]++')
  ((${#ids[@]} > 0)) || return 0
  # PostgreSQL's image declares an anonymous data volume for the psql jobs.
  containers="$(docker inspect "${ids[@]}")" || fail "could not inspect fixture containers"
  jq -e --arg p "${project}" --arg db_scripts "${repository_root}/scripts/db" '
    all(.[];
      (.Name | startswith("/" + $p + "_") or startswith("/" + $p + "-")) and
      .Config.Labels["com.docker.compose.project"] == $p and
      all(.NetworkSettings.Networks | keys[]; . == ($p + "_default")) and
      all(.Mounts[]?;
        if .Type == "bind" then
          .Source == $db_scripts and .Destination == "/scripts" and .RW == false
        else
          .Type == "volume" and .Destination == "/var/lib/postgresql/data" and
          (.Name == ($p + "_postgres_data") or (.Name | test("^[a-f0-9]{64}$")))
        end
      )
    )
  ' <<<"${containers}" >/dev/null || fail "existing fixture containers have foreign ownership, mounts or networks"
  anonymous="$(jq -r --arg named "${project}_postgres_data" \
    '[.[].Mounts[]? | select(.Type == "volume" and .Name != $named) | .Name] | unique[]' <<<"${containers}")"
  while IFS= read -r id; do
    [[ -z "${id}" ]] && continue
    docker volume inspect "${id}" --format '{{json .Labels}}' \
      | jq -e 'has("com.docker.volume.anonymous") and (has("com.docker.compose.project") | not)' >/dev/null \
      || fail "volume ${id} is not an anonymous PostgreSQL image volume"
  done <<<"${anonymous}"
}

# Refuse to touch anything this run does not positively own.
guard_project() {
  local model
  model="$(docker compose "$@" config --format json)" || fail "could not resolve fixture Compose model"
  validate_model <<<"${model}" || fail "resolved Compose model targets non-fixture containers, networks or volumes"
  check_resource_labels volume
  check_resource_labels network
  check_live_containers
}

validate_project_name() {
  [[ "${project}" =~ ^intelibill-p01-05-[a-z0-9-]+$ ]] \
    || fail "P0105_PROJECT must match intelibill-p01-05-*; got '${project}'"
}

# Host ports for this project; refuses ports something else already listens on.
assign_ports() {
  local base="$1" name port
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

stack_up() {
  local compose_log="${evidence}/${project}-compose.log" status
  log "compose up --build (${project})"
  if compose up -d --build >>"${compose_log}" 2>&1; then
    wait_stack
  else
    status=$?
    log "FAIL: compose up --build exit ${status}; see ${compose_log}"
    return "${status}"
  fi
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
