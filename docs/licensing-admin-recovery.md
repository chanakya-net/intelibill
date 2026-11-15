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
  their own authenticator; they use their recovery codes, which keep the owner role and stay usable until
  re-enrollment completes.
- **Safe audit.** History records `recovery-code-redeemed`, `recovery-refused`, `recovery-reset-issued`,
  `recovery-reset-redeemed`, `recovery-reset-denied` and one `device-session-revoked` (reason
  `RecoveryReset`) per ended session, with administrator state before and after. Audit rows and logs never
  contain an email, password, recovery code, reset code or evidence reference. The reviewer's evidence is
  kept on the immutable `admin_recoveries` row.

## Review requirements

A reviewed reset is a security decision. Before submitting it, the platform owner must:

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
  in configuration files. Rotating `ActiveKeyId` keeps old keys available for decryption; recovery codes
  issued under a retired key stop verifying, so rotate only after administrators have re-enrolled or hold a
  reviewer.
- Name at least two platform owners once capability delegation (P01-13) is available, so a reviewed reset is
  possible for any owner. Until then the sole bootstrapped owner recovers only with their own codes.
- If every platform owner loses both authenticator and codes, recovery requires the documented out-of-band
  operator procedure (database-level action under change control with audit), never a UI or unaudited edit.

## Rollout

Apply `20261115000319_AddAdminRecovery` through the licensing migration job before deploying the API. The
migration only adds `admin_recoveries`, `admin_totp.retired_at` and widened audit constraints; existing facts
are unchanged. Its Down refuses (SQLSTATE 55000) once recovery evidence exists. Reproduce the browser proof
with `bash tests/licensing/acceptance/run-p0112-browser.sh`.
