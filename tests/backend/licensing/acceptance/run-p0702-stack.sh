#!/usr/bin/env bash
set -euo pipefail
umask 077
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$repository_root"
project="${P0105_PROJECT:-intelibill-p01-05-p0702-$(date -u +%Y%m%d%H%M%S)-$$}"
evidence="${P0702_EVIDENCE_DIR:?set P0702_EVIDENCE_DIR to a private evidence directory}"
mkdir -p "$evidence"
evidence="$(cd "$evidence" && pwd)"
fixture_dir="$repository_root/tests/backend/licensing/acceptance/p0702"
source scripts/dev/licensing-stack-lib.sh
validate_project_name
endpoint="${DOCKER_HOST:-$(docker context inspect "$(docker context show)" --format '{{.Endpoints.docker.Host}}')}"
[[ "$endpoint" = unix:///* && -S "${endpoint#unix://}" ]] || fail "A local Docker socket is required"
for kind in container volume network; do
  listing=("$kind" ls -q)
  if [[ "$kind" == container ]]; then listing+=(-a); fi
  names="$(docker "${listing[@]}" --filter "name=^${project}[_-]")"
  labeled="$(docker "${listing[@]}" --filter "label=com.docker.compose.project=$project")"
  [[ -z "$names$labeled" ]] || fail "Choose a fresh Compose project"
done
existing_images="$(docker image ls -q --filter "reference=${project}-*")"
[[ -z "$existing_images" ]] || fail "Choose a project without existing build images"
scratch="$(mktemp -d)"
project_owned=false
cleanup() {
  local status=$?
  trap - EXIT
  if [[ "$project_owned" == true ]]; then
    compose down --volumes --rmi local --remove-orphans >>"$evidence/cleanup.log" 2>&1 || status=1
  fi
  rm -rf "$scratch"
  exit "$status"
}
trap cleanup EXIT
python3 "$fixture_dir/fixture-transport.py" "$scratch"
failure_override="$scratch/override.json"
base="${P0702_PORT_BASE:-$((23000 + RANDOM % 22000))}"
assign_ports "$base"
! lsof -nP -iTCP:"$((base + 432))" -sTCP:LISTEN >/dev/null 2>&1 || fail "PostgreSQL port is in use"
project_owned=true
stack_up
compose ps -a >"$evidence/services.txt"
compose images >"$evidence/images.txt"
sql intelibill_licensing_dev -c 'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id' >"$evidence/migrations.txt"
grep -Fxq 20261128100620_DeliverBillingNoticeEmails "$evidence/migrations.txt"
source "$fixture_dir/demo.sh"
