# Phase 10 handoff — environment infrastructure

**Updated 2026-07-27 on branch `infra-setup`.** Phase 10 was applied and
independently reviewed at implementation head `e8c11915`; the documentation
commit that records this evidence necessarily follows that head. Everything
below was verified against the live subscription, not recalled from the
runbook. Where this document and
[infrastructure-implementation-guide.md](infrastructure-implementation-guide.md)
disagree, this document is newer.

Read first: [infrastructure-decisions.md](infrastructure-decisions.md) §7, §8, §18, §19, §20, §21 — those are the constraints you cannot design around without reopening a decision.

---

## 1. Where the work stands

| Phase | State |
|---|---|
| 1–4 bootstrap, state, OIDC identities, GitHub environments | done |
| 5 shared infrastructure (PostgreSQL, no DNS) | done |
| 6 DNS delegation | **⏸ deferred to ~2026-08-25.** Everything runs on `*.azurecontainerapps.io` |
| 7 database bootstrap (principals, grants, isolation) | done and verified |
| 8 application production contract | done **except 8.4 telemetry instruments** |
| 9 Key Vault | vaults and signing keys applied; integration secrets outstanding |
| **10 environment infrastructure** | **done, applied, and independently verified 2026-07-27** |
| 11 pipelines | not started |
| 12 domains | ⏸ blocked by phase 6 |
| 13–14 keep-warm, verification | not started |

---

## 2. What exists in Azure right now

Subscription `cef6a1af-9d98-437f-b99c-ad6d24e5631c`, tenant `e5208e76-dd12-47f0-9541-c9b45afaffe6`, one resource group **`intelibill-shared`**, region **`centralindia`**.

> `southindia` and `eastus` are both offer-restricted for this subscription for PostgreSQL. Check with the subscription-scoped capabilities API, not `list-skus`, which reads a catalog rather than your entitlement.

```
intelibill-pg-01      PostgreSQL 17 Flexible, Standard_B1ms, 32 GB, public access,
                      14-day backups, zone 1
                      databases: intelibill_dev, intelibill_prod
intelibill-dev-kv     RBAC, soft delete 7 days, purge protection OFF
intelibill-prod-kv    RBAC, soft delete 90 days, purge protection ON (irreversible)
                      both hold key `jwt-signing` (RSA 2048, sign/verify, rotation policy)
intelibilltfstate01   OpenTofu state, containers tfstate-{shared,dev,prod}
intelibill-logs       Log Analytics, PerGB2018, 30-day retention, 0.1 GB/day cap
intelibill-dev-env    Container Apps environment (Consumption)
  intelibill-dev-api  internal ingress; generated internal FQDN
  intelibill-dev-web  external ingress; generated public FQDN
  intelibill-dev-migrate
                      manual migration job; never executed
intelibill-prod-env   Container Apps environment (Consumption)
  intelibill-prod-api internal ingress; generated internal FQDN
  intelibill-prod-web external ingress; generated public FQDN
  intelibill-prod-migrate
                      manual migration job; never executed
```

### Workload identities (attached without replacement)

| Environment | Role | Name | Client ID | Principal ID |
|---|---|---|---|---|
| dev | app | `id-app-dev` | `7c33ca76-6977-4e45-9c42-fda8cd5b2aab` | `639aa307-3394-4da0-a5bf-bcecf7a36632` |
| dev | migrator | `id-migrator-dev` | `51dece82-a93c-466b-8a16-6eaca361db28` | `d4463264-a136-467a-af6d-e174d99dab26` |
| prod | app | `id-app-prod` | `a5f8f605-3069-4a73-afdd-8972eb847602` | `122b2c9e-7ec7-4b84-9d80-5267092eb0a7` |
| prod | migrator | `id-migrator-prod` | `b5c2de5b-0852-44fc-8a36-4a7404c479bd` | `051aad50-f111-4c7d-8ba6-75630cba1b64` |

Read them from `tofu -chdir=.tofu/envs/<env> output -json identities` rather than copying — but **do not recreate them**. The PostgreSQL principals from Phase 7 are named after these identities and registered by object ID; a replacement identity gets a new object ID and every grant silently stops matching.

### GitHub-facing identities (bootstrap layer)

`plan`, `infra_apply`, `deploy_dev`, `deploy_prod`. Environments `dev`, `prod`, `shared` exist, each carrying `AZURE_CLIENT_ID_INFRA`, `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`; `dev` and `prod` also carry `AZURE_CLIENT_ID_DEPLOY`.

### Database grants already in place (Phase 7)

