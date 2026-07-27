# Phase 11 Delivery Pipelines Design

**Date:** 2026-07-27

**Status:** Implemented and locally verified on `infra-setup`; activation
awaits merge and public GHCR packages

**Branch:** `infra-setup`

## Purpose

Phase 10 created the Azure environments, workload identities, Key Vaults,
Container Apps, and manual migration jobs, but intentionally left immutable
bootstrap images running. Phase 11 creates the repository delivery system that
reviews infrastructure changes, applies approved changes, builds immutable
application artifacts, migrates each database, and promotes the same tested
artifacts from dev to production.

The first Phase 11 merge publishes three GHCR packages. Deployment remains
disabled until those packages have been made public because Container Apps has
no registry credential by design. After that one-time gate, pushes to `main`
release to dev and then pause at the protected production environment before
promoting the same digests.

## Scope

Phase 11 adds:

- a trusted same-repository pull-request OpenTofu plan workflow;
- a gated, sequential shared/dev/prod OpenTofu apply workflow;
- one build-once application release workflow;
- a self-contained EF Core migration-bundle image;
- bounded helpers that wait for a Container Apps job execution and workload
  revisions;
- Ruby/Psych contract tests for helper behavior and workflow security/order;
- committed provider lock files for every root used in CI;
- non-secret GitHub variables required to replace local `terraform.tfvars`.

Phase 11 does not:

- merge `infra-setup` to `main`;
- make a GHCR package public automatically, because that visibility change is
  irreversible;
- configure custom domains, SMTP, OAuth, New Relic, a SignalR backplane, or
  keep-warm schedules;
- increase the API replica cap above one;
- apply schema migrations from API startup or a GitHub-hosted runner.

## Chosen Delivery Architecture

### Infrastructure plan

`.github/workflows/infra-plan.yml` runs only for same-repository pull requests
that touch the environment roots, modules, scripts, or the workflow itself. It
deliberately excludes the manual bootstrap root. It never uses
`pull_request_target` and declares no GitHub environment. It authenticates with
the repository-scoped plan identity, which can read Azure resources and the
three environment state containers but cannot mutate them.

The workflow plans `shared`, `dev`, and `prod` independently, renders the saved
plan, and maintains one bounded sticky PR comment per layer. Fork pull requests
are skipped before OIDC login so untrusted code cannot read state.

Bootstrap remains manual. Its state and permissions create the identities used
by the workflows themselves, and the plan identity deliberately has no access
to bootstrap state.

### Infrastructure apply

`.github/workflows/infra-apply.yml` runs on pushes to `main` that touch the
three environment roots, modules, scripts, or the workflow. Jobs are strictly
ordered:

```text
shared → dev → prod
```

`shared` enters the protected `shared` environment before obtaining the
group-scoped infrastructure identity. It creates a saved plan, shows it, runs
`.tofu/scripts/guard-shared-egress-plan.sh`, applies that exact plan, and
verifies the live firewall inventory. Dev and prod also apply saved plans;
prod enters its protected environment before login.

The ordinary workflow fails closed for Container Apps environment replacement
or outbound-address transitions. Those changes require the retained-address
two-apply procedure in `docs/phase-10-handoff.md`, because deleting old rules
before a new revision is ready can cut off the running application.

### Application delivery

`.github/workflows/deploy.yml` validates the backend and frontend, then builds
and pushes these public GHCR packages exactly once:

```text
ghcr.io/chanakya-net/intelibill/api
ghcr.io/chanakya-net/intelibill/web
ghcr.io/chanakya-net/intelibill/migrate
```

Every image is labeled with its source repository, receives SBOM and provenance
attestations, is scanned for high and critical vulnerabilities, and is passed
between jobs as an `@sha256:` reference. Production never rebuilds.

Release order is:

```text
validate → build/scan
  → dev migration → dev API/web update → dev smoke
  → production approval
  → prod migration → prod API/web update → prod smoke
```

The workflow supports manual `publish`, `dev`, and `prod` targets. Pushes to
`main` request a full release, but environment jobs run only when repository
variable `GHCR_PUBLIC` equals `true`. The first merge therefore publishes and
scans private packages without attempting an Azure pull. After the owner makes
all three packages public and changes `GHCR_PUBLIC` to `true`, a manual `prod`
run performs the first dev-to-prod promotion and future pushes release
automatically.

