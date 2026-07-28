# Development Container App Cooldown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep both development Container Apps idle for 30 minutes before they
scale to zero while leaving production behavior unchanged.

**Architecture:** The shared environment-infrastructure module will set the
AzureRM Container App template cooldown conditionally from the existing
`var.env` discriminator. Module contract tests will lock the development value
to 1,800 seconds and require production to retain no explicit override.

**Tech Stack:** OpenTofu 1.8+, AzureRM provider 4.81.0, Azure Container Apps

## Global Constraints

- Apply the 1,800-second cooldown to both development Container Apps.
- Keep `min_replicas = 0` for both apps.
- Leave production on Azure's implicit 300-second default.
- Do not change the migration job, replica limits, scale rules, probes, or
  application code.
- Keep the shared environment-infrastructure module as the source of truth.

---

### Task 1: Lock and implement the environment-specific cooldown

**Files:**

- Modify:
  `.tofu/modules/environment-infrastructure/tests/contract.tftest.hcl:156`
- Modify: `.tofu/modules/environment-infrastructure/api.tf:35`
- Modify: `.tofu/modules/environment-infrastructure/web.tf:20`

**Interfaces:**

- Consumes: existing `var.env` string constrained to `dev` or `prod`.
- Produces: `azurerm_container_app.*.template[0].cooldown_period_in_seconds`
  equal to `1800` in development and `null` in production.

- [ ] **Step 1: Write the failing development and production contract assertions**

Extend the development scale assertion in
`.tofu/modules/environment-infrastructure/tests/contract.tftest.hcl`:

```hcl
assert {
  condition = (
    azurerm_container_app.api.template[0].min_replicas == 0 &&
    azurerm_container_app.api.template[0].max_replicas == 1 &&
    azurerm_container_app.api.template[0].cooldown_period_in_seconds == 1800 &&
    azurerm_container_app.api.template[0].http_scale_rule[0].name == "http-concurrency" &&
    azurerm_container_app.api.template[0].http_scale_rule[0].concurrent_requests == "50" &&
    azurerm_container_app.web.template[0].min_replicas == 0 &&
    azurerm_container_app.web.template[0].max_replicas == 1 &&
    azurerm_container_app.web.template[0].cooldown_period_in_seconds == 1800 &&
    azurerm_container_app.web.template[0].http_scale_rule[0].name == "http-concurrency" &&
    azurerm_container_app.web.template[0].http_scale_rule[0].concurrent_requests == "50"
  )
  error_message = "Both dev apps must scale from zero to one on HTTP concurrency 50 and wait 1,800 seconds before returning to zero."
}
```

Extend the `prod_sizing_contract` assertion that checks sizing and replica
caps:

```hcl
assert {
  condition = (
    azurerm_container_app_environment.main.name == "intelibill-prod-env" &&
    azurerm_container_app.api.name == "intelibill-prod-api" &&
    azurerm_container_app.web.name == "intelibill-prod-web" &&
    azurerm_container_app_job.migrate.name == "intelibill-prod-migrate" &&
    azurerm_container_app.api.template[0].container[0].cpu == 0.75 &&
    azurerm_container_app.api.template[0].container[0].memory == "1.5Gi" &&
    azurerm_container_app.web.template[0].container[0].cpu == 0.5 &&
    azurerm_container_app.web.template[0].container[0].memory == "1Gi" &&
    azurerm_container_app.api.template[0].max_replicas == 1 &&
    azurerm_container_app.api.template[0].cooldown_period_in_seconds == null &&
    azurerm_container_app.web.template[0].max_replicas == 1 &&
    azurerm_container_app.web.template[0].cooldown_period_in_seconds == null
  )
  error_message = "Prod must use its names and approved sizing without raising either replica cap or overriding the default cooldown."
}
```

- [ ] **Step 2: Run the module contract test and verify the new development assertion fails**

Run:

```bash
cd .tofu/modules/environment-infrastructure
tofu test -filter=tests/contract.tftest.hcl
```

Expected: `dev_contract` fails because both
`cooldown_period_in_seconds` values are `null`, not `1800`. Confirm that the
failure comes from the new development assertion rather than syntax,
initialization, or provider errors.

- [ ] **Step 3: Add the minimal conditional cooldown to both app templates**

Add this attribute after `max_replicas` in the `template` block of
`.tofu/modules/environment-infrastructure/api.tf`:

```hcl
cooldown_period_in_seconds = var.env == "dev" ? 1800 : null
```

Add the same attribute after `max_replicas` in the `template` block of
`.tofu/modules/environment-infrastructure/web.tf`:

```hcl
cooldown_period_in_seconds = var.env == "dev" ? 1800 : null
```

- [ ] **Step 4: Re-run the module contract test and verify it passes**

Run:

```bash
cd .tofu/modules/environment-infrastructure
tofu test -filter=tests/contract.tftest.hcl
```

Expected: all runs pass, including `dev_contract` with `1800` and
`prod_sizing_contract` with `null`.

- [ ] **Step 5: Format and validate the affected configurations**

Run from the repository root:

```bash
tofu fmt -check -recursive .tofu
tofu -chdir=.tofu/modules/environment-infrastructure validate
tofu -chdir=.tofu/envs/dev validate
tofu -chdir=.tofu/envs/prod validate
```

Expected: all four commands exit successfully with no formatting changes or
validation errors.

- [ ] **Step 6: Run the development and production root-module contract tests**

Run from the repository root:

```bash
tofu -chdir=.tofu/envs/dev test -filter=tests/contract.tftest.hcl
tofu -chdir=.tofu/envs/prod test -filter=tests/contract.tftest.hcl
```

Expected: both environment contract test suites pass.

- [ ] **Step 7: Review the final diff and commit the implementation**

Run:

```bash
git diff --check
git diff -- .tofu/modules/environment-infrastructure/api.tf \
  .tofu/modules/environment-infrastructure/web.tf \
  .tofu/modules/environment-infrastructure/tests/contract.tftest.hcl
git status --short
git add .tofu/modules/environment-infrastructure/api.tf \
  .tofu/modules/environment-infrastructure/web.tf \
  .tofu/modules/environment-infrastructure/tests/contract.tftest.hcl
git commit -m "fix(infra): extend dev scale cooldown"
```

Expected: the diff contains only the two conditional cooldown attributes and
the development/production contract assertions before the implementation
commit is created.