Per environment: `CONNECT` for its own app and migrator only, `REVOKE CONNECT/TEMPORARY … FROM PUBLIC`, schema `USAGE` for the app, `CREATE` for the migrator, and `ALTER DEFAULT PRIVILEGES` so the app gets DML on whatever the migrator creates later. The cross-environment `CONNECT` matrix returns `t,t,f,f` — **those two false results are the entire dev/prod data boundary**, since both databases live on one server.

---

## 3. Phase 10 completion evidence

### Live workload behavior

| Environment | API | Web | Migration job |
|---|---|---|---|
| dev | `intelibill-dev-api`; internal; `intelibill-dev-api.internal.jollypond-9e71a2fb.centralindia.azurecontainerapps.io` | `intelibill-dev-web`; external; `intelibill-dev-web.jollypond-9e71a2fb.centralindia.azurecontainerapps.io` | `intelibill-dev-migrate`; manual; migrator principal `d4463264-a136-467a-af6d-e174d99dab26`; 0 executions |
| prod | `intelibill-prod-api`; internal; `intelibill-prod-api.internal.politebush-ac4f5ec3.centralindia.azurecontainerapps.io` | `intelibill-prod-web`; external; `intelibill-prod-web.politebush-ac4f5ec3.centralindia.azurecontainerapps.io` | `intelibill-prod-migrate`; manual; migrator principal `051aad50-f111-4c7d-8ba6-75630cba1b64`; 0 executions |

API and web resources are configured for minimum 0 and maximum 1 replica.
Azure's live representation omits `minReplicas` when it is the zero default;
all four live resources explicitly reported `maxReplicas = 1`. The API remains
internal and the web app remains the only public browser origin.

Both environments still use the immutable bootstrap image. Phase 11 must
replace the migration-job image before starting it, wait for a successful
execution, and only then deploy the real API and web images. Phase 10 did not
start either job.

### Public-network exception and current derived address snapshot

The public PostgreSQL exception remains intentionally narrow. Live verification
found 362 managed firewall rules: 181 named `container-apps-dev-*` and 181
named `container-apps-prod-*`. Every rule has identical start and end IPv4
addresses; there are zero broad, ranged, operator, or otherwise unmanaged
rules. In particular, the Azure-services `0.0.0.0` exception is absent.

The following snapshot is **derived and non-canonical**. Azure may change
Container Apps outbound addresses; OpenTofu state and this document are not a
source of truth for future reachability.

| Environment | Derived unique advertised addresses | SHA-256 of sorted addresses, one address plus newline per record |
|---|---:|---|
| dev | 181 | `25cdaa25bcb51b120eca6563def49c030d3181dda4f437ca3e9df1f98a856648` |
| prod | 181 | `c400ecdfeaf34ce0959a1cdb194f577af37969594b64dff614727ae7b8cd0395` |

Retrieve and fingerprint the live API, web, and job union:

```bash
for environment_name in dev prod; do
  api_addresses="$(
    az containerapp show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-api" \
      --query properties.outboundIpAddresses -o json
  )"
  web_addresses="$(
    az containerapp show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-web" \
      --query properties.outboundIpAddresses -o json
  )"
  job_addresses="$(
    az containerapp job show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-migrate" \
      --query properties.outboundIpAddresses -o json
  )"
  sorted_addresses="$(
    jq -n \
      --argjson api "${api_addresses}" \
      --argjson web "${web_addresses}" \
      --argjson job "${job_addresses}" \
      '[$api[], $web[], $job[]] | unique | sort'
  )"
  printf '%s count=%s sha256=%s\n' \
    "${environment_name}" \
    "$(jq 'length' <<<"${sorted_addresses}")" \
    "$(jq -r '.[]' <<<"${sorted_addresses}" | shasum -a 256 | awk '{print $1}')"
done

.tofu/scripts/check-container-app-egress.sh
```

The final checker output was:

```text
Egress allowlist verified: 362 expected address(es), 362 managed rule(s).
```

For any future address change, use this exact retained-address two-apply
procedure. The examples use `dev`; set `environment_name=prod` for production.
Never refresh both environment states when only one environment changed.

