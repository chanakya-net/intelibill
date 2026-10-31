# Phone authentication lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the public phone signup that cannot restore access, then deliver verified phone enrollment, new-device login and a safe lost-session recovery path behind a default-off flag.

**Architecture:** `POST /api/auth/register/phone` currently issues a session on an unproved number, so it is gated first and independently. Verified phone authentication then runs on purpose-bound, short-lived challenges that mirror the existing email-ownership feature exactly: a `PhoneChallenge` row holds a keyed HMAC of a six-digit code, every transition happens inside the one identity advisory lock the email flow already uses, and the code travels only in an encrypted outbox row that a background worker hands to an SMS transport. Phone-number changes stay separate from account recovery: a number that was recycled by a carrier can never claim a legacy account that carries business history.

**Tech Stack:** ASP.NET Core 10, C# latest, ErrorOr, FluentValidation, EF Core 10 / Npgsql / PostgreSQL, xUnit + NSubstitute, Testcontainers; Angular 21 standalone components, NgRx, PrimeNG, Bun.

**Spec:** [Market-readiness review](../../reviews/2026-10-21-market-readiness.md) item 5 and [API security evidence](../../reviews/2026-10-21-market-readiness-evidence/api-security.md) § 5.

## Global Constraints

- Web and API only. Mobile (`src/mobile/android/intelibill_mobile`) is out of scope.
- Preserve the layer order Domain ← Application ← Infrastructure ← Api, repository abstractions and `IUnitOfWork`.
- Versionless `PackageReference` entries; versions only in `Directory.Packages.props`. This plan adds **no** NuGet package: the SMS transport is a form-encoded `HttpClient` POST.
- Warnings are errors; the build must stay analyzer-clean.
- Angular standalone components; Bun only (no npm/yarn).
- Migrations are applied by the migration job, not by API startup. Run `dotnet ef database update` locally after pulling.
- Never log an OTP code, the HMAC key, or a full destination number. Masked destinations only (`+91 ••••• ••210`).
- Existing phone-only accounts keep their data. Nothing in this plan deletes a user or signs one out before a recovery route exists.
- Policy values below are the ones this plan implements; they are recommendations the product owner may revise before enablement.

## Decision: SMS delivery for testing

**The answer to "is there a free service": yes for testing, no for production in India.**

Three free tiers, layered. Checked 1 November 2026.

| Tier | What it is | Cost | Delivers a real SMS? | Used for |
|---|---|---|---|---|
| `PhoneAuth:LocalCapture=true` | The code is written to the encrypted `identity_sms` outbox row and never sent. Tests read it out of the database exactly as `IdentityTestMailbox` already reads email tokens. | Free, forever | No | Every automated test, and all local development |
| Twilio **test credentials** + magic numbers | A separate credential pair that exercises the REST API without charging the account or reaching a real handset. `+15005550006` passes validation; `+15005550001`, `+15005550002`, `+15005550003`, `+15005550004`, `+15005550009` return specific error codes. | Free | No | Transport-adapter tests: retries, error mapping |
| Twilio **trial account** | Free trial credit, no card required to start. Sends only to verified recipient numbers (up to 5; the sign-up number is auto-verified), and the body is prefixed "Sent from your Twilio trial account". Trial accounts verify recipients by SMS only, and expire 30 days after sign-up. | Free trial credit | **Yes** | The one-time real-delivery rehearsal in Task 7 |

**India specifics that make the trial workable.** Twilio reaches India over two gateways. The **international** gateway requires **no DLT (TRAI) registration** — which is why a free trial can deliver an OTP to an Indian handset today. In exchange, any sender ID is replaced with a random 5-to-9-digit short code, it bypasses the Indian DND registry, and shortened URLs are disallowed. The **domestic** gateway requires completed DLT entity, header/sender-ID and template registration on the operator portal before Twilio will whitelist the sender, and the Entity ID and Template ID must then be supplied on every submission.

So `PhoneAuthOptions` carries `DltEntityId` and `DltTemplateId` as optional values: empty means the international route (fine for testing), populated means the domestic route (required for production volume).

**Fallback if the trial's verified-recipient cap gets in the way:** an Android SMS gateway such as [textbee](https://textbee.dev/) turns a phone you already own into the transport over your own SIM — its free plan advertises 50 messages a day and 300 a month, and because the traffic is person-to-person it needs no DLT. It speaks a plain HTTP API, so it plugs into the same `ISmsSender` seam. Treat the advertised limits as a vendor claim.

