# Cases for scripts/dev/test-licensing-stack.sh beyond the fresh stack. Sourced.

# Removes a disposable project this run created, after the ownership guard.
discard_project() {
  guard_project
  compose down -v --remove-orphans >>"${evidence}/${project}-compose.log" 2>&1
  log "removed disposable project ${project} and its own volumes"
}

# An existing developer volume from before #835: PostgreSQL alone, the merchant
# schema applied by hand, the old runtime role, and merchant data.
case_reused() {
  project="${main_project}-reused"
  guard_project
  assign_ports 44000
  compose up -d postgres >>"${evidence}/${project}-compose.log" 2>&1
  wait_for postgres healthy
  compose run --rm --no-deps merchant-migrate >>"${evidence}/${project}-compose.log" 2>&1
  compose run --rm --no-deps db-runtime-role >>"${evidence}/${project}-compose.log" 2>&1
  sql intelibill_dev -c "create table p0105_sentinel(note text primary key); insert into p0105_sentinel values ('merchant-before-licensing')" >/dev/null
  local history
  history="$(sql intelibill_dev -c 'select count(*) from "__EFMigrationsHistory"')"

  local refused
  if refused="$(compose run --rm --no-deps --entrypoint psql db-bootstrap -X --host=postgres --username=intelibill \
    --dbname=postgres --set=environment=Production --set=stage=bootstrap --file=/scripts/local-licensing-role.sql 2>&1)"; then
    fail "local preparation ran with environment=Production"
  fi
  grep -q 'local Development/Testing stacks only' <<<"${refused}" || fail "unexpected refusal: ${refused}"
  [[ "$(sql postgres -c "select count(*) from pg_roles where rolname like 'intelibill_licensing%'")" == 0 ]] \
    || fail "refused preparation still created roles"
  pass "preparation refuses environment=Production before any DDL"

  local first second
  compose run --rm --no-deps db-bootstrap >"${evidence}/reused-bootstrap-1.log" 2>&1 &
  first=$!
  compose run --rm --no-deps db-bootstrap >"${evidence}/reused-bootstrap-2.log" 2>&1 &
  second=$!
  wait "${first}" || fail "concurrent bootstrap #1 failed"
  wait "${second}" || fail "concurrent bootstrap #2 failed"
  pass "two concurrent bootstraps on the merchant-only volume both exit 0"

  stack_up
  check_probes
  [[ "$(sql intelibill_dev -c 'select note from p0105_sentinel')" == merchant-before-licensing ]] || fail "merchant data lost"
  [[ "$(sql intelibill_dev -c 'select count(*) from "__EFMigrationsHistory"')" == "${history}" ]] || fail "merchant history changed"
  [[ "$(sql postgres -c "select pg_get_userbyid(datdba) from pg_database where datname = 'intelibill_dev'")" == intelibill ]] \
    || fail "merchant database ownership changed"
  sql_as intelibill_app intelibill_app_password intelibill_dev 'select note from p0105_sentinel' | grep -q merchant-before-licensing \
    || fail "merchant runtime lost access to existing data"
  pass "existing merchant volume kept its data (${history} migrations), owner and runtime access; licensing added beside it"
  discard_project
}

assert_never_started() {
  local service state
  for service in "$@"; do
    state="$(state_of "${service}")"
    [[ "${state}" == absent || "${state}" == "created "* ]] || fail "${service} started after a failed migration: ${state}"
  done
}

# A migrator that exits nonzero must keep its own API and web from ever
# starting. `docker compose up` reports the failure and stops starting further
# services, so the other branch is then started alone: its dependency chain
# never includes the failed job. A later full `up` that fails again leaves the
# running branch serving.
run_failure() {
  local broken="$1" base="$2" sibling_web="$3" sibling_api="$4" sibling_url_var="$5"
  shift 5
  local blocked=("$@") override
  project="${main_project}-fail-${broken%%-*}"
  guard_project
  assign_ports "${base}"
  override="${evidence}/${project}.override.yml"
  printf 'services:\n  %s:\n    entrypoint: ["/bin/sh", "-c", "echo forced migration failure >&2; exit 7"]\n' "${broken}" >"${override}"
  export COMPOSE_FILE="docker-compose.yml:${override}"
  ! compose up -d --build >>"${evidence}/${project}-compose.log" 2>&1 || fail "compose up hid the failing ${broken}"
  [[ "$(state_of "${broken}")" == "exited 7 "* ]] || fail "${broken} should have exited 7: $(state_of "${broken}")"
  assert_never_started "${blocked[@]}"
  pass "${broken} exit 7: compose up failed visibly and ${blocked[*]} never started"

  compose up -d "${sibling_web}" >>"${evidence}/${project}-compose.log" 2>&1 \
    || fail "the ${sibling_web} branch could not start beside the failed ${broken}"
  wait_for "${sibling_api}" healthy
  wait_http "${!sibling_url_var}/" 200
  assert_never_started "${blocked[@]}"
  pass "${sibling_web} branch starts alone and serves while ${broken} stays failed"

  ! compose up -d >>"${evidence}/${project}-compose.log" 2>&1 || fail "repeated up hid the failing ${broken}"
  wait_for "${sibling_api}" healthy
  expect_http "${!sibling_url_var}/" 200
  assert_never_started "${blocked[@]}"
  compose ps -a --format '{{.Service}} {{.State}} {{.ExitCode}}' >"${evidence}/${project}-states.txt"
  pass "a repeated failing up leaves the running ${sibling_api} branch serving"
  discard_project
  unset COMPOSE_FILE
}