1. **Capture the old set from both persistent sources before any refresh.**
   The affected environment's persisted output and its exact
   `container-apps-<env>-*` PostgreSQL rules must be nonempty, canonical, and
   equal. This works both before a planned operation and after an unannounced
   provider rotation because neither source has been refreshed yet. Never
   substitute the snapshot printed in this document.

   ```bash
   set -euo pipefail
   environment_name=dev
   [[ "${environment_name}" == dev || "${environment_name}" == prod ]]
   transition_dir="$(mktemp -d)"

   canonical_address_array='
     def canonical_ipv4:
       type == "string" and
       test("^(0|[1-9][0-9]{0,2})(\\.(0|[1-9][0-9]{0,2})){3}$") and
       (split(".") | all(.[]; tonumber >= 0 and tonumber <= 255)) and
       . != "0.0.0.0" and . != "255.255.255.255";
     type == "array" and length > 0 and all(.[]; canonical_ipv4)
   '

   discover_live_addresses() {
     local selected_environment="$1"
     local api_addresses web_addresses job_addresses
     api_addresses="$(
       az containerapp show --resource-group intelibill-shared \
         --name "intelibill-${selected_environment}-api" \
         --query properties.outboundIpAddresses -o json
     )"
     web_addresses="$(
       az containerapp show --resource-group intelibill-shared \
         --name "intelibill-${selected_environment}-web" \
         --query properties.outboundIpAddresses -o json
     )"
     job_addresses="$(
       az containerapp job show --resource-group intelibill-shared \
         --name "intelibill-${selected_environment}-migrate" \
         --query properties.outboundIpAddresses -o json
     )"
     jq -nS \
       --argjson api "${api_addresses}" \
       --argjson web "${web_addresses}" \
       --argjson job "${job_addresses}" \
       '[$api[], $web[], $job[]] | unique | sort'
   }

   tofu -chdir=".tofu/envs/${environment_name}" \
     output -json container_apps |
     jq -S '.outbound_ip_addresses | unique | sort' \
       >"${transition_dir}/old-state.json"

   firewall_prefix="container-apps-${environment_name}-"
   az postgres flexible-server firewall-rule list \
     --resource-group intelibill-shared \
     --server-name intelibill-pg-01 -o json |
     jq -eS --arg prefix "${firewall_prefix}" '
       def canonical_ipv4:
         type == "string" and
         test("^(0|[1-9][0-9]{0,2})(\\.(0|[1-9][0-9]{0,2})){3}$") and
         (split(".") | all(.[]; tonumber >= 0 and tonumber <= 255)) and
         . != "0.0.0.0" and . != "255.255.255.255";
       [.[] | select(.name | startswith($prefix))] as $rules |
       if
         ($rules | length) == 0 or
         any(
           $rules[];
           (.startIpAddress | canonical_ipv4 | not) or
           (.endIpAddress | canonical_ipv4 | not) or
           .startIpAddress != .endIpAddress or
           .name != ($prefix + (.startIpAddress | gsub("\\."; "-")))
         )
       then error("affected-environment firewall rules are invalid")
       else [$rules[].startIpAddress] | unique | sort
       end
     ' >"${transition_dir}/old-firewall.json"

   jq -e "${canonical_address_array}" \
     "${transition_dir}/old-state.json" >/dev/null
   jq -e "${canonical_address_array}" \
     "${transition_dir}/old-firewall.json" >/dev/null
   jq -e -s '.[0] == .[1]' \
     "${transition_dir}/old-state.json" \
     "${transition_dir}/old-firewall.json" >/dev/null
   jq -S '.' "${transition_dir}/old-state.json" \
     >"${transition_dir}/old.json"

   discover_live_addresses "${environment_name}" \
     >"${transition_dir}/current-live.json"
   jq -e "${canonical_address_array}" \
     "${transition_dir}/current-live.json" >/dev/null
   ```

2. **Choose one executable entry branch and converge on `old.json` plus
   `new.json`.**

   - For a **planned** transition, current live must still equal the validated
     old set. Supply the absolute path of the already reviewed affected-
     environment saved plan; this runbook does not authorize its contents.
     Apply it directly, then discover the distinct new live set.
   - For an **unannounced** provider rotation, current live must already differ
     from old. Treat that one captured current set as new; do not overwrite old
     with it and do not rediscover it into a second file.

   ```bash
   transition_mode="${TRANSITION_MODE:?set planned or unannounced}"
   case "${transition_mode}" in
     planned)
       jq -e -s '.[0] == .[1]' \
         "${transition_dir}/old.json" \
         "${transition_dir}/current-live.json" >/dev/null
       planned_environment_plan="${PLANNED_ENVIRONMENT_PLAN:?set PLANNED_ENVIRONMENT_PLAN to the reviewed saved plan path}"
       [[ "${planned_environment_plan}" == /* ]]
       tofu -chdir=".tofu/envs/${environment_name}" \
         show "${planned_environment_plan}"
       tofu -chdir=".tofu/envs/${environment_name}" \
         apply "${planned_environment_plan}"
       discovery_deadline=$((SECONDS + 180))
       while ((SECONDS < discovery_deadline)); do
         discover_live_addresses "${environment_name}" \
           >"${transition_dir}/new.json"
         if jq -e -s '.[0] != .[1]' \
           "${transition_dir}/old.json" \
           "${transition_dir}/new.json" >/dev/null
         then
           break
         fi
         sleep 5
       done
       ;;
     unannounced)
       jq -e -s '.[0] != .[1]' \
         "${transition_dir}/old.json" \
         "${transition_dir}/current-live.json" >/dev/null
       jq -S '.' "${transition_dir}/current-live.json" \
         >"${transition_dir}/new.json"
       ;;
     *)
       printf 'TRANSITION_MODE must be planned or unannounced\n' >&2
       exit 1
       ;;
   esac

   jq -e "${canonical_address_array}" \
     "${transition_dir}/old.json" >/dev/null
   jq -e "${canonical_address_array}" \
     "${transition_dir}/new.json" >/dev/null
   jq -e -s '.[0] != .[1]' \
     "${transition_dir}/old.json" \
     "${transition_dir}/new.json" >/dev/null
   ```