**What is not free.** Production OTP traffic to Indian numbers over the domestic route needs DLT registration (entity, header, template) and per-message spend. Indian vendors advertise signup credits — Fast2SMS ₹50, Authkey's startup plan ₹2,500/10k OTPs on approval, trial packs from MSG91 and 2Factor — but those are trial credit, not a free tier, and none of them exempts you from DLT. Vendor selection, the message-cost ceiling and supported countries stay open product decisions; this plan commits only to the `ISmsSender` seam plus one Twilio implementation.

Sources: [Twilio test credentials](https://www.twilio.com/docs/iam/test-credentials), [Twilio trial account](https://www.twilio.com/docs/usage/tutorials/how-to-use-your-free-trial-account), [Twilio India SMS guidelines](https://www.twilio.com/en-us/guidelines/in/sms), [Twilio alphanumeric sender ID registration in India](https://support.twilio.com/hc/en-us/articles/21162166457755-Documents-Required-and-Instructions-to-Register-Your-Alphanumeric-Sender-ID-in-India), [textbee](https://textbee.dev/), [Fast2SMS](https://www.fast2sms.com/), [Authkey startup plan](https://authkey.io/startup-india).

## File Structure

**Create**

| File | Responsibility |
|---|---|
| `src/backend/Intelibill.Domain/Entities/PhoneChallenge.cs` | The challenge row: purpose, destination, code HMAC, key id, attempts, expiry, resend deadline, consumption |
| `src/backend/Intelibill.Domain/Entities/IdentitySms.cs` | One encrypted outbound message with a durable bounded retry lease |
| `src/backend/Intelibill.Domain/ValueObjects/PhoneNumberFormat.cs` | E.164 normalization that refuses to guess at an ambiguous number |
| `src/backend/Intelibill.Domain/Interfaces/Repositories/IPhoneChallengeRepository.cs` | Challenge persistence under the identity advisory lock |
| `src/backend/Intelibill.Application/Common/Interfaces/IPhoneOwnershipService.cs` | The application-facing phone lifecycle, plus `PhoneChallengeReceipt` |
| `src/backend/Intelibill.Application/Common/Interfaces/IIdentitySmsOutbox.cs` | Queue a code or a security notice; `Enabled` gate |
| `src/backend/Intelibill.Application/Common/Interfaces/ISmsSender.cs` | The transport seam |
| `src/backend/Intelibill.Application/Common/Errors/Errors.Phone.cs` | Stable phone error codes |
| `src/backend/Intelibill.Application/Features/Auth/Services/PhoneOwnershipService.cs` | The lifecycle itself, mirroring `EmailOwnershipService` |
| `src/backend/Intelibill.Infrastructure/Options/PhoneAuthOptions.cs` | `PhoneAuth` configuration section |
| `src/backend/Intelibill.Infrastructure/Options/PhoneAuthOptionsValidator.cs` | Refuses a half-configured provider at startup |
| `src/backend/Intelibill.Infrastructure/Repositories/PhoneChallengeRepository.cs` | The repository implementation |
| `src/backend/Intelibill.Infrastructure/Data/Configurations/PhoneChallengeConfiguration.cs` | `phone_challenges` and `identity_sms` mappings |
| `src/backend/Intelibill.Infrastructure/Services/Auth/IdentitySmsOutbox.cs` | AES-GCM encryption of the message body; keyed-HMAC helper |
| `src/backend/Intelibill.Infrastructure/Services/Auth/IdentitySmsWorker.cs` | Background delivery with a bounded retry lease |
| `src/backend/Intelibill.Infrastructure/Services/Auth/TwilioSmsSender.cs` | `HttpClient` POST to the Twilio Messages API |
| `src/backend/Intelibill.Infrastructure/Services/Auth/LocalCaptureSmsSender.cs` | Development/test sink that delivers nothing |
| `src/backend/Intelibill.Api/Controllers/PhoneOwnershipController.cs` | `api/auth/phone/*` endpoints |
| `src/frontend/src/app/features/auth/pages/phone-verification-page.component.{ts,html,scss,spec.ts}` | Request, resend, confirm |
| `docs/auth-phone-setup.md` | Operator setup, mirroring `docs/auth-email-setup.md` |
| `tests/backend/integration/Intelibill.Integration.Tests/PhoneLifecycleIntegrationTests.cs` | The full journey |
| `tests/backend/integration/Intelibill.Integration.Tests/IdentityTestPhone.cs` | Reads a code out of the outbox, as `IdentityTestMailbox` does for email |

**Modify**

| File | Change |
|---|---|
| `src/backend/Intelibill.Domain/Entities/User.cs` | `IsPhoneVerified`, `PhoneVerifiedAt`, `VerifyPhone`, `ClearVerifiedPhone`; `ClaimEmail` clears the verified phone |
| `src/backend/Intelibill.Infrastructure/Data/Configurations/UserConfiguration.cs` | Map the two new columns |
| `src/backend/Intelibill.Application/Features/Auth/DTOs/AuthenticationProof.cs` | `CredentialKind.PhoneOwnership`, `FromPhoneOwnership` |
| `src/backend/Intelibill.Api/Controllers/AuthController.cs` | `register/phone` refuses while phone auth is off |
| `src/backend/Intelibill.Application/Features/Auth/Commands/RegisterWithPhone/RegisterWithPhoneCommandHandler.cs` | Gate, normalize, mark verified only on proof |
| `src/backend/Intelibill.Application/Features/Users/Commands/UpdateMyProfile/UpdateMyProfileCommandHandler.cs` | A profile edit cannot move a verified phone |
| `src/backend/Intelibill.Infrastructure/DependencyInjection.cs` | Register options, repository, service, outbox, worker, transport |
| `src/frontend/src/app/core/auth/auth.service.ts`, `auth.models.ts` | Phone challenge calls and models |
| `src/frontend/src/app/features/auth/pages/login-page.component.{ts,html}` | A phone entry point, shown only when the server says phone auth is on |
| `src/frontend/src/app/app.routes.ts` | The `auth/phone` route |
| `src/frontend/public/assets/i18n/*.json` | Nine locale files |

## Review Focus

The spec's own five acceptance scenarios are named tests in Tasks 2, 3 and 5. These five are implied by the spec but no scenario in it exercises them, most likely to bite first:

1. **A claimed account keeps a verified-phone flag for a number it no longer holds.** `User.ClaimEmail` sets `PhoneNumber = null` today; adding `IsPhoneVerified` without clearing it there leaves a true flag over a null number, and the next phone login would match nothing while the account still claims a verified factor. → Task 5, `ClaimEmail_ClearsVerifiedPhone`.
2. **A bare ten-digit Indian number silently becomes a different subscriber.** "India-first UI may default +91 but must not silently rewrite an ambiguous number": `98765 43210` is an Indian mobile, `0987654321` and `1234567890` are not, and prefixing `+91` to all three sends an OTP to a stranger. → Task 3, `Enroll_AmbiguousNumber_IsRejectedNotRewritten`.
3. **`PUT /api/users/me` still writes `PhoneNumber` with no proof.** A profile edit that can set the phone number makes the verified-phone column meaningless as a credential. → Task 5, `UpdateMyProfile_CannotChangeVerifiedPhone`.
4. **The off switch closes only the old endpoint.** Gating `register/phone` while leaving the new challenge endpoints open would ship a worse hole than the one being closed. → Task 1, `PhoneAuthDisabled_ChallengeEndpointsAlsoRefuse`.
5. **The code leaks through a response, a log line or an unmasked destination.** The challenge receipt is returned to an unauthenticated caller, so it must carry a masked destination and no code, and the delivery worker must not log the body on failure. → Task 4, `Receipt_MasksDestinationAndOmitsCode` and `SmsDeliveryFailure_LogsNoCodeOrDestination`.

---

### Task 1: Close public phone enrollment behind a default-off flag

Deployable on its own: it removes the unrecoverable signup without waiting for the rest.

**Files:**
- Create: `src/backend/Intelibill.Infrastructure/Options/PhoneAuthOptions.cs`
- Create: `src/backend/Intelibill.Application/Common/Errors/Errors.Phone.cs`
- Modify: `src/backend/Intelibill.Application/Features/Auth/Commands/RegisterWithPhone/RegisterWithPhoneCommandHandler.cs`
- Modify: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs`
- Test: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Auth/Commands/RegisterWithPhoneCommandHandlerTests.cs`
- Test: `tests/backend/integration/Intelibill.Integration.Tests/PhoneLifecycleIntegrationTests.cs`

**Interfaces:**
- Produces: `PhoneAuthOptions` with `const string SectionName = "PhoneAuth"` and `bool Enabled { get; init; }` (default `false`); `Errors.Phone.EnrollmentUnavailable` → `Error.Failure("Auth.PhoneEnrollmentUnavailable", ...)`, surfaced as HTTP 503. The handler takes `IPhoneAuthPolicy` (`bool Enabled { get; }`) so Application does not reference Infrastructure options.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task HandleAsync_PhoneAuthenticationDisabled_DoesNotCreateUserOrTokens()
// policy.Enabled => false
// Assert.True(result.IsError);
// Assert.Equal("Auth.PhoneEnrollmentUnavailable", result.FirstError.Code);
// await _userRepository.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
// await _sessions.DidNotReceive().IssueForProvedAccountAsync(Arg.Any<User>(), Arg.Any<AuthenticationProof>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
```

```csharp
[Fact] public async Task PhoneAuthDisabled_ChallengeEndpointsAlsoRefuse()
// POST /api/auth/register/phone, /api/auth/phone/challenges and /api/auth/phone/confirm
// each return 503 with title "Auth.PhoneEnrollmentUnavailable", and the users table is unchanged.
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter RegisterWithPhone`
Expected: FAIL — `IPhoneAuthPolicy` does not exist.

- [ ] **Step 3: Implement the gate**

`PhoneAuthOptions.Enabled` defaults to `false`. `PhoneAuthPolicy(IOptions<PhoneAuthOptions>) : IPhoneAuthPolicy` in Infrastructure. The handler returns `Errors.Phone.EnrollmentUnavailable` before it touches the repository or opens a transaction. `ErrorType.Failure` already maps to 503 in `EmailOwnershipController`; do the same in `AuthController` for this one code so the response is a stable problem document rather than a 500.

- [ ] **Step 4: Confirm green**

Run: `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter RegisterWithPhone`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/backend/Intelibill.Infrastructure/Options/PhoneAuthOptions.cs src/backend/Intelibill.Application src/backend/Intelibill.Api tests/backend
git commit -m "feat(auth): close public phone enrollment behind a default-off flag"
```

---

### Task 2: The challenge: schema, keyed HMAC, atomic attempts and consumption

**Files:**
- Create: `src/backend/Intelibill.Domain/Entities/PhoneChallenge.cs`
- Create: `src/backend/Intelibill.Domain/Interfaces/Repositories/IPhoneChallengeRepository.cs`
- Create: `src/backend/Intelibill.Infrastructure/Repositories/PhoneChallengeRepository.cs`
- Create: `src/backend/Intelibill.Infrastructure/Data/Configurations/PhoneChallengeConfiguration.cs`
- Modify: `src/backend/Intelibill.Domain/Entities/User.cs`, `UserConfiguration.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/PhoneChallengeTests.cs`

**Interfaces:**
- Produces:
  - `PhoneChallenge.Create(string purpose, string destination, string codeHash, string keyId, DateTimeOffset expiresAt, DateTimeOffset resendAllowedAt, Guid? userId = null, long version = 0)`
  - `bool RegisterFailedAttempt(int maxAttempts)` — increments and returns whether the challenge is now exhausted
  - `void Consume(DateTimeOffset now)`
  - `bool IsOpen(DateTimeOffset now, int maxAttempts)`
  - `IPhoneChallengeRepository`: `BeginAsync`, `FindForUpdateAsync(Guid, CancellationToken)`, `SupersedeAsync(destination, purpose, userId, now, ct)`, `HasSendBudgetAsync(destination, now, ct)`, `FindUserByVerifiedPhoneAsync(destination, ct)`, `HasBusinessHistoryAsync(userId, ct)`, `InvalidateCredentialsAsync(userId, ct)`, `Add(PhoneChallenge)`, `AddSms(IdentitySms)`, `CommitAsync`
  - `User.VerifyPhone(string e164, DateTimeOffset now)`, `User.ClearVerifiedPhone()`

Policy values: six digits, five-minute lifetime, five attempts, 60-second resend spacing.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void RegisterFailedAttempt_FifthWrongAttempt_ClosesChallenge()
[Fact] public void IsOpen_AfterExpiry_IsFalse()          // TimeProvider-driven, exact boundary: ExpiresAt is exclusive
[Fact] public void Consume_IsIdempotentlyRefusedTwice()  // IsOpen false once ConsumedAt is set
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter PhoneChallenge`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the entity**

Store the code as a keyed HMAC-SHA256, hex, with the key id beside it — a bare SHA-256 over a 10^6 code space is recoverable from a database dump in milliseconds, which is the whole reason this column is not a plain hash. The key is `Identity:PhoneCodeKey`, base64, 32 bytes, read from the configured secret store exactly as `Identity:OutboxKey` already is.

- [ ] **Step 4: Implement the repository and mappings**

`BeginAsync` takes the same `pg_advisory_xact_lock(7463218941)` the email flow uses, so phone and email identity transitions serialize against each other rather than racing on one account. `FindForUpdateAsync` reads the challenge `FOR UPDATE`, which is what makes two simultaneous confirmations of one code produce exactly one session. Tables `phone_challenges` (destination 16, purpose 32, code_hash 64, key_id 32) and `identity_sms`, indexed `(destination, created_at)`, `(user_id, purpose)` and `(completed_at, next_attempt_at)`.

- [ ] **Step 5: Add the user columns**

`is_phone_verified` NOT NULL DEFAULT false — the backfill value is *unknown*, which is false, never true. `phone_verified_at` nullable. `VerifyPhone` sets both and increments `SecurityVersion`, because gaining a sign-in factor is a security transition.

- [ ] **Step 6: Generate the migration**

```bash
dotnet ef migrations add AddVerifiedPhoneChallenges \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

- [ ] **Step 7: Confirm green and commit**

Run: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter PhoneChallenge`
Expected: PASS

```bash
git commit -am "feat(auth): add phone challenges with keyed code hashes and bounded attempts"
```

---

### Task 3: Verified enrollment and new-device login

**Files:**
- Create: `src/backend/Intelibill.Application/Common/Interfaces/IPhoneOwnershipService.cs`
- Create: `src/backend/Intelibill.Application/Features/Auth/Services/PhoneOwnershipService.cs`
- Create: `src/backend/Intelibill.Domain/ValueObjects/PhoneNumberFormat.cs`
- Create: `src/backend/Intelibill.Api/Controllers/PhoneOwnershipController.cs`
- Modify: `src/backend/Intelibill.Application/Features/Auth/DTOs/AuthenticationProof.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/ValueObjects/PhoneNumberFormatTests.cs`, `PhoneLifecycleIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2's repository and entity; `IAuthSessionService.IssueForProvedAccountAsync` (Task 2's transaction holds the lock, which is the precondition that method documents).
- Produces:
  - `PhonePurpose { Enrollment = 1, Login = 2, PhoneChange = 3 }`
  - `Task<ErrorOr<PhoneChallengeReceipt>> RequestAsync(string phone, PhonePurpose purpose, Guid? actor, long version, CancellationToken ct)`
  - `Task<ErrorOr<AuthResult>> ConfirmAsync(Guid challengeId, string code, PhonePurpose purpose, CancellationToken ct)`
  - `sealed record PhoneChallengeReceipt(Guid ChallengeId, string MaskedDestination, DateTimeOffset ResendAllowedAt, DateTimeOffset ExpiresAt)`
  - `AuthenticationProof.FromPhoneOwnership(Guid userId, long verifiedSecurityVersion, Guid challengeId, DateTimeOffset verifiedAt)` with `CredentialKind.PhoneOwnership = 4`
  - `PhoneNumberFormat.TryNormalize(string input, string? defaultCountryCode, out string e164)`

