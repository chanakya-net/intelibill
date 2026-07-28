# Development Container App Cooldown Design

**Date:** 2026-07-28
**Status:** Approved
**Scope:** Development API and web Container Apps

## Problem

The development API and web Container Apps currently omit an explicit scale
cooldown. Azure therefore uses its 300-second default and can scale the final
replica to zero five minutes after activity stops. That idle window is too short
for the desired development workflow and causes avoidable cold starts between
nearby requests.

## Decision

Set `cooldown_period_in_seconds` to `1800` for both development Container Apps.
Keep `min_replicas = 0`, so both apps can still scale to zero after the longer
idle period.

Apply the setting conditionally in the shared environment-infrastructure
module:

```hcl
cooldown_period_in_seconds = var.env == "dev" ? 1800 : null
```

Production will continue to omit the setting and retain Azure's 300-second
default. The migration job is outside this change because it is manually
triggered and does not use the HTTP scale-to-zero path.

## Alternatives Considered

### Add a module input

A configurable cooldown input would support per-environment tuning, but it adds
an unnecessary public configuration surface for a single fixed development
policy.

### Update the live resources directly

An Azure CLI update would take effect quickly, but OpenTofu would not own the
change and a later apply could remove it. Infrastructure configuration remains
the source of truth.

## Behavior and Cost

Azure applies the cooldown when scaling from the final replica to zero. After
development traffic stops, each app can therefore remain idle for up to
30 minutes instead of five minutes. This reduces cold starts during active
development sessions while preserving scale-to-zero outside those sessions.

The longer idle window can add up to 25 minutes of idle replica billing per app
after a burst of traffic. Production cost and behavior remain unchanged.

## Verification

- Update the environment-infrastructure contract test first and confirm it
  fails while the default cooldown is still in effect.
- Assert that both development Container Apps use a 1,800-second cooldown.
- Assert that production has no explicit cooldown override.
- Apply the minimal template changes and confirm the module contract test
  passes.
- Run the development and production root-module tests to verify the shared
  module remains valid in both environments.
- Run `tofu fmt -check -recursive` and `tofu validate` for the affected
  configurations.

## Out of Scope

- Changing production cooldown behavior.
- Keeping a minimum replica permanently active.
- Adding a keep-warm schedule.
- Changing replica limits, scale rules, probes, or application code.