3. **Persist only the affected environment's computed output.** A normal
   shared plan sees the old remote-state output until the affected environment
   state contains new. A planned OpenTofu apply may already have persisted it;
   an unannounced rotation has not. In either case, create, review, and apply a
   saved `-refresh-only` plan. Require exit 0 when state already equals new or
   exit 2 when workload drift must be persisted. The plan must contain no Azure
   mutation, and any drift must be limited to the affected API/web/job.

   ```bash
   tofu -chdir=".tofu/envs/${environment_name}" \
     output -json container_apps |
     jq -S '.outbound_ip_addresses | unique | sort' \
       >"${transition_dir}/pre-refresh-state.json"
   if jq -e -s '.[0] == .[1]' \
     "${transition_dir}/pre-refresh-state.json" \
     "${transition_dir}/new.json" >/dev/null
   then
     expected_refresh_exit=0
     expect_resource_drift=false
   else
     expected_refresh_exit=2
     expect_resource_drift=true
   fi

   refresh_plan="${transition_dir}/${environment_name}-refresh.tfplan"
   set +e
   tofu -chdir=".tofu/envs/${environment_name}" plan \
     -refresh-only -detailed-exitcode -out="${refresh_plan}"
   refresh_exit=$?
   set -e
   [[ "${refresh_exit}" -eq "${expected_refresh_exit}" ]]

   tofu -chdir=".tofu/envs/${environment_name}" \
     show -json "${refresh_plan}" >"${transition_dir}/refresh-plan.json"
   jq -e \
     --argjson expect_resource_drift "${expect_resource_drift}" \
     --slurpfile new "${transition_dir}/new.json" '
     .errored == false and
     (
       [.resource_changes[]? |
         select(.change.actions != ["no-op"])] |
       length == 0
     ) and
     (
       [.resource_drift[]? |
         select(.change.actions != ["no-op"]) |
         .address] as $drift |
       (
         if $expect_resource_drift
         then ($drift | length > 0)
         else ($drift | length == 0)
         end
       ) and
       all(
         $drift[];
         . == "module.environment_infrastructure.azurerm_container_app.api" or
         . == "module.environment_infrastructure.azurerm_container_app.web" or
         . == "module.environment_infrastructure.azurerm_container_app_job.migrate"
       )
     ) and
     (
       .planned_values.outputs.container_apps.value.outbound_ip_addresses |
       unique | sort
     ) == $new[0]
   ' "${transition_dir}/refresh-plan.json" >/dev/null

   tofu -chdir=".tofu/envs/${environment_name}" apply "${refresh_plan}"
   tofu -chdir=".tofu/envs/${environment_name}" \
     output -json container_apps |
     jq -S '.outbound_ip_addresses | unique | sort' \
       >"${transition_dir}/persisted-new.json"
   jq -e -s '.[0] == .[1]' \
     "${transition_dir}/new.json" \
     "${transition_dir}/persisted-new.json" >/dev/null
   ```

4. **Create one retained source for both shared state and the checker.** The
   old affected-environment set must appear identically in both files.

   ```bash
   jq -nS \
     --arg environment "${environment_name}" \
     --slurpfile old "${transition_dir}/old.json" \
     '{dev: [], prod: []} | .[$environment] = $old[0]' \
     >"${transition_dir}/retained.json"
   jq -nS \
     --slurpfile retained "${transition_dir}/retained.json" \
     '{retained_container_apps_outbound_ips: $retained[0]}' \
     >"${transition_dir}/shared-retained.tfvars.json"
   jq -e \
     --slurpfile retained "${transition_dir}/retained.json" \
     '.retained_container_apps_outbound_ips == $retained[0]' \
     "${transition_dir}/shared-retained.tfvars.json" >/dev/null
   ```

