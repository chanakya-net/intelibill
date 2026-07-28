# API Cold-Start Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to execute this plan.

**Goal:** Stop the development API's OOM/restart cycle and move one-time
Wolverine and EF Core initialization ahead of the first business request while
retaining scale-to-zero.

**Architecture:** OpenTofu gives the development API 1 vCPU / 2 GiB and sets its
minimum replicas to zero. The production image contains pre-generated Wolverine
handler code and uses static loading. A read-only hosted startup service builds
the EF Core model before the host becomes ready.

**Tech Stack:** .NET 10, ASP.NET Core, Wolverine 5.24, JasperFx 1.21, EF Core 10,
xUnit, Azure Container Apps, OpenTofu, Docker.

---

## Task 1: Lock the infrastructure contract

**Files:**

- Modify: `.tofu/modules/environment-infrastructure/tests/contract.tftest.hcl`
- Modify: `.tofu/modules/environment-infrastructure/main.tf`
- Modify: `.tofu/modules/environment-infrastructure/api.tf`

- [ ] Change the dev contract assertion to require 1 vCPU, 2 GiB, and
  `min_replicas == 0`; leave production assertions unchanged.
- [ ] Run `tofu test` in `.tofu/modules/environment-infrastructure` and confirm
  the changed assertions fail against the current module.
- [ ] Change only the dev API resource map to 1 vCPU / 2 GiB.
- [ ] Make the API minimum replica count explicitly zero for both environments.
- [ ] Re-run the module test and confirm it passes.
- [ ] Run the dev root-module contract test in `.tofu/envs/dev`.

## Task 2: Warm the EF Core model before readiness

**Files:**

- Create:
  `src/backend/Intelibill.Api/Startup/ApplicationModelWarmupService.cs`
- Modify: `src/backend/Intelibill.Api/Program.cs`
- Create:
  `tests/backend/unit/Intelibill.Api.Unit.Tests/Startup/ApplicationModelWarmupServiceTests.cs`

- [ ] Write a unit test that resolves the hosted warm-up service, starts it
  against an isolated test context, and verifies that it performs no database
  writes.
- [ ] Run the focused API unit test and confirm it fails because the warm-up
  service/registration does not exist.
- [ ] Implement an `IHostedService` that creates a scope, resolves
  `ApplicationDbContext`, and reads `context.Model` exactly once during startup.
- [ ] Register the service before `builder.Build()`.
- [ ] Re-run the focused test and confirm it passes.

## Task 3: Pre-generate Wolverine handlers for production

**Files:**

- Create:
  `src/backend/Intelibill.Api/Extensions/WolverineCodeGenerationExtensions.cs`
- Modify: `src/backend/Intelibill.Api/Program.cs`
- Modify: `src/backend/Dockerfile`
- Create:
  `tests/backend/unit/Intelibill.Api.Unit.Tests/Wolverine/WolverineCodeGenerationTests.cs`

- [ ] Inspect the installed Wolverine/JasperFx 5.x assemblies and XML
  documentation to verify the exact static-loading and code-generation command
  APIs.
- [ ] Write a unit test proving the production JasperFx profile uses static
  generated-code loading and asserts generated types exist.
- [ ] Run the focused test and confirm it fails before the configuration helper
  exists.
- [ ] Configure production for static loading while preserving dynamic local
  development/test behavior.
- [ ] Update the application entry point if required by the verified Wolverine
  5.x command API.
- [ ] Run Wolverine code generation before `dotnet publish` in the Docker build
  stage.
- [ ] Re-run focused tests and confirm they pass.
- [ ] Build the production Docker image and confirm code generation and publish
  both succeed.

## Task 4: Verify the complete change

**Files:**

- Inspect all files changed by Tasks 1-3.

- [ ] Run `git diff --check`.
- [ ] Run `dotnet build src/backend/Intelibill.slnx`.
- [ ] Run API, application, and domain unit test projects.
- [ ] Run the full backend test command; report any external Docker dependency
  separately if it cannot run.
- [ ] Run all relevant OpenTofu contract tests.
- [ ] Rebuild the production API Docker image from a clean source context.
- [ ] Confirm `min_replicas = 0`, dev 1 vCPU / 2 GiB, production sizing
  unchanged, and no migration/retry behavior was introduced.

## Task 5: Publish a draft pull request

**Files:**

- Include only the design, plan, implementation, and tests listed above.
- Exclude `debug_human_report.md` and `debug_llm_context.md`.

- [ ] Review `git status`, `git diff`, and staged scope.
- [ ] Commit implementation with an intentional conventional commit.
- [ ] Push `codex/fix-api-cold-start`.
- [ ] Open a draft PR to `main` describing the OOM evidence, cost impact,
  scale-to-zero tradeoff, implementation, and verification results.
