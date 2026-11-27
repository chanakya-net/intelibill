#!/usr/bin/env bash
# Phase 1 acceptance on the shipping local Compose stack.
set +x
set -euo pipefail
umask 077
export LC_ALL=C
unset DEBUG PWDEBUG NODE_OPTIONS BASH_ENV
unset HTTP_PROXY HTTPS_PROXY ALL_PROXY http_proxy https_proxy all_proxy

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
cd "$repository_root"
stage=preflight
project="${P0115_PROJECT:-}"
destination="${P0115_ARTIFACTS:-}"
scratch=
cleanup() {
  local status=$?
  [[ -z "$scratch" ]] || rm -r -- "$scratch"
  if ((status != 0)); then printf 'P01-15 failed at %s (exit %s)\n' "$stage" "$status" >&2; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
refuse() { printf '%s\n' "$1" >&2; exit 1; }

validate_input() {
  [[ "$project" =~ ^intelibill-p01-05-845-[a-z0-9]+$ ]] || refuse 'Set a unique P0115_PROJECT beginning with intelibill-p01-05-845-.'
  [[ "$destination" = /* && -d "$destination" && -O "$destination" ]] || refuse 'Use an existing, owned absolute P0115_ARTIFACTS directory.'
  destination="$(cd "$destination" && pwd -P)"
  [[ "$destination" != "$repository_root" && "$destination" != "$repository_root/"* ]] || refuse 'Use an evidence directory outside the worktree.'
  local permissions environment
  permissions="$(ls -ld "$destination" | cut -c5-10)"
  [[ "$permissions" == '------' ]] || refuse 'Make P0115_ARTIFACTS private (chmod 700).'
  for environment in "${DOTNET_ENVIRONMENT:-Development}" "${ASPNETCORE_ENVIRONMENT:-Development}"; do
    [[ "$environment" =~ ^(Development|Testing)$ ]] || refuse 'Use Development or Testing.'
  done
  local context endpoint
  context="$(docker context show)"
  endpoint="${DOCKER_HOST:-$(docker context inspect "$context" --format '{{.Endpoints.docker.Host}}')}"
  [[ "$endpoint" = unix:///* && -S "${endpoint#unix://}" ]] || refuse 'Use a local Docker Unix socket.'
}

original_resources() {
  local label='label=com.docker.compose.project=intelibill'
  docker ps -a --filter "$label" --format '{{.ID}} {{.Names}}'
  docker network ls --filter "$label" --format '{{.ID}} {{.Name}}'
  local volumes name
  volumes="$(docker volume ls --filter "$label" --format '{{.Name}}')"
  while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    docker volume inspect "$name" --format '{{.Name}} {{.CreatedAt}}'
  done <<<"$volumes"
}

assert_fresh() {
  local existing
  existing="$(
    docker ps -a --filter "name=^/${project}([_-]|$)" --format '{{.Names}}' &&
    docker network ls --filter "name=^${project}([_-]|$)" --format '{{.Name}}' &&
    docker volume ls --filter "name=^${project}([_-]|$)" --format '{{.Name}}'
  )" || refuse 'Could not verify fresh fixture ownership.'
  [[ -z "$existing" ]] || refuse 'Fixture project already has resources. Choose another name.'
  (set -o noclobber; printf '%s\n' "$project" >"$destination/project")
}

assert_checked_source() {
  local current changes
  current="$(git rev-parse --verify HEAD)" || refuse 'Could not identify the source commit.'
  [[ "$current" == "$source_sha" ]] || refuse 'Source commit changed during acceptance.'
  changes="$(git status --porcelain=v1 --untracked-files=no)" || refuse 'Could not check the tracked source tree.'
  [[ -z "$changes" ]] || refuse 'Commit tracked changes before running acceptance.'
}

run_stack_case() {
  P0105_PROJECT="$project" P0105_ARTIFACTS="$evidence" \
    bash scripts/dev/test-licensing-stack.sh --case "$1" >"$scratch/stack-$1.log" 2>&1
}

prepare_provider() {
  local mfa_root signing_root
  mfa_root="$(openssl rand -base64 32)"
  signing_root="$(openssl rand -base64 32)"
  printf '%s\n' "$mfa_root" "$signing_root" >"$scratch/secrets"
  failure_override="$scratch/provider.yml"
  cat >"$failure_override" <<YAML
services:
  licensing-api:
    environment:
      AdminMfa__ActiveKeyId: local
      AdminMfa__Keys__local: "$mfa_root"
      AdminSessions__SigningKey: "$signing_root"
      AdminFixtures__SessionDigest: ""
      AdminFixtures__AdminId: ""
      AdminFixtures__Capabilities: ""
YAML
  compose up -d >"$scratch/provider-up.log" 2>&1
  wait_stack
  prepare_proof >"$scratch/proof" 2>"$scratch/bootstrap.log"
  [[ -s "$scratch/proof" ]]
  cat "$scratch/proof" >>"$scratch/secrets"
}

prepare_proof() {
  compose run -T --rm --no-deps \
    -e DOTNET_ENVIRONMENT=Development -e LicensingMigrationDatabase__Host=postgres \
    -e LicensingMigrationDatabase__Database=intelibill_licensing_dev \
    -e LicensingMigrationDatabase__Username=intelibill_licensing_migrator \
    -e LicensingMigrationDatabase__Password=intelibill_licensing_migrator_password \
    licensing-api bootstrap-admin prepare --verified-email p0115-owner@example.test \
    --verification-reference p0115-local-fixture --admin-origin "$admin_web"
}

assert_bootstrap_closed() {
  local status=0
  prepare_proof >"$scratch/bootstrap-retry.stdout" 2>"$scratch/bootstrap-retry.log" || status=$?
  [[ "$status" == 2 && ! -s "$scratch/bootstrap-retry.stdout" ]] &&
    grep -Fxq 'Initial setup is closed.' "$scratch/bootstrap-retry.log"
}

browser_phase() (
  cd src/frontend/licensing
  export NODE_PATH="$PWD/node_modules"
  export P0115_ADMIN_WEB="$admin_web" P0115_MERCHANT_WEB="$merchant_web" P0115_MAILPIT="$mailpit_ui"
  export P0115_PROOF_FILE="$scratch/proof" P0115_SCRATCH="$scratch" P0115_PHASE="$1"
  export P0115_MERCHANT_CREDENTIALS="$evidence/.fixture-credentials.json"
  export P0115_MERCHANT_STATE="$evidence/browser-state.json"
  bun x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0115.config.ts
)

run_browser() {
  local phase="$1" log="$scratch/browser-$1.log"
  if browser_phase "$phase" >"$log" 2>&1; then
    require_pass_count "$log" 1 || refuse 'Chromium did not report one passing journey.'
    [[ -s "$scratch/observed-$phase" ]] || refuse 'Chromium did not write its observations.'
  else
    local status=$?
    grep -E '^[[:space:]]+Error: Provider journey failed at [a-zA-Z /-]+$' "$log" >&2 || true
    return "$status"
  fi
}

assert_audit_role() {
  local statement
  for statement in 'UPDATE provider_audit_events SET action=action' 'DELETE FROM provider_audit_events' 'TRUNCATE provider_audit_events'; do
    expect_denied 'licensing audit mutation' 'permission denied|immutable|append.only' \
      intelibill_licensing_app intelibill_licensing_app_password intelibill_licensing_dev \
      "BEGIN; $statement; ROLLBACK;" >>"$scratch/audit-role.log" 2>&1
  done
}

database_facts() {
  sql intelibill_licensing_dev -c "
    SELECT
      (SELECT count(*) FROM initial_admin_bootstrap WHERE owner_id IS NOT NULL AND consumed_at IS NOT NULL),
      (SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NOT NULL),
      (SELECT count(*) FROM admin_invitations WHERE accepted_at IS NOT NULL),
      (SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL),
      (SELECT count(*) FROM provider_audit_events);
    SELECT id, capabilities FROM admin_identities ORDER BY id;" || return
  local database
  for database in intelibill_dev intelibill_licensing_dev; do
    sql "$database" -c 'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id' || return
  done
}

restart_provider() {
  local facts
  facts="$(database_facts)"
  [[ "$facts" =~ ^1\|2\|1\|[1-9][0-9]*\|[1-9][0-9]* ]] || refuse 'Expected one bootstrap, two enrolled identities, one accepted invitation and revoked sessions.'
  compose restart backend licensing-api >"$scratch/restart.log" 2>&1
  wait_stack
  [[ "$(database_facts)" = "$facts" ]] || refuse 'API restart changed persisted facts.'
  compose down >"$scratch/down.log" 2>&1
  compose up -d >"$scratch/up.log" 2>&1
  wait_stack
  [[ "$(database_facts)" = "$facts" ]] || refuse 'Compose recreation changed persisted facts.'
  printf '%s\n' "${facts%%$'\n'*}" >"$scratch/database-counts"
}

write_summary() {
  local output="$scratch/summary"
  {
    printf 'source_base=%s\nrun_utc=%s\nproject=%s\n' "$source_sha" "$run_utc" "$project"
    printf 'Primary Compose build: docker compose --env-file /dev/null -f "%s/docker-compose.yml" -p %s --profile full up -d --build\n' "$repository_root" "$project"
    printf '%s\n' \
      'PASS existing P01-05 all cases (fresh, reused, migration failures, browser)' \
      'PASS bootstrap CLI refused another proof after consumption (exit 2)' \
      'PASS all five jobs, restricted runtime logins, RLS, retained volumes and independent branches' \
      'PASS Chromium merchant Mailpit enrollment and sign-in after restart (2 + 2 tests)' \
      'PASS provider audit runtime denied UPDATE, DELETE and TRUNCATE' \
      'PASS provider facts and migration history survived API restart and Compose down/up' \
      'PASS original developer Compose resource identities unchanged' \
      'LIVE UNVERIFIED'
    printf 'Chromium command (admin workspace, private fixture variables): %s\n' \
      'NODE_PATH="$PWD/node_modules" bun x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0115.config.ts'
    printf 'PostgreSQL counts (consumed bootstrap|enrolled identities|accepted invitations|revoked sessions|audit events): %s\n' "$(cat "$scratch/database-counts")"
    printf '%s\n' 'PASS provider Chromium enroll and restart (1 + 1 tests)'
    cat "$scratch/observed-enroll" "$scratch/observed-restart"
  } >"$output"
  publish_checked "$output" "$scratch/secrets" "$destination/summary.txt" ||
    refuse 'Shareable evidence is missing or contains a fixture credential.'
  cat "$destination/summary.txt"
}

run_all() {
  source_sha="$(git rev-parse --verify HEAD)" || refuse 'Could not identify the source commit.'
  assert_checked_source
  assert_fresh
  run_utc="$(date -u +%FT%TZ)"
  original_resources | sort >"$scratch/original"
  stage=combined-stack
  run_stack_case all
  assert_checked_source
  grep -q 'case all complete' "$evidence/run.log" || refuse 'Missing P01-05 completion evidence.'
  require_pass_count "$evidence/browser-enroll.log" 2 &&
    require_pass_count "$evidence/browser-restart.log" 2 || refuse 'Missing merchant Chromium pass counts.'
  stage=provider-inputs
  assign_ports 14000
  prepare_provider
  stage=provider-enrollment
  run_browser enroll
  stage=database-and-restart
  assert_bootstrap_closed || refuse 'The bootstrap CLI did not confirm that initial setup is closed.'
  assert_audit_role
  restart_provider
  stage=provider-after-restart
  run_browser restart
  stage=evidence
  original_resources | sort >"$scratch/after"
  cmp -s "$scratch/original" "$scratch/after" || refuse 'Original developer resource identities changed.'
  assert_checked_source
  write_summary
}

[[ "$#" = 2 && "$1" = --case && "$2" =~ ^(all|cleanup)$ ]] || refuse 'Usage: test-p01-foundation.sh --case all|cleanup'
validate_input
scratch="$(mktemp -d "$destination/.p0115.XXXXXX")"
evidence="$scratch/p0105"
mkdir "$evidence"
source scripts/dev/licensing-stack-lib.sh
source scripts/licensing/p01-foundation-evidence.sh
for variable in ${!INTELIBILL_@}; do unset "$variable"; done
case "$2" in
  all) run_all ;;
  cleanup)
    stage=fixture-cleanup
    [[ -f "$destination/project" && "$(cat "$destination/project")" == "$project" ]] || refuse 'No fixture ownership record for this project in the evidence directory.'
    run_stack_case cleanup
    printf '%s\n' 'PASS label-checked fixture cleanup'
    ;;
esac
