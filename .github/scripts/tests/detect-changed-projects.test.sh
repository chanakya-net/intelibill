#!/usr/bin/env bash
set -euo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/detect-changed-projects.sh"

failures=0

# expect <description> <output key> <expected value> <changed paths...>
expect() {
  local description="$1" key="$2" expected="$3"
  shift 3
  local actual
  actual="$(printf '%s\n' "$@" | "${script}" | sed -n "s/^${key}=//p")"
  if [ "${actual}" != "${expected}" ]; then
    printf 'FAIL: %s\n  %s\n  expected: %s\n  actual:   %s\n' \
      "${description}" "${key}" "${expected}" "${actual}" >&2
    failures=$((failures + 1))
  fi
}

merchant_suite='{"name":"merchant","roots":"tests/backend/intelibill tests/backend/shared"}'
licensing_suite='{"name":"licensing","roots":"tests/backend/licensing tests/backend/shared"}'
licensing_only_suite='{"name":"licensing","roots":"tests/backend/licensing"}'
shared_suite='{"name":"shared","roots":"tests/backend/shared"}'
api_image='{"name":"api","dockerfile":"src/backend/intelibill/Dockerfile","tag":"intelibill-api-pr"}'
migrate_image='{"name":"migrate","dockerfile":"src/backend/intelibill/Dockerfile.migrate","tag":"intelibill-migrate-pr"}'
licensing_api_image='{"name":"licensing-api","dockerfile":"src/backend/licensing/Dockerfile","tag":"intelibill-licensing-api-pr"}'
licensing_migrate_image='{"name":"licensing-migrate","dockerfile":"src/backend/licensing/Dockerfile.migrate","tag":"intelibill-licensing-migrate-pr"}'
web_image='{"name":"web","dockerfile":"src/frontend/intelibill/Dockerfile","tag":"intelibill-web-pr"}'
admin_image='{"name":"admin-web","dockerfile":"src/frontend/licensing/Dockerfile","tag":"intelibill-admin-web-pr"}'
all_suites="[${merchant_suite},${licensing_only_suite}]"
all_images="[${api_image},${migrate_image},${licensing_api_image},${licensing_migrate_image},${web_image},${admin_image}]"

# A merchant API change runs the merchant suite and nothing else.
merchant_change=src/backend/intelibill/Intelibill.Api/Program.cs
expect 'merchant API' backend_suites "[${merchant_suite}]" "${merchant_change}"
expect 'merchant API' images "[${api_image},${migrate_image}]" "${merchant_change}"
expect 'merchant API' merchant_web false "${merchant_change}"
expect 'merchant API' admin_web false "${merchant_change}"
expect 'merchant tests' backend_suites "[${merchant_suite}]" \
  tests/backend/intelibill/unit/Intelibill.Domain.Unit.Tests/SomeTests.cs

# A licensing change runs the licensing suite and nothing else.
licensing_change=src/backend/licensing/Intelibill.Licensing.Api/Program.cs
expect 'licensing API' backend_suites "[${licensing_suite}]" "${licensing_change}"
expect 'licensing API' images "[${licensing_api_image},${licensing_migrate_image}]" "${licensing_change}"
expect 'licensing API' merchant_web false "${licensing_change}"
expect 'licensing contracts' backend_suites "[${licensing_suite}]" \
  tests/contracts/licensing/billing-accounts-v1.json

# A web change runs that web app and no backend.
merchant_web_change=src/frontend/intelibill/src/app/app.ts
expect 'merchant web' merchant_web true "${merchant_web_change}"
expect 'merchant web' admin_web false "${merchant_web_change}"
expect 'merchant web' backend_suites '[]' "${merchant_web_change}"
expect 'merchant web' images "[${web_image}]" "${merchant_web_change}"
admin_web_change=src/frontend/licensing/src/app/app.ts
expect 'admin web' admin_web true "${admin_web_change}"
expect 'admin web' merchant_web false "${admin_web_change}"
expect 'admin web' backend_suites '[]' "${admin_web_change}"
expect 'admin web' images "[${admin_image}]" "${admin_web_change}"

