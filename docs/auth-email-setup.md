# Email ownership: manual setup

This feature needs a working mail transport before users can complete email signup. New email signup requests do not create a user, password, shop, or session. The mailbox owner chooses their password after opening the verification email.

## 1. Choose a development mail transport

### Resend: delivery to an actual inbox

1. Create a free [Resend account](https://resend.com/signup).
2. Create a sending API key. Store the key in local user secrets or the deployment secret store.
3. For solo testing, use `onboarding@resend.dev` as the sender and the email address associated with your Resend account as the recipient.
4. To send to other testers, add a domain you control in Resend, publish the DNS records it supplies, and wait for verification. Then use a sender address on that domain.

Configure the API with these settings; the SMTP password is the Resend API key:

| Configuration | Value |
|---|---|
| `Email__Enabled` | `true` |
| `Email__Host` | `smtp.resend.com` |
| `Email__Port` | `587` |
| `Email__UseStartTls` | `true` |
| `Email__Username` | `resend` |
| `Email__Password` | Your sending API key, supplied as a secret |
| `Email__FromEmail` | `onboarding@resend.dev` for solo testing, or your verified sender |
| `Email__FromName` | `Intelibill` |

Use `:` instead of `__` when setting keys through .NET user secrets. Do not put credentials in tracked settings files. Keep click tracking disabled for security mail.

MailKit verifies certificate revocation by default and macOS cannot complete that OCSP/CRL lookup, so the TLS handshake with a real provider fails there with `SslHandshakeException: An incomplete certificate revocation check occurred`. `MailKitSmtpClientFactory` therefore skips the check in Development and Testing only. Production and every other environment keep it, which is what `MailKitSmtpClientFactoryTests` pins. A local run that must exercise the production setting can relay through Mailpit instead of relaxing it.

Provider references, checked 22 October 2026: [SMTP settings](https://resend.com/docs/send-with-smtp), [test sender restrictions](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain), [domain verification](https://resend.com/docs/dashboard/domains/introduction). The free tier has provider quotas; use local capture for repeated automated tests.

### Mailpit: local capture

[Mailpit](https://mailpit.axllent.org/about/) captures email and displays it in a browser. It does not deliver messages to Gmail or other external inboxes.

For a local Docker installation:

```sh
docker run --rm --name intelibill-mailpit \
  -p 127.0.0.1:1025:1025 \
  -p 127.0.0.1:8025:8025 \
  axllent/mailpit
```

Open `http://localhost:8025` to inspect messages and follow verification links. Run the API in `Development` and configure:

| Configuration | Value |
|---|---|
| `Email__Enabled` | `true` |
| `Email__LocalCapture` | `true` |
| `Email__Host` | `localhost` |
| `Email__Port` | `1025` |
| `Email__UseStartTls` | `false` |
| `Email__Username`, `Email__Password` | Empty |
| `Email__FromEmail` | `noreply@intelibill.test` |
| `Email__FromName` | `Intelibill development` |

The local capture host is restricted. If the API runs in a container, use the explicitly supported `mailpit` service hostname rather than `localhost`. Production SMTP requires TLS and authentication.

## 2. Configure Azure delivery and encrypted mail storage

Both `.tofu/envs/dev` and `.tofu/envs/prod` accept `mail_delivery`. Their default is disabled; both Azure API workloads run `Production`, with `Email__LocalCapture=false`. Mailpit is for local Development/Testing only.

1. In the Azure portal, select the intended subscription and environment vault (`intelibill-dev-kv` or `intelibill-prod-kv`). Your operator identity needs the existing `Key Vault Secrets Officer` grant; the application already has read-only `Key Vault Secrets User` access.
2. Create the secret `resend-smtp-key`, entering the Resend sending API key as its value. Create a **different** secret, `identity-mail-aes-key`, containing a freshly generated base64 32-byte key. Generate it in a trusted local terminal with `openssl rand -base64 32`; transfer it directly to the vault and protected backup, then clear temporary clipboard/terminal material. Do not paste either value into tfvars, source, tickets, or deployment logs.
3. Copy that environment's `terraform.tfvars.example` to its ignored `terraform.tfvars` if needed, preserving its existing subscription/operator settings. Add this block, replacing the sender with your verified sender (or `onboarding@resend.dev` only for your own test inbox):

```hcl
mail_delivery = {
  enabled                = true
  host                   = "smtp.resend.com"
  port                   = 587
  use_start_tls          = true
  username               = "resend"
  from_email             = "security@example.com"
  from_name              = "Intelibill"
  password_secret_name   = "resend-smtp-key"
  outbox_key_secret_name = "identity-mail-aes-key"
}
```

4. Use the normal reviewed infrastructure deployment process to install these references before the application cutover below. No infrastructure source edit is needed. Names are single Key Vault name segments (1–127 letters/digits/hyphens), never URLs, paths, versions, or values. Container Apps resolves `Email__Password` and `Identity__OutboxKey` from versionless Key Vault URIs under the application managed identity. OpenTofu does not read their values into state or outputs. The reference URI and names are deployment metadata.
5. Verify the new API revision can resolve both secrets and has `Email__Enabled=true`, `Email__UseStartTls=true`, and `Email__LocalCapture=false` without displaying secret values. Invalid SMTP options can prevent startup; missing/invalid outbox key causes verification requests to fail with 503. Confirm inbox receipt using the smoke checklist before opening signup.

The infrastructure requires authenticated STARTTLS for enabled delivery. It does not provide an implicit-TLS or plaintext deployment mode. For a temporary sending pause, set `enabled=false` while retaining both secret names and the vault values: the worker stops processing mail, and pending mail still expires. Retaining the key is essential for later inspection/recovery.

- Set `App__BaseUrl` to the frontend origin used by the recipient, including its scheme and port. A local frontend and a deployed frontend need different values. Azure infrastructure already derives this from the web ingress FQDN; verify that it is the intended recipient-visible HTTPS origin.
- Set `Identity__OutboxKey` to a securely generated, base64-encoded 32-byte key. Generate one with `openssl rand -base64 32`, then place it directly in secret storage.
- Keep the same outbox key across restarts and all API replicas that share the database. Retain it with the encrypted pending mail during backups and recovery.
- Do not rotate the outbox key while pending messages still require it. Drain or expire that work before a coordinated rotation. This feature's key setting is not a complete managed key-rotation system.
- Disabled or invalid mail configuration must not be treated as successful delivery. A request being accepted into the queue also does not establish inbox receipt.

Verification links expire after 30 minutes; recent-authentication proofs expire after five minutes and work once. The initial shared destination budget is one send per minute and five per hour. A throttled or ineligible request retains the generic accepted response. The worker polls every five seconds, uses a 30-second SMTP timeout, and attempts delivery at most five times within the mail expiry window.

Verification tokens are stored as hashes. The mail queue needs the actual link to deliver it, so its payload is encrypted. Never include real tokens, full verification links, or mail payloads in screenshots, PRs, logs, or deployment evidence.

For rotation, first close identity requests and let pending delivery finish or expire; security notices may remain eligible for one day. Retain the old key with its database backups. After confirming that no live queued ciphertext needs it, replace the vault value and coordinate a new API revision/restart across every replica before reopening writes. Versionless vault references can refresh independently, so do not update the key during active processing. This release has one active encryption key and no old-key fallback. SMTP API key rotation is separate: publish the replacement credential, restart/verify delivery, then revoke the prior credential in Resend.

## 3. Apply the migration before the new release

Back up the intended database and confirm the target environment first. Schedule the identity cutover and temporarily close public authentication/identity writes before migration. Do not let the old API continue creating identities after the migration resets historical verification evidence: it still has the old verification/linking semantics.

Deployed environments use the existing migration job, which must succeed before the new API/frontend revisions serve traffic. API startup does not apply migrations. Deploy the new API and matching frontend, retire the old API revision, verify configuration, then reopen authentication traffic. The existing migration-before-deployment pipeline alone does not provide this maintenance gate.

For an explicitly selected local development database:

```sh
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

Supply the mail settings, link origin, and shared outbox key before enabling the new enrollment flow. Deploy the matching API and frontend contracts together.

## 4. Handle the session and legacy-account cutover

The migration revokes existing refresh tokens, invalidates old access tokens, and resets historical email-verification flags that were not supported by ownership evidence. Every pre-cutover account is conservatively marked for operator review because absence of deleted business history cannot be established from the current records. **Existing email/password users need an ownership-recovery plan before applying this migration.** The same migration drops `user_external_logins`: external identity providers are gone, so an account whose only credential was a provider link can no longer sign in and must be recovered through the operator procedure below.

Ownership/security changes invalidate prior access and refresh credentials.

Do not bulk set `IsEmailVerified=true` because an address exists. An email match is insufficient evidence of account ownership.

Explicit self-service claims are limited to eligible unverified accounts created after the cutover without business history. Pre-cutover accounts and accounts that have held shop/business access require operator-assisted ownership review. Keep their shop and financial records intact. A support-required response must not be bypassed by changing database flags.

Establish verified email through Account security **before creating or joining a shop**. Account security is available without a shop. Once an unverified account has business history, adding or changing its recovery email requires operator review.

Before public rollout, inventory these accounts using counts and opaque user IDs. With an authorized read-only reporting connection to the intended database, run the following **after migration**, in a read-only transaction. The actual schema uses `users.requires_operator_recovery` and `users.is_email_verified`:

```sql
BEGIN READ ONLY;
SELECT count(*) AS total_accounts,
       count(*) FILTER (WHERE requires_operator_recovery) AS operator_review_accounts,
       count(*) FILTER (WHERE email IS NOT NULL AND NOT is_email_verified)
         AS unverified_email_accounts
FROM public.users;

SELECT id AS user_id, requires_operator_recovery, is_email_verified
FROM public.users
WHERE requires_operator_recovery
ORDER BY id;
COMMIT;
```

These queries expose no email, password hashes, tokens, or mail payloads. Keep even opaque IDs in restricted operational records. The recovery marker includes every pre-cutover user and subsequent accounts with membership access; it cannot distinguish deleted historical business access. Do not infer self-service eligibility from the absence of current memberships.

Resolve ownership with the merchant and a reviewed recovery procedure that removes untrusted credentials and invalidates sessions atomically. **No privileged operator recovery console or turnkey recovery command is implemented by this change.** Arrange and review that procedure before migrating existing email/password users; otherwise keep their rollout blocked. Mailbox access alone must not silently transfer an existing business account, and flipping recovery/verification flags is not a recovery procedure.

Employee invitation/acceptance, complete per-device session management, general authentication budgets, and the broader recovery-delivery work remain separate readiness projects. Do not treat this feature as completing those projects.

The old owner-created-password staff onboarding path now returns HTTP 409 `Auth.InvitationRequired`. It cannot create unverified login identities that reserve another person's email. Plan the verified staff invitation work before relying on that onboarding path for a public launch. This release does not complete the broader session/authentication, membership/invitation, or password/account recovery readiness plans (including plans 03, 08, and 10).

## 5. Perform a delivery and security smoke test

Use a dedicated test mailbox and disposable development data:

1. Request email signup. Confirm the UI shows a check-inbox message and has no authenticated session; protected shop creation must fail before confirmation.
2. Observe the email in the actual inbox or Mailpit. Choose a fresh password on the confirmation page, finish registration, and sign in.
3. Try the same link again and an expired link; both must fail. After the resend cooldown, request another link and confirm the earlier link is no longer usable.
4. Try an existing address and confirm the request response does not reveal account existence. Check destination limits also apply when email case or whitespace changes.
5. Confirm the removed provider endpoints are gone: `POST /api/auth/login/external`, `login/external/init`, `login/external/callback` and the `GET /auth/{google,facebook}/callback` relays all answer 404, and the sign-in page offers no provider button.
6. With a verified account and no shop, open Account security from shop setup, verify email, then return to shop setup. Confirm mandatory onboarding resumes when leaving Account security; authentication must still be required.
7. Request an email change. Confirmation requires a session for the same existing account as well as the mailbox token; on a different device, sign in with the original email first. Confirm anonymous and different-account attempts fail without consuming the proof. The old address remains active until confirmation, notifications reach the appropriate addresses, and old access/refresh credentials fail afterward.
8. Exercise a newly eligible unverified account claim and the pre-cutover/business-account support-required outcome. Confirm business data remains intact. Existing email/password users must use the separately reviewed operator recovery procedure.
9. With queued mail present, restart the API using the same outbox key. Confirm delivery can continue. Simulate a provider outage and verify retries do not create additional usable proofs.

Record timestamps, safe message IDs, and pass/fail outcomes. Actual inbox receipt is a manual external check; SMTP acceptance alone is insufficient evidence.

## 6. Release gates and rollback

Run normal backend build/tests and frontend tests/build without hiding dependency-audit failures. Feature tests executed with diagnostic overrides do not establish a clean release gate. Fix any outstanding vulnerable dependencies before public release.

Rollback must stop new enrollment and preserve challenge, audit, encrypted mail, and key material needed for inspection or recovery. Do not restore an older API that automatically links accounts by matching email. Keep schema compatibility and token cutover behavior in the rollback decision.

No live Resend account/domain setup, Azure secret resolution, infrastructure apply, production migration/cutover, or actual inbox delivery is established by automated tests. Complete and record these external checks manually; a passing mocked transport test does not validate delivery or a safe production rollout.
