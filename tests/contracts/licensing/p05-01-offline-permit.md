# P05-01 signed offline permit

The merchant API can issue a signed, versioned `OfflineLicensePermitV1` bound to one shop, user, authentication session, device and invoice lease. It is issued from the entitlement that was verified when the lease was reserved (`GrantSnapshotV1`) and ends at the earliest deadline the server knows. This slice ends at the registered issuer and validator; no HTTP response, snapshot record or browser path carries a permit yet (#901 onwards).

## Seam for the next slice

| Step | Production call | Rule |
|---|---|---|
| Reservation | `IOfflineLicencePermitIssuer.CaptureGrantAsync(session, lease)` | Runs inside the reservation's read-committed transaction, before the new lease is saved. It stages one grant row for the same save and never commits. A lease that already exists is refused (`OfflinePermit.LeaseNotNew`), so a legacy lease can never be anchored later. |
| Snapshot completion | `IssueAsync(session, grantId, completion, tillSessionId?)` | `completion` carries only server identifiers and the emitted lot count. Completion time, every deadline and the stock-evidence expiry come from the clock and retained rows. Repeating a completion returns the stored permit after authority is checked again. |
| Historical proof | `VerifyAsync(signedPermit, expectedScope, at)` | Uses only the shop's retained public keys, the expected scope and the instant being judged. |

`ReserveInvoiceLeaseCommandHandler`, `InvoiceLeaseDto`, the snapshot stream and every existing client are unchanged. Existing leases keep their nullable reserver columns.

## Signing key

The `OfflineLicensePermit` configuration section uses the existing named merchant signing-key facility: `LocalKeyId` + `LocalPrivateKeyPem` (Development and Testing only) or `KeyVaultKeyId` with `PublicKeyId` + `PublicKeyPem` pinning the public half of that exact key version. The key must not be shared with any other signing section. Every signature is verified against the public key before anything is stored, and the public key (DER SubjectPublicKeyInfo and its SHA-256) is retained per shop in `offline_license_verification_keys` the first time it signs for that shop. Without a usable key, issuance answers `OfflinePermit.SigningUnavailable` and nothing else changes.

Grants and permits are RS256 compact tokens with distinct protected types (`intelibill-offline-grant+v1`, `intelibill-offline-permit+v1`). The payload is canonical: fixed member order, no whitespace, lowercase hyphenated GUIDs, UTC instants with six fractional digits. Anything else is rejected, even when validly signed.

## Deadline

`validUntil` is the earliest of the caps below; the window is `[notBefore, validUntil)` with `issuedAt = notBefore`. On a tie the earlier row names `limitingReason`.

| Reason | Source |
|---|---|
| `AuthorityFreshness` | Original grant's `authorityVerifiedAt` + 48 h (the older of the account and shop heads). |
| `LicenceEnd` | Original grant's deadline: access end, next boundary or scheduled suspension. |
| `CurrentLicenceEnd` | The same deadline from the head fenced at issue. It only shortens; a more generous head never extends an old reservation. |
| `DeviceFreshness` | Server clock at the authenticated reservation + 48 h. |
| `AuthDeadline` | Earlier of the session and usable refresh-credential ends, frozen at reservation and read again at issue. |
| `LeaseExpiry` | The lease's stored expiry. |
| `FiscalYearEnd` | Start of the fiscal year after the leased series (1 April 00:00 IST). |
| `SnapshotLifetime` | Snapshot completion + 48 h. |
| `StockEvidenceExpiry` | Earliest retained lot evidence of the snapshot; absent only for a complete empty snapshot. |
| `TillDayEnd` | End of the caller's open till session's business day, when the shop runs till sessions. |

Reservation follows verification and capture follows reservation, so for facts this server writes `DeviceFreshness`, `SnapshotLifetime` and `StockEvidenceExpiry` can tie with or trail `AuthorityFreshness` but not precede it while their constants stay at 48 hours and 7 days. Their arithmetic and tie order are pinned by `offline-permit-v1.json`.

A new 48 hours, a renewal or a new grace period needs a new verified reservation. A suspension or closure in effect, lost membership or session, a changed membership or session-scope version, or stale or incoherent proof refuses issuance. Refusals write nothing.

## PostgreSQL scenarios (`P0501LicensingTests`)

| Scenario | Expected outcome |
|---|---|
| Head verified 2026-11-20 12:00 UTC, lease reserved 2026-11-22 11:59 UTC | Permit window ends 2026-11-22 12:00:00.000000 UTC, reason `AuthorityFreshness`; stored grant keeps revisions 1/1 and the original verification time. |
| New snapshot at 11:59:59 | New permit, same deadline. |
| Head revision 2 arrives, verified at 11:59:59 | The old reservation still signs revision 1 and ends at 12:00; `currentShopRevision` is 2. |
| 12:00:00 | `OfflinePermit.DeadlineReached`; grant, permit and key row counts unchanged. A new reservation is signed under revision 2. |
| Each reachable cap | Licence end, scheduled suspension, stricter current head, auth end, lease expiry, fiscal year end and till day end each set the deadline; issued one microsecond before it, refused at it. |
| Wrong caller, device, lease, grant, shop, snapshot count, catalogue or chronology | Typed refusal, no rows. |
| Restriction and issuance queued on the same fence | Restriction first: issuance is refused, zero permits. Issuer first: one permit at the fenced revisions, then the restriction commits and the next attempt is refused. Same for an account restriction delivered through a sibling shop. |
| Revocation, suspension of membership or version change committed while the issuer waits on the actor row | Refused, no rows. |
| Clock reaches the deadline while the signature is pending | The staged permit is rolled back. |
| Four concurrent first completions | One stored permit; all four callers receive its bytes. |
| Key A rotated to B, A removed from configuration | A's permit still verifies at its historical instant in a new process; new permits are signed by B. Stored key rows are public SPKI only. A retained key id cannot be rebound; a key shared with another signing purpose signs nothing. |
| In-place upgrade from `20261121120000_ProjectClosureReopenings` | Legacy lease rows are byte-identical. The three tables force row security with `USING` and `WITH CHECK`, refuse `UPDATE`, `DELETE` and `TRUNCATE` and hold their constraints. The downgrade refuses while entitlement evidence or these tables hold rows, and leaves schema and rows as they were. |

## Evidence limits

Entitlements in these scenarios are fixture-signed and applied through the production projection store. Permits are signed with locally generated RSA keys through the existing named signing-key facility. The contract vectors were signed once by a throwaway key whose private half was discarded. No licensing provider, payment provider or Azure Key Vault was contacted, and nothing here enables production enforcement.

No browser path changed in this slice, so there is no browser evidence. It applies from the slice that delivers or stores a permit in the web client.