Routes, rate limits matching the email endpoints: `POST api/auth/phone/challenges` (5 per 15 min), `POST api/auth/phone/confirm` (10 per 15 min), `GET api/auth/phone/availability` (anonymous, returns `{ enabled }`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("+919876543210", "+919876543210")]
[InlineData("9876543210", "+919876543210")]   // a 10-digit Indian mobile starting 6-9, with DefaultCountryCode "+91"
[InlineData("+91 98765 43210", "+919876543210")]
public void TryNormalize_AcceptsUnambiguousInput(string input, string expected);

[Theory]
[InlineData("0987654321")]   // leading zero: a trunk prefix, not a subscriber number
[InlineData("1234567890")]   // 10 digits, not an Indian mobile range
[InlineData("98765")]
[InlineData("+9198765432100000")]
public void TryNormalize_RejectsAmbiguousInput_WithoutRewriting(string input);
```

```csharp
[Fact] public async Task Enroll_AmbiguousNumber_IsRejectedNotRewritten()
// POST phone/challenges with "0987654321" => 400 Auth.InvalidPhoneNumber, and no outbox row exists.

[Fact] public async Task PhoneSignup_Logout_NewDeviceLogin_SucceedsAfterNewProof()
// enroll with proof -> session; sign out; request a Login challenge on the same number;
// confirm with the fresh code on a second HttpClient => 200 with a usable access token,
// and the second session's id differs from the first.