5. **First shared apply: add every new exact rule and delete nothing.** The
   machine guard below requires the complete active plan to equal
   `new - old`, create-only, with identical start/end addresses.

   ```bash
   first_plan="${transition_dir}/shared-add-new.tfplan"
   set +e
   tofu -chdir=.tofu/envs/shared plan \
     -detailed-exitcode \
     -var-file="${transition_dir}/shared-retained.tfvars.json" \
     -out="${first_plan}"
   first_exit=$?
   set -e
   [[ "${first_exit}" -eq 0 || "${first_exit}" -eq 2 ]]
   tofu -chdir=.tofu/envs/shared show -json "${first_plan}" \
     >"${transition_dir}/shared-add-new.json"

   jq -e \
     --arg environment "${environment_name}" \
     --slurpfile old "${transition_dir}/old.json" \
     --slurpfile new "${transition_dir}/new.json" '
     def rule_name($ip):
       "container-apps-" + $environment + "-" + ($ip | gsub("\\."; "-"));
     (($new[0] - $old[0]) |
       map({
         actions: ["create"],
         name: rule_name(.),
         start: .,
         end: .
       }) |
       sort_by(.name)) as $expected |
     ([.resource_changes[]? |
       select(.change.actions != ["no-op"]) |
       {
         address,
         actions: .change.actions,
         name: .change.after.name,
         start: .change.after.start_ip_address,
         end: .change.after.end_ip_address
       }] |
       sort_by(.name)) as $active |
     ($active | map(del(.address))) == $expected and
     all(
       $active[];
       .address |
       startswith(
         "module.database." +
         "azurerm_postgresql_flexible_server_firewall_rule.allowed["
       )
     )
   ' "${transition_dir}/shared-add-new.json" >/dev/null

   tofu -chdir=.tofu/envs/shared apply "${first_plan}"
   .tofu/scripts/check-container-app-egress.sh \
     --environment "${environment_name}" \
     --retained-file "${transition_dir}/retained.json"
   ```

   Wait for PostgreSQL firewall propagation. The readiness gate below is only
   valid after the real Phase 11+ Node web and API images are deployed; do not
   run it against the bootstrap image. It first resolves the public web FQDN
   and single active revision, uses bounded harmless GETs to wake a
   scale-to-zero web app, and polls Azure for a ready/running replica with a
   hard three-minute deadline.

   ```bash
   web_name="intelibill-${environment_name}-web"
   public_web_fqdn="$(
     az containerapp show --resource-group intelibill-shared \
       --name "${web_name}" \
       --query properties.configuration.ingress.fqdn -o tsv
   )"
   active_revisions="$(
     az containerapp revision list --resource-group intelibill-shared \
       --name "${web_name}" --query '[?properties.active]' -o json
   )"
   [[ -n "${public_web_fqdn}" ]]
   [[ "$(jq 'length' <<<"${active_revisions}")" -eq 1 ]]
   active_revision="$(jq -r '.[0].name' <<<"${active_revisions}")"

   wake_deadline=$((SECONDS + 180))
   wake_status=""
   replica_name=""
   while ((SECONDS < wake_deadline)); do
     wake_status="$(
       curl --silent --show-error --max-time 10 \
         --output /dev/null --write-out '%{http_code}' \
         "https://${public_web_fqdn}/phase-10-readiness-wake" || true
     )"
     replica_json="$(
       az containerapp replica list \
         --resource-group intelibill-shared \
         --name "${web_name}" \
         --revision "${active_revision}" -o json
     )"
     replica_name="$(
       jq -r '
         [
           .[] |
           select(.properties.runningState == "Running") |
           select(
             any(
               .properties.containers[];
               .name == "web" and
               .ready == true and
               .runningState == "Running"
             )
           )
         ][0].name // empty
       ' <<<"${replica_json}"
     )"
     if [[ "${wake_status}" == 200 && -n "${replica_name}" ]]; then
       break
     fi
     sleep 5
   done
   [[ "${wake_status}" == 200 && -n "${replica_name}" ]]
   ```

   Then target that explicit revision and replica. The Node probe has its own
   two-minute deadline, retries the internal API readiness URL, prints a stable
   success marker only for HTTP 200, and exits nonzero otherwise. Require both
   the CLI exit and marker before removing stale rules:

   ```bash
   readiness_output="$(
     az containerapp exec \
       --resource-group intelibill-shared \
       --name "${web_name}" \
       --revision "${active_revision}" \
       --replica "${replica_name}" \
       --container web \
       --command "node -e \"const end=Date.now()+120000; (async()=>{ while(Date.now()<end){ try { const r=await fetch(process.env.API_ORIGIN + '/health/ready',{signal:AbortSignal.timeout(5000)}); if(r.status===200){ console.log('PHASE10_READINESS_HTTP_200'); return; } } catch {} await new Promise(resolve=>setTimeout(resolve,5000)); } process.exit(1); })();\""
   )"
   printf '%s\n' "${readiness_output}"
   grep -Fq 'PHASE10_READINESS_HTTP_200' <<<"${readiness_output}"
   ```

   Do not retire old rules based only on bootstrap-image health or a resource
   provisioning status.

