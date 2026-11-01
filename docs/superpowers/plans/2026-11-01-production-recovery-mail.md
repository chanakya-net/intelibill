# Production Recovery Mail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Password-reset mail is committed with its proof and delivered by the durable worker, production refuses to start without a working mail configuration, and operators can see queue health.

**Architecture:** Plan 10 (market-readiness) proposed a new `SecurityMailDelivery` queue. Since it was written (HEAD `5485507e`), `main` gained the encrypted `identity_mail` outbox (`IdentityMailOutbox`, AES-GCM under `Identity:OutboxKey`), `IdentityMailWorker` (advisory-lock claim, bounded leased retries, supersession check, payload wiped on completion) and `Email:LocalCapture` for mailpit; `NoOpEmailService` no longer logs links. This plan therefore **reuses** that outbox instead of adding a parallel queue: reset mail joins it via a new `PasswordResetTokenId` column, and the remaining plan-10 gaps are closed around it.

**Tech Stack:** ASP.NET Core 10, EF Core 10/Npgsql, MailKit, xUnit + NSubstitute, Testcontainers.

**Spec:** Pasted plan "Production email and recovery delivery" (plan 10); `docs/reviews/2026-10-21-market-readiness.md`.

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; repositories + IUnitOfWork; warnings-as-errors; CPM.
- Migrations are applied by the migration job, never at API startup.
- Token table stays hash-only; bearer links exist only inside encrypted outbox rows.
- Reset request answers the same generic 200 for known/unknown accounts; global mail unavailability is a uniform 503 `Auth.EmailDeliveryUnavailable` before any account lookup.
- Never log an exception message, link, destination or payload.

## Gap analysis (plan 10 → current `main`)

| Plan-10 requirement | State on `main` | This plan |
|---|---|---|
| Durable encrypted queue, worker, leases, retries, wipe on completion | Done (`identity_mail`) for enrollment/claim/email-change/invitations | Reuse |
| Reset challenge + mail commit together | **Missing** — reset is sent synchronously after commit; SMTP failure loses the link | Task 2 |
| Superseded/expired reset never sent | **Missing** for resets | Task 2 |
| Production rejects disabled mail; TLS required; HTTPS origin | **Missing** — disabled passes validation; `UseStartTls=false` maps to `Auto` (may downgrade to plaintext) | Task 1 |
| Reset origin from server config, not caller | Partly — controller passes `App:BaseUrl`; duplicated with outbox | Task 2 (single source: outbox) |
| Password-changed security notice | **Missing** (only email-change has one) | Task 3 |
| Readiness signal | **Missing** | Task 4 |
| Metrics through plan 46 / deployment rehearsal | Out of scope (46 not built; rehearsal is an operator step) | Noted in PR |

## Review Focus

1. SMTP outage after commit: reset row and mail row exist; worker retries the same link; no new proof minted — `ResetRequest_QueuesMailInSameTransaction`, worker test `PasswordResetMail_SupersededToken_IsNotSent`.
2. Second reset request before first is delivered: first mail completes without sending — `PasswordResetMail_SupersededToken_IsNotSent`.
3. Production config with `Email:Enabled=false` or plaintext SMTP or `http://` base URL: startup fails — `Production_DisabledMail_FailsValidation`, `Production_HttpBaseUrl_FailsValidation`.
4. Unknown vs known account: identical response and no mail row for unknown — existing `UnknownAndKnownResetRequest…` integration test plus `ResetRequest_UnknownAccount_QueuesNothing`.
5. Missing outbox key: request answers 503 and nothing is queued — `HandleAsync_DeliveryDisabled_DoesNotLookUpAccount` (now driven by `IIdentityMailOutbox.Enabled`).

---

### Task 1: Environment-aware mail validation and strict TLS

**Files:**
- Modify: `src/backend/Intelibill.Infrastructure/Options/EmailOptionsValidator.cs`
- Modify: `src/backend/Intelibill.Infrastructure/Services/Auth/SmtpEmailService.cs`
- Test: `tests/backend/unit/Intelibill.Api.Unit.Tests/Options/EmailOptionsTests.cs`

**Interfaces:**
- Produces: `EmailOptionsValidator(IHostEnvironment? environment = null, IConfiguration? configuration = null)`.

