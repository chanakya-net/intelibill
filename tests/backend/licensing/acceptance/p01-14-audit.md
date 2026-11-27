# P01-14 provider audit history

The Provider Console offers **Audit history** to the enrolled bootstrap owner through the live `admin.audit.identity.read` grant. The identity-only GET returns at most 100 rows in descending UTC instant and event ID order. **From (UTC)** includes the specified instant, and **Before (UTC)** excludes it. Seeking through older pages keeps the cursor tied to the filters. Choose **Audit history inspected** to see successful audit reads.

Expand **Event details** to inspect the safe actor, reason, correlation ID and recorded before/after values. Accepting a fixture invitation records the actual PostgreSQL invitation versions on both sides of the transition. Historical events keep their recorded values and show unavailable metadata where applicable.

## Behavior-first regression

On `ee5c846cc7d7ba4f4dc05976798339b2b80256e8` with only the initial test added, run:

```bash
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0114Tests.OwnerReadsBoundedUtcAuditHistory
```

Real PostgreSQL fixture setup, owner enrollment and MFA sign-in reached HTTP 200. The first authorized audit GET failed in `OwnerReadsBoundedUtcAuditHistory` with `Expected: OK; Actual: NotFound` (exit 1). The subsequent transition test initially found a null version reference. The authentication regression initially recorded one out of three expected audit denials.

## Local browser and migration demonstration

```bash
bun install --frozen-lockfile
bash tests/backend/licensing/acceptance/run-p0114-browser.sh
```

The runner uses a disposable PostgreSQL and Mailpit instance, the source licensing API, the production admin build and Chromium. Both users sign in with a fixture password and authenticator. The owner navigates from the console, filters invitation acceptance, expands both versions, checks the UTC timestamp at a 390px viewport and pages forward and back. A delayed response for an older filter cannot overwrite the current result. An unauthenticated direct visit goes to sign-in. The enrolled invitee has no audit navigation, sees the denied state at `/audit`, and receives a no-store 403 on a direct API request. The audit DOM and response are checked for fixture secret, attestation, exception and merchant-data canaries.

Apply `AddProviderAuditInspection` using the explicit licensing migration job before deploying the readers and writers. The runner verifies a predecessor row digest through upgrade and reapplication, exercises the runtime write and read paths, and checks that the database refuses update, delete and truncate. Down takes an exclusive lock and refuses to discard enriched audit evidence. The integration suite also checks a fresh schema and verifies the EF model against its snapshot.

This evidence uses local fixtures.