case_failure() {
  run_failure licensing-migrate 24000 frontend backend merchant_web licensing-runtime-role licensing-api admin-frontend
  run_failure merchant-migrate 34000 admin-frontend licensing-api admin_web db-runtime-role backend frontend
}

# Playwright is installed in the admin workspace (Bun's isolated linker keeps
# the repository root free of packages), so the suite runs from there.
browser_phase() {
  (
    cd src/admin-frontend
    NODE_PATH="${PWD}/node_modules" P0105_PHASE="$1" P0105_MERCHANT_WEB="${merchant_web}" \
      P0105_ADMIN_WEB="${admin_web}" P0105_MAILPIT="${mailpit_ui}" P0105_ARTIFACTS="${evidence}" \
      bun x playwright test --config ../../tests/licensing/browser/playwright.config.ts
  ) 2>&1 | tee "${evidence}/browser-$1.log" >&2
  return "${PIPESTATUS[0]}"
}

browser_rows() {
  local user_id shop_id
  user_id="$(sed -n 's/.*"userId": *"\([^"]*\)".*/\1/p' "${evidence}/browser-state.json")"
  shop_id="$(sed -n 's/.*"shopId": *"\([^"]*\)".*/\1/p' "${evidence}/browser-state.json")"
  printf 'users=%s shops=%s memberships=%s all_users=%s all_shops=%s' \
    "$(sql intelibill_dev -c "select count(*) from users where id = '${user_id}'")" \
    "$(sql intelibill_dev -c "select count(*) from shops where id = '${shop_id}'")" \
    "$(sql intelibill_dev -c "select count(*) from shop_memberships where user_id = '${user_id}' and shop_id = '${shop_id}'")" \
    "$(sql intelibill_dev -c 'select count(*) from users')" \
    "$(sql intelibill_dev -c 'select count(*) from shops')"
}

# customers is row-level secured by app.active_shop_id: the runtime role sees
# the enrolled shop's customer only under that shop's scope.
check_rls_scope() {
  local shop_id owner unscoped scoped other
  shop_id="$(sed -n 's/.*"shopId": *"\([^"]*\)".*/\1/p' "${evidence}/browser-state.json")"
  owner="$(sql intelibill_dev -c "select count(*) from customers where shop_id = '${shop_id}'")"
  unscoped="$(sql_as intelibill_app intelibill_app_password intelibill_dev 'select count(*) from customers')"
  scoped="$(sql_as intelibill_app intelibill_app_password intelibill_dev \
    "select set_config('app.active_shop_id', '${shop_id}', false) is null; select count(*) from customers" | tail -1)"
  other="$(sql_as intelibill_app intelibill_app_password intelibill_dev \
    "select set_config('app.active_shop_id', '00000000-0000-0000-0000-000000000001', false) is null; select count(*) from customers" | tail -1)"
  record browser-rls.txt "owner=${owner} runtime_unscoped=${unscoped} runtime_own_shop=${scoped} runtime_other_shop=${other}"
  [[ "${owner}" -ge 1 && "${unscoped}" == 0 && "${scoped}" == "${owner}" && "${other}" == 0 ]] \
    || fail "row-level security: $(cat "${evidence}/browser-rls.txt")"
  pass "row-level security holds for the runtime role: $(cat "${evidence}/browser-rls.txt")"
}

case_browser() {
  project="${main_project}"
  guard_project
  assign_ports 14000
  [[ "$(state_of backend)" == "running 0 healthy"* ]] || stack_up
  browser_phase enroll || fail "browser enrollment phase failed"
  local before
  before="$(browser_rows)"
  [[ "${before}" == "users=1 shops=1 memberships=1"* ]] || fail "browser rows missing: ${before}"
  check_rls_scope
  pass "database holds the enrolled user and shop (${before})"
  compose down >>"${evidence}/${project}-compose.log" 2>&1
  compose up -d >>"${evidence}/${project}-compose.log" 2>&1
  wait_stack
  browser_phase restart || fail "browser restart phase failed"
  [[ "$(browser_rows)" == "${before}" ]] || fail "restart changed user/shop rows: $(browser_rows)"
  pass "after down/up the same user signs in and owns the same single shop (${before})"
}

case_cleanup() {
  local suffix
  for suffix in "" -reused -fail-licensing -fail-merchant; do
    project="${main_project}${suffix}"
    discard_project
  done
}
