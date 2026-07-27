# Phase 11 Delivery Pipelines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add tested GitHub Actions workflows that plan/apply OpenTofu and build, migrate, deploy, smoke-test, and safely roll back digest-pinned Intelibill releases.

**Architecture:** Pull requests use a read-only, repository-scoped Azure OIDC identity to plan the three environment states. Merges apply saved plans through shared/dev/prod environment gates. Application releases build API, web, and migration images once in public GHCR, run the existing Container Apps migration job, deploy dev, and promote the identical digests through the production reviewer gate.

**Tech Stack:** GitHub Actions, OpenTofu 1.12.5, Azure CLI 2.88, Docker Buildx/BuildKit, GHCR, .NET 10/EF Core 10.0.4, Bun 1.3.11, Angular 21, Bash, Ruby/Psych contract validation.

**Verification record:** All helper/workflow contracts, shell checks, six
OpenTofu roots, backend build plus 1,876 tests, and frontend build plus 1,464
tests passed locally. EF migration bundles were built for Linux x86-64 and
ARM64, and the standalone runtime path was exercised without a source tree.
The local Docker VM exposes only 975 MiB, so all Dockerfiles passed BuildKit's
static build check while full OCI builds remain enforced by the pull-request
workflow. The existing multi-platform provider locks remained unchanged; an
explicit registry refresh was interrupted after the registry stalled, while
all locked local OpenTofu tests passed.

## Global Constraints

- Work in the existing `infra-setup` checkout because the user explicitly requested the current branch.
- Preserve unrelated user changes and stage only Phase 11 files.
- Use strict red-green-refactor for executable helpers and workflow contracts.
- Use public GHCR with no Container Apps registry credential or `AcrPull` assignment.
- Build API, web, and migration artifacts once and promote only immutable `@sha256:` references.
- Use `AZURE_CLIENT_ID_PLAN` only for read-only PR plans, `AZURE_CLIENT_ID_INFRA` only for environment applies, and `AZURE_CLIENT_ID_DEPLOY` only for routine releases.
- Never use `pull_request_target`, a stored Azure client secret, a database password, or a Key Vault value in CI.
- Run the migration job to `Succeeded` before updating either application image.
- Keep dev before prod and require `environment: prod` before production Azure login.
- Keep API and web rollback references when a post-migration deployment fails.
- Guard every ordinary shared saved plan with `.tofu/scripts/guard-shared-egress-plan.sh`.
- Do not automate retained outbound-address transitions; fail closed and use the Phase 10 handoff procedure.
- Push the current branch after one final verified commit; do not open a PR unless separately requested.

---

### Task 1: Add executable deployment helper tests

**Files:**

- Create: `.github/scripts/tests/wait-for-container-app-job.test.sh`
- Create: `.github/scripts/tests/wait-for-container-app-revisions.test.sh`
- Create: `.github/scripts/tests/validate-phase-11-workflows.test.sh`

**Interfaces:**

- Consumes: fake `az`, `curl`, `sleep`, and workflow YAML fixtures in temporary directories.
- Produces: behavior contracts for `wait-for-container-app-job.sh`, `wait-for-container-app-revisions.sh`, and the three workflow files.

- [x] **Step 1: Write the failing job-wait tests**

Create a fake `az` executable that returns literal execution states from a
newline-delimited fixture. Assert:

```text
Running → Succeeded exits 0
Running → Failed exits nonzero
an empty status exits nonzero
repeated Running past MAX_POLLS exits nonzero
```

The production change caught is accepting a failed, malformed, or unbounded
migration execution.

- [x] **Step 2: Write the failing revision-wait tests**

Return controlled JSON for API/web revision queries and controlled HTTP codes
for the public web checks. Assert healthy `Running` and `ScaledToZero` revisions
plus `200,200` exit zero, while multiple active revisions, unhealthy state, or
a non-200 proxy response exit nonzero.

The production change caught is promoting an unhealthy or partially routed
release.

- [x] **Step 3: Write the failing workflow-structure validator**

Use Ruby/Psych to load all three workflows as mappings and assert semantic
properties rather than source lines:

```ruby
assert "pull_request_target" not in plan_triggers
assert plan_permissions == {
    "contents": "read",
    "id-token": "write",
    "pull-requests": "write",
}
assert apply_jobs["dev"]["needs"] == "shared"
assert apply_jobs["prod"]["needs"] == "dev"
assert deploy_jobs["dev"]["needs"] == ["build"]
assert deploy_jobs["prod"]["needs"] == ["build", "dev"]
assert deploy_jobs["prod"]["environment"] == "prod"
```

