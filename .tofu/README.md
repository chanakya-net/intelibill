# Infrastructure (`.tofu`)

OpenTofu configuration for the Azure infrastructure that runs intelibill. Changes are planned and applied through GitHub Actions.

## Layout

| Path | Purpose |
|---|---|
| `bootstrap/` | One-time root: remote state backend, workload identities and their role assignments |
| `envs/shared/` | Resources shared by every environment |
| `envs/dev/`, `envs/prod/` | Per-environment roots |
| `modules/` | Reusable modules: `container-apps-egress`, `database`, `dns`, `environment-infrastructure`, `key-vault`, `shared-monitoring`, `workload-identities` |
| `scripts/` | Plan guards and checks used by the pipelines (`check-container-app-egress.sh`, `guard-shared-egress-plan.sh`) |
| `architecture_doc/` | Infrastructure architecture |

Each root has a `terraform.tfvars.example`. Real `*.tfvars` and `*.tfplan` files are gitignored and must stay out of the repository.

## Tooling

- OpenTofu; roots require `>= 1.8`, the pipelines pin `1.12.5`.
- Tests sit next to the code they cover: `tests/*.tftest.hcl` in roots and modules, shell tests in `bootstrap/tests/` and `scripts/tests/`.

```bash
tofu -chdir=.tofu/envs/dev init
tofu -chdir=.tofu/envs/dev validate
tofu -chdir=.tofu/envs/dev test
```

## Pipelines

| Workflow | Trigger | Does |
|---|---|---|
| `.github/workflows/infra-plan.yml` | Pull requests touching `.tofu/envs/**`, `.tofu/modules/**`, `.tofu/scripts/**` | `init`, `validate` and `plan` per layer; posts the plan on the pull request |
| `.github/workflows/infra-apply.yml` | Pushes to `main` touching the same paths | Applies the layers |

Changes to `bootstrap/` are not covered by these workflows.

## Documentation

- [Architecture](architecture_doc/architecture.md): target design, resource inventory, identity and state, PostgreSQL, application production contract, pipeline order, cost model, risks.
- [Decisions](../docs/infrastructure-decisions.md): what was chosen and rejected, and why.
- [Implementation guide](../docs/infrastructure-implementation-guide.md): phase-by-phase runbook and current status.
- Application images deployed here are built from [`src/backend/intelibill`](../src/backend/intelibill/README.md), [`src/backend/licensing`](../src/backend/licensing/README.md), [`src/frontend/intelibill`](../src/frontend/intelibill/README.md) and [`src/frontend/licensing`](../src/frontend/licensing/README.md).