# Shared code reaches every consumer.
expect 'shared backend library' backend_suites "${all_suites}" \
  src/backend/shared/Intelibill.Shared.Web/SafeErrorFactory.cs
expect 'shared backend library' merchant_web false \
  src/backend/shared/Intelibill.Shared.Web/SafeErrorFactory.cs
expect 'shared web package' merchant_web true src/frontend/shared/ui/src/index.ts
expect 'shared web package' admin_web true src/frontend/shared/ui/src/index.ts
expect 'shared web package' backend_suites '[]' src/frontend/shared/ui/src/index.ts
expect 'lockfile' images "[${web_image},${admin_image}]" bun.lock
expect 'database scripts' backend_suites "${all_suites}" scripts/db/verify-tenant-policies.sql

# The consumer regression suites run alone only when nothing else is selected.
expect 'shared regressions' backend_suites "[${shared_suite}]" \
  tests/backend/shared/P0102ConsumerRegressionTests/SomeTests.cs
expect 'shared regressions' images '[]' \
  tests/backend/shared/P0102ConsumerRegressionTests/SomeTests.cs

# Repository-wide inputs select everything.
for path in .github/workflows/deploy.yml .github/scripts/detect-changed-projects.sh \
  Directory.Build.props Directory.Packages.props global.json Intelibill.slnx .dockerignore; do
  expect "${path}" backend_suites "${all_suites}" "${path}"
  expect "${path}" images "${all_images}" "${path}"
  expect "${path}" merchant_web true "${path}"
  expect "${path}" admin_web true "${path}"
done

# Documentation selects nothing, wherever it lives.
for path in README.md docs/licensing-rollout.md src/backend/intelibill/README.md \
  src/frontend/intelibill/architecture_doc/architecture.md \
  tests/backend/licensing/acceptance/p01-foundation.md; do
  expect "${path}" backend_suites '[]' "${path}"
  expect "${path}" images '[]' "${path}"
  expect "${path}" merchant_web false "${path}"
  expect "${path}" admin_web false "${path}"
done

# Projects with their own workflows select nothing here.
expect 'mobile' backend_suites '[]' src/mobile/android/intelibill_mobile/lib/main.dart
expect 'barcode service' backend_suites '[]' src/backend/barcode-master-api/BarcodeAPI.Api/Program.cs

# Mixed changes are the union of their parts.
expect 'merchant API and web' backend_suites "[${merchant_suite}]" \
  "${merchant_change}" "${merchant_web_change}" README.md
expect 'merchant API and web' images "[${api_image},${migrate_image},${web_image}]" \
  "${merchant_change}" "${merchant_web_change}" README.md
expect 'both backends' backend_suites "${all_suites}" "${merchant_change}" "${licensing_change}"

# --all ignores the changed paths.
all_outputs="$("${script}" --all </dev/null)"
for line in "backend_suites=${all_suites}" "images=${all_images}" merchant_web=true admin_web=true; do
  if ! grep -Fqx -- "${line}" <<<"${all_outputs}"; then
    printf 'FAIL: --all\n  missing: %s\n' "${line}" >&2
    failures=$((failures + 1))
  fi
done

# No changed paths at all is valid and selects nothing.
empty_outputs="$("${script}" </dev/null)"
for line in 'backend_suites=[]' 'images=[]' merchant_web=false admin_web=false; do
  if ! grep -Fqx -- "${line}" <<<"${empty_outputs}"; then
    printf 'FAIL: no changes\n  missing: %s\n' "${line}" >&2
    failures=$((failures + 1))
  fi
done

if [ "${failures}" -gt 0 ]; then
  printf '%d change-detection expectation(s) failed.\n' "${failures}" >&2
  exit 1
fi

echo 'Change detection passed.'