[Fact] public async Task Otp_ParallelConfirm_CreatesOneSession()
// Barrier-synchronised: two confirms of one challenge; exactly one 200, exactly one
// non-success, and db.AuthSessions.Count(x => x.UserId == id) == 1.

[Fact] public async Task Otp_ExpiredOrFiveWrongAttempts_CannotAuthenticate()
[Fact] public async Task Otp_ResendInvalidatesPriorCode()
// The superseded code is refused; only the newest one authenticates.

[Fact] public async Task Enroll_ExistingGlobalIdentity_IsNotMergedByUnverifiedContactPhone()
// An account whose phone_number was set through a profile edit (is_phone_verified = false)
// does not become the subject of a phone enrollment: the enrollment is refused with
// Auth.RecoverySupportRequired and no session is issued.
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter PhoneNumberFormat`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement `PhoneNumberFormat.TryNormalize`**

Strip spaces, hyphens and parentheses. Input that already starts `+` is accepted when the remainder is 8–15 digits. Input with no `+` is accepted only when `defaultCountryCode` is `"+91"` and the input is exactly 10 digits whose first digit is 6–9; everything else is rejected. A leading `0` is never stripped and never prefixed — that is the ambiguity the spec forbids guessing at. No `libphonenumber` dependency: this rule is small, total and testable, and it fails closed.

- [ ] **Step 4: Implement `PhoneOwnershipService`**

Mirror `EmailOwnershipService` structure, including its generic-response discipline: `RequestAsync` returns a receipt with `ChallengeId = Guid.Empty` for an ineligible destination rather than disclosing whether an account exists, and the send budget is checked inside the lock. `ConfirmAsync` opens the lock, reads the challenge `FOR UPDATE`, compares the HMAC with `CryptographicOperations.FixedTimeEquals`, increments attempts on a miss, consumes on a hit, then calls `IssueForProvedAccountAsync` with `FromPhoneOwnership` (enrollment of a brand-new account uses `FromRegistration`, as the email flow does) — all in the one transaction, which is what makes the parallel-confirm test pass.

- [ ] **Step 5: Implement the controller**

- [ ] **Step 6: Confirm green and commit**

Run: `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter PhoneLifecycleIntegrationTests`
Expected: PASS

```bash
git commit -am "feat(auth): verified phone enrollment and new-device login over OTP challenges"
```

---

### Task 4: SMS delivery — outbox, worker, transports, configuration validation

**Files:**
- Create: `IdentitySms.cs`, `IIdentitySmsOutbox.cs`, `ISmsSender.cs`, `IdentitySmsOutbox.cs`, `IdentitySmsWorker.cs`, `TwilioSmsSender.cs`, `LocalCaptureSmsSender.cs`, `PhoneAuthOptionsValidator.cs`, `IdentityTestPhone.cs`
- Modify: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs`
- Test: `tests/backend/unit/Intelibill.Api.Unit.Tests/...PhoneAuthOptionsValidatorTests.cs`, `PhoneLifecycleIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `IIdentitySmsOutbox`: `bool Enabled { get; }`, `void QueueCode(Guid challengeId, string destination, string purpose, string code, DateTimeOffset expiresAt)`, `void QueueSecurityNotice(string destination, string reason)`
  - `ISmsSender`: `Task SendIdentityAsync(string destination, string body, CancellationToken ct)`
  - `IdentitySms.Create(Guid? challengeId, string destination, string encryptedContent, DateTimeOffset expiresAt)`, `Complete(now)`, `Failed(now)`

`ISmsSender` deliberately takes a rendered body rather than the pasted plan's `SendCodeAsync(destination, code, templateId)`: the outbox already encrypts a rendered body for email, the DLT template id is per-deployment configuration rather than per-call data, and keeping the code out of the transport signature means no transport can log it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Receipt_MasksDestinationAndOmitsCode()
// The 202 body contains "+91 ••••• ••210" and no six-digit sequence anywhere,
// and the code is recoverable only by decrypting the identity_sms row.

[Fact] public async Task SmsDeliveryFailure_LogsNoCodeOrDestination()
// A sender that throws leaves attempts == 1 with a future next_attempt_at, and the captured
// log scope contains neither the code nor any digit run of 8 or more.

[Fact] public void Validate_ProviderEnabledWithoutCredentials_Fails()
[Fact] public void Validate_LocalCaptureOutsideDevelopmentOrTesting_Fails()
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter PhoneAuthOptions`
Expected: FAIL