Also inspect flattened job step commands to prove the plan/deploy identity
variables do not cross, migration commands precede app updates, production uses
build-job digest outputs, and concurrency is declared.

- [x] **Step 4: Run all three test files and confirm RED**

Run:

```bash
.github/scripts/tests/wait-for-container-app-job.test.sh
.github/scripts/tests/wait-for-container-app-revisions.test.sh
.github/scripts/tests/validate-phase-11-workflows.test.sh
```

Expected: each fails because its production helper or workflows do not exist.

---

### Task 2: Implement bounded Azure deployment helpers

**Files:**

- Create: `.github/scripts/wait-for-container-app-job.sh`
- Create: `.github/scripts/wait-for-container-app-revisions.sh`
- Test: `.github/scripts/tests/wait-for-container-app-job.test.sh`
- Test: `.github/scripts/tests/wait-for-container-app-revisions.test.sh`

**Interfaces:**

- `wait-for-container-app-job.sh RESOURCE_GROUP JOB_NAME EXECUTION_NAME`
- `wait-for-container-app-revisions.sh RESOURCE_GROUP API_NAME WEB_NAME`
- Optional test/runtime inputs: `MAX_POLLS` and `POLL_INTERVAL_SECONDS`.

- [x] **Step 1: Implement migration execution polling**

Validate all positional inputs, default `MAX_POLLS=120` and
`POLL_INTERVAL_SECONDS=5`, and query:

```bash
az containerapp job execution show \
  --resource-group "${resource_group}" \
  --name "${job_name}" \
  --job-execution-name "${execution_name}" \
  --query properties.status --output tsv
```

Continue only for `Running|Processing|Pending`; return zero only for
`Succeeded`; fail immediately for every other nonempty state and for malformed
empty output. Time out after the exact poll budget.

- [x] **Step 2: Implement revision and public-route polling**

Require one active revision per app whose `properties.healthState` is
`Healthy` and `properties.runningState` is `Running`. Resolve the web FQDN,
then require bounded HTTP 200 responses from `/` and `/api/ping`.

- [x] **Step 3: Run helper tests and confirm GREEN**

Run:

```bash
.github/scripts/tests/wait-for-container-app-job.test.sh
.github/scripts/tests/wait-for-container-app-revisions.test.sh
```

Expected: every success/error/timeout case passes.

---

### Task 3: Build the migration artifact

**Files:**

- Create: `src/backend/Dockerfile.migrate`
- Create: `.github/scripts/tests/migration-image-contract.test.sh`

**Interfaces:**

- Consumes repository .NET projects, `global.json`, central package versions,
  and the existing `ApplicationDbContextFactory`.
- Produces an OCI image whose entrypoint is `/app/efbundle`.

- [x] **Step 1: Write the failing executable image contract**

Build the Dockerfile when Docker is available and inspect the resulting image
configuration. Assert it runs as `app`, has entrypoint `/app/efbundle`, and has
no API server command. If the local daemon is unavailable, run
`docker buildx build --check` and retain the full build for CI.

- [x] **Step 2: Confirm RED**

Run:

```bash
.github/scripts/tests/migration-image-contract.test.sh
```

Expected: failure because `src/backend/Dockerfile.migrate` does not exist.

- [x] **Step 3: Implement the migration Dockerfile**

Use the API Dockerfile's reviewed multi-architecture SDK/runtime index digests.
Install `dotnet-ef` version `10.0.4` in the build stage, restore the API and
Infrastructure projects, and run:

```bash
dotnet ef migrations bundle \
  --project src/backend/Intelibill.Infrastructure/Intelibill.Infrastructure.csproj \
  --startup-project src/backend/Intelibill.Api/Intelibill.Api.csproj \
  --configuration Release \
  --self-contained \
  --target-runtime linux-x64 \
  --output /out/efbundle
```

The GitHub build supplies `TARGETARCH`; map `amd64` to `linux-x64` and `arm64`
to `linux-arm64`. Copy only the executable into the runtime image, install
`libgssapi-krb5-2` and CA certificates, switch to `USER $APP_UID`, and use
`ENTRYPOINT ["/app/efbundle"]`.

- [x] **Step 4: Run GREEN verification**

Run the image contract, then build both `linux/amd64` and `linux/arm64` through
Buildx when the builder supports them.

---

### Task 4: Implement the three workflows and lock dependencies

**Files:**

- Create: `.github/workflows/infra-plan.yml`
- Create: `.github/workflows/infra-apply.yml`
- Create: `.github/workflows/deploy.yml`
- Modify: `.terraform.lock.hcl` files only when `tofu providers lock` produces
  additional platform checksums.
