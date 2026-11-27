# Licensing administrator recovery (P01-12)

Provider administrators recover access through one of two audited paths. Neither path bypasses MFA:
both end every session of the account, retire its authenticator and leave only a 15-minute
**enrollment** credential. Business access returns only after a new authenticator is verified, and that
verification issues ten new recovery codes.

## Paths

| Path | Who starts it | Proof required | Effect on the account |
| --- | --- | --- | --- |
| Recovery code | The administrator, from **Sign in → Lost your authenticator? Recover access** (`POST /api/admin/auth/recovery/code`) | Email, password and one unused recovery code | Code consumed once; every session revoked; authenticator retired. Unused codes stay valid until the new authenticator is verified, so a lapsed enrollment can be restarted with another code. |
| Reviewed reset | A platform owner with MFA verified less than five minutes ago, from **Review administrator recovery** (`POST /api/admin/recovery/resets`) | Reason, verification method, ticket-style evidence reference and explicit confirmation | Every session revoked; authenticator retired; all unused recovery codes void; a one-use reset code valid for 24 hours is shown once to the reviewer. |
| Enrollment continuation | The administrator, via **Continue enrollment with a saved key** (`POST /api/admin/auth/recovery/continue`) | Email, password and the independently generated key saved before redeeming the final recovery code | If the response was lost or enrollment expired, replaces the 15-minute enrollment credential. The key stops working once fresh MFA is verified or a reviewed reset supersedes it. |
| Reset redemption | The administrator, same recovery page (`POST /api/admin/auth/recovery/reset`) | Email, password and the reset code | Reset consumed once; enrollment credential issued. |

Both redemptions require the account password, so neither a recovery code, a reset code nor the reviewer
alone can take over an account. A password alone never re-opens enrollment after a recovery: first-owner
**resume** is refused for any administrator with recovery history.

## Guarantees

- **One use.** Recovery codes are stored as keyed HMAC digests and reset codes as SHA-256 digests. Each
  redemption locks the administrator row (the sign-in, refresh and MFA lock order), so concurrent
  presentations of the same proof admit exactly one.
- **No business access before enrollment.** Recovery sets the account to MFA-pending. Access tokens and
  refresh credentials of earlier sessions return 401, sign-in with the old authenticator is refused, and
  the enrollment credential answers 403 on every business route.
- **Bounded requests.** Requests must carry the exact provider origin and the sign-in request token, are
  limited to 4 KiB and draw on the PostgreSQL sign-in budget (5 attempts per account per 15 minutes, 60
  globally per 5 minutes; reviews per reviewer). Evidence references match
  `[A-Za-z0-9][A-Za-z0-9._:#/-]{2,63}`, so they cannot carry an address or a narrative.
- **Last recoverable owner.** A reviewed reset refuses (409 `Admin.LastRecoverableOwner`) when its target is
  a platform owner and no other active, MFA-enrolled platform owner would remain. A reviewer cannot reset
  their own authenticator. Redeeming the final unused code requires a separate 256-bit continuation key.
  The browser generates it and asks the administrator to save it before submitting. The same database
  transaction consumes the final code and saves the key digest. Keep the key with the same care as a
  recovery code until enrollment finishes. The continuation path uses the recovery budget and records
  each renewal. It grants enrollment access for the same account and ends any previous enrollment
  credential.
- **Safe audit.** History records `recovery-code-redeemed`, `recovery-refused`, `recovery-reset-issued`,
  `recovery-reset-redeemed`, `recovery-reset-denied` and one `device-session-revoked` (reason
  `RecoveryReset`) per ended session, with administrator state before and after. Audit rows and logs never
  contain an email, password, recovery code, continuation key, reset code or evidence reference.
  Each continuation records `enrollment-resumed`. PostgreSQL preserves the review decision on the
  `admin_recoveries` row and permits an open grant to move once to redeemed or retired.

## Review requirements

A reviewed reset is a security decision requiring recovery-review capability and a live platform owner
session. The API checks the same persisted session again inside the reset transaction. Sign-out,
revocation, expiry or MFA age of five minutes refuses the request. Before submitting it, the reviewer must:

1. Verify the person out of band through a channel recorded before the incident (video call, in person or a
   call back to a number already on file). Email to the account address alone is not verification.
2. Open a ticket and use its reference as the evidence reference. Record the verification details in the
   ticket, not in InteliBill.
3. Hand the reset code to the verified person over that same channel. Never paste it into email, chat or the
   ticket. It expires in 24 hours and is useless without the person's own password.
4. Treat **suspected compromise** as an incident: after re-enrollment, review the account's device sessions
   and audit history.

Two-person approval is not required for this initial single-provider release; recent MFA, the reviewer's
capability, the explicit confirmation and immutable audit are mandatory.

## Key custody (launch input)

Production administrator identities, secret custody and the out-of-band recovery owner are human-provided
launch inputs and are **not** established by this change. Before production access:

- The `AdminMfa` key ring (TOTP encryption and recovery-code HMAC keys) lives in the secrets manager, never
  in configuration files. Keep prior keys available for decryption and recovery-code HMAC after changing
  `ActiveKeyId`. Pending setup uses the active key for the new authenticator while keeping the previous
  code key association. Successful MFA verification retires those codes and issues new codes under the
  new authenticator key. Remove a prior key only after administrators no longer depend on it.
- Name at least two platform owners once capability delegation (P01-13) is available, so a reviewed reset is
  possible for any owner. Until then the sole bootstrapped owner recovers only with their own codes.
- If every platform owner loses both authenticator and codes, recovery requires the documented out-of-band
  operator procedure (database-level action under change control with audit), never a UI or unaudited edit.

## Rollout

1. Apply `20261115000319_AddAdminRecovery` and `20261115002745_ProtectAdminRecovery` through the explicit
   licensing migration job. Existing enrolled identities, credential digests, sessions and audit
   history retain their values.
2. Route admin traffic to the recovery-aware licensing API and drain all predecessor replicas
   before accepting recovery requests. Use recovery-aware API builds for rollback once recovery
   writes have been enabled. PostgreSQL also rejects ordinary enrollment credentials for recovered
   accounts, and retired authenticator ciphertext is erased when recovery commits.
3. Both migrations refuse Down after recovery evidence exists (SQLSTATE 55000). Use the existing
   schema and recover forward. Reproduce the local cutover, rollback and browser paths with
   `bash tests/backend/licensing/acceptance/run-p0112-browser.sh`.