Each environment job captures the previous API and web image references before
updating them. If an application update or smoke test fails, it restores those
references. Migrations are not rolled back; every migration must follow
expand/contract compatibility so the previous application revision can keep
serving.

## Migration Image

`src/backend/Dockerfile.migrate` uses the same digest-pinned .NET SDK and
runtime families as the API image. Its build stage installs the exact
`dotnet-ef` version used by the repository, restores the backend, and creates a
linux-arm64/linux-x64-compatible migration bundle through
`ApplicationDbContextFactory`.

The runtime stage contains the bundle and the libraries required for PostgreSQL
and Entra authentication, runs as the base image's non-root `app` user, and
executes only the bundle. It receives the existing migration-job environment:

```text
AZURE_CLIENT_ID=<migrator identity client ID>
Database__Host=intelibill-pg-01.postgres.database.azure.com
Database__Port=5432
Database__Database=intelibill_dev | intelibill_prod
Database__Username=id-migrator-dev | id-migrator-prod
Database__UseEntraAuth=true
```

The deploy identity updates the existing job image, starts exactly one
execution, and polls that named execution until `Succeeded`. `Failed`,
`Stopped`, `Degraded`, malformed responses, or the bounded timeout stop the
release before either application image changes.

## Smoke and Rollback Contract

After each app update, the workflow waits for exactly one active API revision
and one active web revision to report healthy and either `Running` or
`ScaledToZero`. The latter is expected because Phase 10 deliberately configured
`minReplicas=0`; the route checks wake both apps. It then resolves the public
web FQDN and requires:

- `GET /` to return the Angular application shell;
- `GET /api/ping` to return HTTP 200 through the web-to-internal-API proxy;
- the API revision readiness state to remain healthy, which includes the
  application's PostgreSQL readiness probe.

The workflow summary records the commit, all three new digests, and both prior
application image references per environment. A failed post-deploy check
restores both prior references and waits for the restored revisions.

## GitHub and Identity Inputs

No client secret, database password, or registry pull credential is introduced.
Workflows consume the existing variables:

```text
AZURE_CLIENT_ID_PLAN
AZURE_CLIENT_ID_INFRA
AZURE_CLIENT_ID_DEPLOY
AZURE_SUBSCRIPTION_ID
AZURE_TENANT_ID
```

The implementation also creates repository variables populated from the
existing gitignored tfvars:

```text
TOFU_ADMIN_OBJECT_ID
TOFU_ADMIN_PRINCIPAL_NAME
TOFU_SECRET_OFFICER_OBJECT_IDS
GHCR_PUBLIC=false
```

The first three values are identifiers, not credentials. Workflow steps map
them to the exact `TF_VAR_*` names required by the roots.

The `prod` and `shared` environments retain required reviewers and protected
branch policies. Administrator bypass must be disabled before the workflows are
considered a production control.

## Error Handling

- Fork PRs never request an Azure OIDC token.
- Plan exit code `1` fails after publishing bounded diagnostics; exit code `2`
  means a valid plan containing changes.
- Every apply uses the exact saved plan it displayed.
- Shared apply cannot bypass the live egress inventory guard.
- Migration polling accepts only one named execution and only `Succeeded`.
- A failed migration stops before app deployment.
- A failed application update or smoke test restores both prior image
  references.
- Concurrency groups serialize infrastructure applies and production releases.
- No workflow prints local tfvars, Azure token material, or Key Vault values.

## Testing and Acceptance

Implementation is accepted when:

1. helper tests demonstrate success, terminal failure, malformed status, and
   timeout behavior against executable fake Azure CLI fixtures;
2. workflow structure validation proves trusted PR filtering, minimal
   permissions, OIDC identity separation, shared/dev/prod ordering, build-once
   digest promotion, migration-before-deploy, protected production use, and
   concurrency;
3. every OpenTofu contract test and validation passes;
4. backend build/tests and frontend build/tests pass;
5. all three container images build locally or in the pushed workflow;
6. GitHub variables exist and `prod`/`shared` administrator bypass is disabled;
7. the committed branch is pushed without unrelated working-tree changes.

Live deployment cannot occur from a workflow that exists only on
`infra-setup`; it begins after this branch is reviewed and merged to `main`.