6. **Second shared apply: clear retention and delete only stale exact rules.**
   Pass an explicit empty map, rather than relying on an operator's local
   tfvars. The guard requires the complete active plan to equal
   `old - new`, delete-only.

   ```bash
   jq -nS \
     '{retained_container_apps_outbound_ips: {dev: [], prod: []}}' \
     >"${transition_dir}/shared-clear-retained.tfvars.json"
   second_plan="${transition_dir}/shared-remove-stale.tfplan"
   set +e
   tofu -chdir=.tofu/envs/shared plan \
     -detailed-exitcode \
     -var-file="${transition_dir}/shared-clear-retained.tfvars.json" \
     -out="${second_plan}"
   second_exit=$?
   set -e
   [[ "${second_exit}" -eq 0 || "${second_exit}" -eq 2 ]]
   tofu -chdir=.tofu/envs/shared show -json "${second_plan}" \
     >"${transition_dir}/shared-remove-stale.json"

   jq -e \
     --arg environment "${environment_name}" \
     --slurpfile old "${transition_dir}/old.json" \
     --slurpfile new "${transition_dir}/new.json" '
     def rule_name($ip):
       "container-apps-" + $environment + "-" + ($ip | gsub("\\."; "-"));
     (($old[0] - $new[0]) |
       map({
         actions: ["delete"],
         name: rule_name(.),
         start: .,
         end: .
       }) |
       sort_by(.name)) as $expected |
     ([.resource_changes[]? |
       select(.change.actions != ["no-op"]) |
       {
         address,
         actions: .change.actions,
         name: .change.before.name,
         start: .change.before.start_ip_address,
         end: .change.before.end_ip_address
       }] |
       sort_by(.name)) as $active |
     ($active | map(del(.address))) == $expected and
     all(
       $active[];
       .address |
       startswith(
         "module.database." +
         "azurerm_postgresql_flexible_server_firewall_rule.allowed["
       )
     )
   ' "${transition_dir}/shared-remove-stale.json" >/dev/null

   tofu -chdir=.tofu/envs/shared apply "${second_plan}"
   .tofu/scripts/check-container-app-egress.sh
   ```

Never swap old and new rules in one propagation window, summarize addresses
into a range, or fall back to the broad Azure-services exception.

### Deploy scopes and identity preservation

The custom `Intelibill Container App Deployer` role has exactly these six
routine deployment assignments:

| Deploy principal | Exact resource scopes |
|---|---|
| dev `4475d63e-2970-455f-935d-f4de25a0d7d4` | `.../Microsoft.App/containerApps/intelibill-dev-api`; `.../Microsoft.App/containerApps/intelibill-dev-web`; `.../Microsoft.App/jobs/intelibill-dev-migrate` |
| prod `be616680-7dd9-450a-85e7-cf52f28e05a4` | `.../Microsoft.App/containerApps/intelibill-prod-api`; `.../Microsoft.App/containerApps/intelibill-prod-web`; `.../Microsoft.App/jobs/intelibill-prod-migrate` |

There is no deploy assignment whose scope equals resource group
`intelibill-shared`. The four app/migrator principal IDs in the identity table
above match the pre-Phase-10 baseline exactly.

The optional `new-relic-api-key` reference remains disabled in both
environments (`observability_secret_configured = false`). No integration
secret value was read or persisted.

### Idempotence and monitoring

Fresh `tofu plan -detailed-exitcode` runs for shared, dev, prod, and bootstrap
all exited 0 with `No changes`. Diagnostic settings route exactly:

- Container Apps: `ContainerAppConsoleLogs`, `ContainerAppSystemLogs`,
  `ContainerAppHTTPLogs`, and `AllMetrics`;
- PostgreSQL: `PostgreSQLLogs` and `AllMetrics`;
- each Key Vault: `AuditEvent` and `AllMetrics`.

After read-only public web and Key Vault metadata probes, the shared workspace
contained recent console, system, and HTTP records for both Container Apps
environments; PostgreSQL log and metric records; and audit and metric records
for both Key Vaults. HTTP records arrived after about ten minutes of ingestion
lag. Both migration-job execution counts were queried again afterward and
remained zero.

### Separate repository-tooling limitation

Graphify AST extraction succeeds, but HTML visualization generation exceeds
the size limit for the 14,305-node graph. This is a repository visualization
limitation, not an Azure, OpenTofu, drift, idempotence, or logging failure.