- [ ] Tests: `Production_DisabledMail_FailsValidation` (env Production, `Enabled=false` → failure mentions `Email:Enabled`); `Development_DisabledMail_IsAllowed`; `Production_HttpBaseUrl_FailsValidation` (`App:BaseUrl=http://x` → mentions `App:BaseUrl`); `Production_MissingOutboxKey_FailsValidation` (mentions `Identity:OutboxKey`); failures never contain the password value.
- [ ] Implement: outside Development/Testing, disabled mail fails; when enabled outside Development/Testing require `App:BaseUrl` absolute `https` and `Identity:OutboxKey` decoding to 32 bytes. Existing null-environment construction keeps current behaviour.
- [ ] `SmtpEmailService`: non-StartTLS maps to `SecureSocketOptions.SslOnConnect`, never `Auto`. Test `SendIdentityAsync_WithoutStartTls_RequiresImplicitTls`.

### Task 2: Queue reset mail in the reset transaction

**Files:**
- Modify: `src/backend/Intelibill.Domain/Entities/IdentityMail.cs`, `src/backend/Intelibill.Infrastructure/Data/Configurations/EmailOwnershipChallengeConfiguration.cs`
- Create: migration `QueuePasswordResetMail` (nullable `password_reset_token_id uuid` + index)
- Modify: `IIdentityMailOutbox`, `IdentityMailOutbox`, `IdentityMailWorker`, `RequestPasswordResetCommand(Handler)`, `AuthController`, `IEmailService`, `SmtpEmailService`, `NoOpEmailService`
- Test: `RequestPasswordResetCommandHandlerTests`, `SmtpEmailServiceTests` (reset composition moves to outbox template test), integration `SecurityMailDeliveryTests.cs` (new), reset-link captures in `SessionLifecycleIntegrationTests`, `RefreshRotationConcurrencyTests`, `EmailOwnershipSecurityTests` switch to `IdentityTestMailbox.ReadPasswordResetAsync`.

**Interfaces:**
- Produces: `IdentityMail.CreateForPasswordReset(Guid passwordResetTokenId, string destination, string encryptedContent, DateTimeOffset expiresAt)`; `IIdentityMailOutbox.QueuePasswordReset(Guid tokenId, string destination, string token, DateTimeOffset expiresAt)`; `RequestPasswordResetCommand(string Email)`; `IdentityTestMailbox.ReadPasswordResetAsync(factory, email): Task<string token>`.
- Removes: `IEmailService.SendPasswordResetAsync` (all mail now goes through `SendIdentityAsync`).

- [ ] Unit tests: handler returns 503 error when `outbox.Enabled` is false without lookup; known account → `QueuePasswordReset` called **before** `CommitAsync` with 15-min expiry; unknown → nothing queued; commit failure → still generic success.
- [ ] Integration (`SecurityMailDeliveryTests`): `ResetRequest_QueuesMailInSameTransaction` (one `identity_mail` row with `PasswordResetTokenId`, content not plaintext, raw token absent from DB); `PasswordResetMail_SupersededToken_IsNotSent` (two requests, run `DeliverOneAsync` twice, recording sender receives only the second link); `PasswordResetMail_SmtpFailure_RetriesSameLink` (fail once → attempts=1, row pending, payload intact; succeed → same token delivered and payload wiped).
- [ ] Implement: reset link `{App:BaseUrl}/reset-password?token=…&email=…` built in the outbox; body keeps the IST expiry wording; worker treats `PasswordResetTokenId` as superseded when the token is missing or `!IsValid`.
- [ ] Generate migration; update model snapshot.

### Task 3: Password-changed security notice

**Files:** `IIdentityMailOutbox`, `IdentityMailOutbox`, `ResetPasswordCommandHandler`, `ChangeMyPasswordCommandHandler` and their unit tests.

- [ ] Produces `IIdentityMailOutbox.QueuePasswordChangedNotice(string destination)` (non-bearer: "Your Intelibill password was changed … all devices signed out … contact support").
- [ ] Both handlers queue it inside their transaction for a verified email only when `outbox.Enabled`; tests assert queued for verified email and not queued when disabled or no verified email.

### Task 4: Mail readiness check

**Files:** Create `src/backend/Intelibill.Api/HealthChecks/IdentityMailReadinessCheck.cs`; modify `EdgeExtensions.cs`; test in `tests/backend/unit/Intelibill.Api.Unit.Tests/HealthChecks/`.

- [ ] `Unhealthy` when `IIdentityMailOutbox.Enabled` is false outside Development; otherwise `Healthy`. Registered with the readiness tag. Queue backlog is an alerting concern (plan 46), not readiness, so a provider outage does not drain replicas.

### Task 5: Verify

- [ ] `dotnet build src/backend/Intelibill.slnx`; unit test projects; `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests`.
- [ ] Local rehearsal: API with mailpit (`Email:LocalCapture`), Angular app in Brave → forgot password → mail arrives in mailpit → reset once → reuse rejected → old session refused.
- [ ] Update `docs/backend-architecture.md` identity-mail section and CLAUDE.md test snapshot; open PR.