- Test: `.github/scripts/tests/validate-phase-11-workflows.test.sh`

**Interfaces:**

- Consumes GitHub variables named in the design and the three helper scripts.
- Produces three independently triggerable GitHub Actions workflows and
  `api-image`, `web-image`, and `migrate-image` build outputs.

- [x] **Step 1: Implement `infra-plan.yml`**

Use pinned action SHAs. Filter same-repository PRs before login, plan
`shared|dev|prod` through a matrix, preserve detailed exit codes, truncate PR
comments to GitHub's body limit, and fail after posting when plan exit code is
`1`.

- [x] **Step 2: Implement `infra-apply.yml`**

Serialize with concurrency `infra-apply-main`, declare `shared → dev → prod`,
enter each GitHub environment before Azure login, and use saved plans.
Shared must run both egress scripts around apply.

- [x] **Step 3: Implement `deploy.yml`**

Validate backend/frontend, build and push three images once with source labels,
SBOM, provenance, and vulnerability scanning. Dev and prod use only build-job
digest outputs. Each environment updates/starts/waits for migration before app
updates, records prior image references, runs smoke checks, and restores prior
references on failure. `GHCR_PUBLIC` gates every Azure deployment.

- [x] **Step 4: Run workflow contracts and confirm GREEN**

Run:

```bash
.github/scripts/tests/validate-phase-11-workflows.test.sh
```

Then parse each workflow with Ruby/Psych and run `actionlint` if available.

- [x] **Step 5: Refresh provider locks**

For every OpenTofu root used in CI, run:

```bash
tofu -chdir=<root> providers lock \
  -platform=linux_amd64 \
  -platform=darwin_arm64
```

Commit only `.terraform.lock.hcl`, never `.terraform/`.

---

### Task 5: Configure GitHub, verify, document, commit, and push

**Files:**

- Modify: `docs/infrastructure-implementation-guide.md`
- Modify: `docs/phase-10-handoff.md`
- Modify: `docs/superpowers/specs/2026-07-27-phase-11-pipelines-design.md`
- Modify: `docs/superpowers/plans/2026-07-27-phase-11-pipelines.md`

**Interfaces:**

- Consumes existing local gitignored tfvars and GitHub repository/environment
  configuration.
- Produces repository variables, non-bypassable shared/prod gates, a verified
  commit, and an updated `origin/infra-setup`.

- [x] **Step 1: Create non-secret repository variables**

Read values locally without printing them and run:

```bash
gh variable set TOFU_ADMIN_OBJECT_ID
gh variable set TOFU_ADMIN_PRINCIPAL_NAME
gh variable set TOFU_SECRET_OFFICER_OBJECT_IDS
gh variable set GHCR_PUBLIC --body false
```

- [x] **Step 2: Disable environment administrator bypass**

Update `prod` and `shared` with `can_admins_bypass=false`, preserving required
reviewers and protected-branch policies. Read both environments back and fail
unless bypass is false and reviewer rules remain.

- [x] **Step 3: Reconcile the runbook**

Mark the hosted-runner temporary-firewall migration snippet as superseded for
the applied Phase 10 topology. Document the Container Apps job path and the
one-time GHCR visibility bootstrap.

- [x] **Step 4: Run complete verification**

Run:

```bash
.github/scripts/tests/*.test.sh
.tofu/bootstrap/tests/deploy-role.test.sh
.tofu/scripts/tests/check-container-app-egress.test.sh
.tofu/scripts/tests/guard-shared-egress-plan.test.sh
tofu fmt -check -recursive .tofu
tofu test  # each existing module/environment test root
dotnet build src/backend/Intelibill.slnx --configuration Release
dotnet test src/backend/Intelibill.slnx --configuration Release --no-build
(cd src/frontend && bun install --frozen-lockfile && bun run build && bun run test)
docker build -f src/backend/Dockerfile .
docker build -f src/frontend/Dockerfile .
docker build -f src/backend/Dockerfile.migrate .
```

If Docker is unavailable locally, record that exact limitation and rely on the
GitHub build job after merge; do not claim local image builds passed.

- [x] **Step 5: Review the complete diff**

Check identity separation, workflow permissions, trigger safety, expression
semantics, migration/app order, rollback behavior, shell quoting, secret
exposure, and absence of unrelated changes. Fix every critical or important
finding and rerun affected verification.

- [x] **Step 6: Commit and push the current branch**

Stage only Phase 11 paths, commit:

```text
feat(infra): add phase 11 delivery pipelines
```

Push with:

```bash
git push -u origin infra-setup
```

Do not create a pull request because the user requested only a current-branch
commit and push.