- [ ] **Step 3: Implement the outbox and worker**

AES-GCM with `Identity:OutboxKey`, the destination as associated data — the same construction `IdentityMailOutbox` uses, so one key rotation covers both. The worker mirrors `IdentityMailWorker`: a five-second tick, a bounded lease taken and committed *before* any network call, no transaction held across I/O, a superseded or expired row completed without sending, and a single `LoggerMessage` on failure that names no payload.

- [ ] **Step 4: Implement the transports and options**

`PhoneAuthOptions`: `Enabled`, `LocalCapture`, `Provider`, `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `DltEntityId`, `DltTemplateId`, `CodeLength = 6`, `CodeLifetimeMinutes = 5`, `MaxAttempts = 5`, `ResendSpacingSeconds = 60`, `DestinationSendsPerHour = 5`, `DefaultCountryCode = "+91"`. The validator returns success when `Enabled` is false, refuses `LocalCapture` outside Development and Testing, and requires the provider credentials otherwise — the same shape as `EmailOptionsValidator`.

`TwilioSmsSender` posts form-encoded `To`/`From`/`Body` (plus the DLT parameters when configured) to `https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json` with basic auth over a named `HttpClient`, 30-second timeout, and maps a non-2xx status to an exception the worker's retry already handles. No new NuGet package.

- [ ] **Step 5: Confirm green and commit**

```bash
git commit -am "feat(auth): deliver phone codes through an encrypted outbox and a pluggable SMS transport"
```

---

### Task 5: Recovery rules, verified phone change, security notices

**Files:**
- Modify: `PhoneOwnershipService.cs`, `User.cs`, `UpdateMyProfileCommandHandler.cs`, `PhoneOwnershipController.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/UserTests.cs`, `PhoneLifecycleIntegrationTests.cs`, `tests/backend/unit/Intelibill.Application.Unit.Tests/.../UpdateMyProfileCommandHandlerTests.cs`

**Interfaces:**
- Consumes: Task 3's service; `IPhoneChallengeRepository.HasBusinessHistoryAsync` (`User.RequiresOperatorRecovery`, already set by `AddShopMembership`).
- Produces: `Task<ErrorOr<Guid>> RequestPhoneChangeAsync(Guid actor, long version, string phone, string reauthProof, CancellationToken ct)`, `Task<ErrorOr<bool>> ConfirmPhoneChangeAsync(Guid actor, long version, Guid challengeId, string code, CancellationToken ct)`; `User.ClearVerifiedPhone()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ClaimEmail_ClearsVerifiedPhone()
// A user with a verified phone, after ClaimEmail: PhoneNumber is null AND IsPhoneVerified is false
// AND PhoneVerifiedAt is null.