---

## 4. The application contract Phase 10 satisfies

This is the part the guide's Phase 10 examples predate. All of it is verified working locally.

### API container

| Setting | Value | Why |
|---|---|---|
| Port | `8080` | `ASPNETCORE_HTTP_PORTS=8080` is baked into the image; `EXPOSE 8080` |
| User | non-root `app` (uid 1654) | shipped by the .NET base image |
| Liveness probe | `GET /health/live` | must **not** include the database — a DB outage failing liveness restarts every replica and turns an outage into a crash loop |
| Readiness probe | `GET /health/ready` | runs `SELECT 1` through the app's own `NpgsqlDataSource`, so under Entra it also proves the identity can still get a token |
| Startup probe | `/health/live` | same endpoint, longer failure budget |

Both health paths are excluded from HTTPS redirection in the app, so plain-HTTP probes answer 200 rather than 307.

Environment variables the API needs:

```
ASPNETCORE_ENVIRONMENT      Production
ASPNETCORE_HTTP_PORTS       8080
AZURE_CLIENT_ID             <app identity client id>   # DefaultAzureCredential
Database__Host              intelibill-pg-01.postgres.database.azure.com
Database__Port              5432
Database__Database          intelibill_dev | intelibill_prod
Database__Username          id-app-dev | id-app-prod    # MUST equal the identity name
Database__UseEntraAuth      true
Database__MaxPoolSize       12                          # per replica; B1ms is small
# Database__Password        MUST BE ABSENT — startup fails if set alongside UseEntraAuth
Jwt__SigningMode            KeyVault
Jwt__KeyVaultKeyId          https://intelibill-<env>-kv.vault.azure.net/keys/jwt-signing
# Jwt__Secret               MUST BE ABSENT — same validator, same reason
Jwt__Issuer / Jwt__Audience <per environment>
App__BaseUrl                <web app origin>
Cors__AllowedOrigins__0     (usually none — the web app proxies same-origin)
Proxy__Enabled              true
Proxy__ForwardLimit         2                           # ingress + web proxy, not 1
Proxy__TrustAnyProxy        true                        # ingress IPs are neither stable nor published
Observability__*            endpoint, service name, environment, OTLP key
```

`Database__Username` must be exactly the managed identity's name — that string is what the identity presents when authenticating, and the Phase 7 principal was created under it.

### Web container

```
PORT          4000
API_ORIGIN    https://<api app fqdn>      # the proxy target
NODE_ENV      production
```