[Fact] public async Task RecycledPhone_CannotClaimLegacyShop()
// A legacy phone-only user holds a shop membership (RequiresOperatorRecovery true). A new party
// proves the same number: the result is Auth.RecoverySupportRequired, no session is issued for the
// legacy user id, and the legacy user's rows are untouched.

[Fact] public async Task ChangePhone_RequiresCurrentSessionAndNewNumberProof()
// Anonymous => 401. Authenticated without a reauth proof => Auth.ReauthenticationRequired.
// With a proof but the wrong code => the number does not move. Correct proof and code => it moves,
// sessions are invalidated, and a notice is queued to both the old and the new number.

[Fact] public async Task UpdateMyProfile_CannotChangeVerifiedPhone()
// PUT /api/users/me with a different phoneNumber on a verified-phone account leaves
// users.phone_number unchanged and returns Auth.PhoneChangeRequiresProof.
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter ClaimEmail`
Expected: FAIL

- [ ] **Step 3: Implement**

`ClaimEmail` calls `ClearVerifiedPhone`. `UpdateMyProfile` leaves `PhoneNumber` alone when `IsPhoneVerified` and the submitted value differs, returning `Auth.PhoneChangeRequiresProof`; the unverified contact-phone case keeps working as it does today. Enrollment and login refuse any number currently held by an account with `RequiresOperatorRecovery` that has not itself proved the number — shop ownership by someone else is never authority to replace global recovery fields, so the answer is an audited support route, not an automatic claim. Phone change requires the existing reauthentication proof plus a fresh challenge on the new number, then `InvalidateCredentialsAsync` and a notice to both numbers.

- [ ] **Step 4: Confirm green and commit**

```bash
git commit -am "feat(auth): protect legacy accounts from recycled numbers and require proof to change a verified phone"
```

---

### Task 6: Web UI

**Files:**
- Create: `phone-verification-page.component.{ts,html,scss,spec.ts}`
- Modify: `auth.service.ts`, `auth.models.ts`, `login-page.component.{ts,html}`, `app.routes.ts`, nine `src/frontend/public/assets/i18n/*.json`

**Interfaces:**
- Consumes: Task 3's three routes.
- Produces: `AuthService.requestPhoneChallenge(phone, purpose)`, `confirmPhoneChallenge(challengeId, code, purpose)`, `phoneAuthAvailability()`; models `PhoneChallengeReceipt`, `PhonePurpose`.

- [ ] **Step 1: Write the failing spec**

```ts
it('otpFailureDoesNotErasePendingWork', ...)   // a wrong code leaves the entered phone and the challenge id in place
it('resendWaitUsesServerDeadline', ...)        // the countdown is driven by receipt.resendAllowedAt, not a client-side timer start
it('showsUnavailableStateWhenPhoneAuthDisabled', ...)
it('masksTheDestinationItWasGiven', ...)
```

- [ ] **Step 2: Run it and watch it fail**

Run: `bun run test --run phone-verification-page.component.spec.ts` from `src/frontend`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement the page and the login-page entry point**

Standalone component, PrimeNG, NgRx-free local state. Show the masked destination the server returned, a resend countdown computed from `resendAllowedAt`, the code expiry, and the alternate verified recovery method when one exists. The login page shows the phone option only when `phoneAuthAvailability()` says enabled, and the page renders a plain unavailable state otherwise.

- [ ] **Step 4: Add translations to all nine locale files**

- [ ] **Step 5: Confirm green and commit**

```bash
cd src/frontend && bun run test --run phone-verification-page.component.spec.ts
git commit -am "feat(frontend): add the phone verification page"
```

---

### Task 7: Whole-journey verification and the enablement gate

**Files:** `docs/auth-phone-setup.md`; no production code.

- [ ] **Step 1: Run the backend suites**

```bash
dotnet build src/backend/Intelibill.slnx
dotnet test src/backend/Intelibill.slnx
```
Expected: no new failures against the 667-test snapshot; every test named in Tasks 1–5 passing by name.

- [ ] **Step 2: Run the frontend suite**

```bash
cd src/frontend && bun run test
```

- [ ] **Step 3: Write `docs/auth-phone-setup.md`**

The three delivery tiers from the decision section above, as a table of configuration keys, mirroring `docs/auth-email-setup.md`. Include the DLT note and the masked-logging rule.

- [ ] **Step 4: The enablement gate**

`PhoneAuth:Enabled` stays `false` on every deployed environment until: one controlled real-delivery rehearsal on a Twilio trial (signup → sign out → new device → sign in → lost-session recovery) has been completed and recorded; the provider, template and consent configuration has been reviewed; and plan 08's destination, IP and attempt budgets are in place. Partial OTP enrollment alone is not sufficient evidence for release. No vendor call is part of this plan.

- [ ] **Step 5: Commit**

```bash
git commit -am "docs(auth): document phone verification setup and the enablement gate"
```

## Migration, rollout and acceptance

- `AddVerifiedPhoneChallenges` backfills `is_phone_verified = false` — unknown, never true. Applied by the migration job; API startup does not apply it.
- Before Task 1 reaches an environment, record the counts of phone-only identities, how many hold live sessions, and how many own shops. Those accounts keep their data and their sessions; they gain an assisted route to a verified email ([auth-email-setup.md](../../auth-email-setup.md) § claim) and later a verified phone.
- Deploy the challenge schema and the budgets before any SMS is switched on.
- Rollback: set `PhoneAuth:Enabled=false`. Existing valid sessions continue, assisted recovery continues, and consumed-proof audit rows are retained.