Runs as non-root `node` (uid 1000), serves static files, answers deep links with `index.html`, and proxies `/api` and `/hubs` including WebSocket upgrades. **It must run on Node, not Bun** — under Bun the WebSocket upgrade handshake returns nothing and every SignalR connection fails. There is no server-side rendering any more ([decision §20](infrastructure-decisions.md#20-no-server-side-rendering)); the process exists for the proxy, which is what keeps the browser same-origin and the CORS list empty.

There is no `/health` endpoint on the web container. Probe `/` — it returns the app shell.

### Migration job

Application startup no longer migrates. Schema belongs to a job that runs the migrator identity, before the new revision is deployed:

- `AZURE_CLIENT_ID` = migrator identity client id, `Database__Username` = `id-migrator-<env>`, `Database__UseEntraAuth=true`.
- The design-time factory is already Entra-aware, so an `efbundle` authenticates the same way the app does — no connection string can express a rotating token, which is why this works at all.
- Migrations include `20260726120000_AddDistributedCacheTable`, which creates the `cache_entries` table the distributed cache needs. Runtime has `CreateIfNotExists=false` and no `CREATE` on the schema, so **the job must run before the app starts or every cache write fails**.
- Expand/contract only: the previous revision keeps serving while the job runs.

---

## 5. Key Vault references

Vaults are RBAC-authorised. The app identity already holds **Key Vault Secrets User** and **Key Vault Crypto User** on its own environment's vault — granted by the `key-vault` module, so do not add access policies.

Build versionless secret references and let Container Apps resolve them under the runtime identity:

```hcl
key_vault_secret_id = "${module.key_vault.vault_uri}secrets/<name>"
```

Never use a Key Vault secret `data` source: the provider reads `value` and OpenTofu persists data-source attributes in state ([decision §7](infrastructure-decisions.md#7-key-vault-secrets-created-out-of-band)).

The JWT signing key is **not** a secret reference — it is a key the app reaches directly through `Jwt__KeyVaultKeyId`. Nothing about it needs to pass through Container Apps secrets.

---

## 6. Terraform layout and how to run it

```
.tofu/bootstrap/      state storage, GitHub OIDC identities, role assignments
.tofu/envs/shared/    PostgreSQL, DNS (deferred)
.tofu/envs/dev/       identities, key vault, Container Apps, diagnostics, roles
.tofu/envs/prod/      same
.tofu/modules/        database, dns, workload-identities, key-vault
```

Each layer has its own state container. `terraform.tfvars` is gitignored; copy from `terraform.tfvars.example` and set `subscription_id`, `location`, and `secret_officer_object_ids` (your own object ID from `az ad signed-in-user show --query id -o tsv`).

```bash
cd .tofu/envs/dev
tofu init
tofu plan -out=tfplan
tofu apply tfplan          # never pipe apply into tail — the exit code comes from the pipe
```

Conventions worth matching: modules take a `env` variable validated to `dev|prod`; outputs carry names, IDs, and URIs but never credentials; comments explain *why*, particularly where a choice looks odd.

**Role assignments need propagation time.** The `key-vault` module uses a `time_sleep` of 60 s between granting a data-plane role and using it, because the first apply otherwise fails with a `Forbidden` that disappears on retry — the kind of failure people learn to retry past instead of read. Do the same for any new data-plane grant.

---

## 7. What Phase 10 built

Phase 10 kept the guide's **10A foundation, then 10B workloads** split, with
the previously completed Key Vault resources reused.

**10A completed:** one shared, capped Log Analytics workspace; one Container
Apps environment per application environment; exact PostgreSQL rules for live
outbound addresses; and the approved diagnostic settings.

**10B completed:** one manual migration job, API app, and web app per
environment, plus all six resource-scoped role assignments.

The implemented constraints worth preserving are:

- `ignore_changes` on the container image, so the deploy pipeline owns the tag and Tofu does not revert it to the quickstart image ([decision §14](infrastructure-decisions.md#14-ignore_changes-on-the-container-image)).
- **Keep deploy identities narrowed to their three environment-specific
  resources.** The former group-scoped
  `azurerm_role_assignment.deploy` entries are absent from bootstrap state and
  configuration. Reintroducing either would let one environment's deploy
  identity update the other's Container Apps because both environments share
  one resource group. Decision
  [§19](infrastructure-decisions.md#19-one-resource-group-for-everything)
  accepts the flattened resource group only with this narrower routine
  deployment boundary.
- Images come from public GHCR, so there is no `registry` block and no `AcrPull` assignment.

---

## 8. Things that will bite you

Each of these was found by testing, not by reading:

- **`Proxy__ForwardLimit` is 2.** Ingress and the web proxy are two hops. One makes the API see the web container as the client.
- **The web server must be Node.** Bun silently fails WebSocket upgrade proxying — the handshake returns nothing, no error.
- **Digest-pin base images with the multi-arch *index* digest**, not a platform-specific one. A linux/amd64 digest builds fine in CI and dies under qemu on an arm64 laptop.
- **`az postgres flexible-server ad-admin` does not exist** in CLI 2.88. It is `microsoft-entra-admin`.
- **`az keyvault key create --kty oct`** is rejected on a Standard vault, and `oct-HSM` needs a Managed HSM. This is why JWT signing is RS256 rather than a shared secret.
- **The local Docker VM has 975 MiB**, which the Angular build OOMs. CI is fine; local `docker compose --profile full up` is not.
- **A flaky integration test** — `CreatePurchaseOrderDraft_ConcurrentCreates_…` fails occasionally under parallel load and passes alone. Pre-existing, unrelated to infrastructure.

---

## 9. Outstanding work not in Phase 10

- **8.4 telemetry instruments** — deliberately deferred by the owner. OpenTelemetry and the OTLP exporter are registered, but there are no metrics for database pool wait, PostgreSQL token refresh, cache failures, SignalR connections, migration version, replica restarts, or external-service latency. The Phase 10 workspace now exists; adding the application instruments remains later work.
- **Atomic rate limiting** — still a distributed-cache read/modify/write. Deprioritised by the owner.
- **Azure SignalR or a backplane** — needed only above one API replica.
- **SMTP credential rotation** — an account action; the value is not in git history.
- **Integration secrets** — nothing is enabled yet; add the telemetry key when it exists.

---

## 10. Verifying you have not broken anything

```bash
dotnet build src/backend/Intelibill.slnx
dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests          # 402
dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests  # 931
dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests       # 201
dotnet test tests/backend/integration/Intelibill.Integration.Tests # 341, needs Docker
cd src/frontend && bun run build && bun run test                   # 1464
cd src/mobile/android/intelibill_mobile && flutter analyze && flutter test  # 1364
```

The four-way database isolation check from guide 7.5 is the one to re-run after touching anything about identities or grants: it must return `t,t,f,f`.
