# P05-01 signed offline permit

The merchant API can issue a signed, versioned `OfflineLicensePermitV1` bound to one shop, user, authentication session, device and invoice lease. It is issued from the entitlement that was verified when the lease was reserved (`GrantSnapshotV1`) and ends at the earliest deadline the server knows. This slice ends at the registered issuer and validator; no HTTP response, snapshot record or browser path carries a permit yet (#901 onwards).

## Seam for the next slice

| Step | Production call | Rule |
|---|---|---|
| Reservation | `IOfflineLicencePermitIssuer.CaptureGrantAsync(session, lease)` | Runs inside the reservation's read-committed transaction, before the new lease is saved. It proves live actor authority (including a usable matching refresh credential) before exposing any commercial licence outcome, isolates first-use key retention in a savepoint rolled back on any refusal, rechecks auth, licence, lease and clock after the signer returns (with or without a signature) and again after key retention, stages one grant row for the same save and never commits. A lease that already exists is refused (`OfflinePermit.LeaseNotNew`), so a legacy lease can never be anchored later. |
| Snapshot capture | `IOfflineLicencePermitIssuer.StartSnapshotCapture(session, grantId, leaseId, deviceId)` | Server-owned capture context fixing snapshot, grant and lease scope at start. Enumerating `StreamAsync` to the matching completion record produces the immutable `OfflineSnapshotCompletion` with the server-stamped completion instant; early exit, cancellation or error leaves it null. Genuinely empty completed streams (`BatchCount == 0`) are supported. The completion object is the proof: the issuing host recognizes that instance (a weak table keyed by object identity) for as long as the caller holds it, whatever other captures begin. A copy with equal values, a completion made on another host and an invalidated one are refused, so the #901 caller must issue from the capture it streamed, on the same host. |
| Snapshot completion | `IssueAsync(new OfflinePermitRequest(session, grantId, completion, tillSessionId))` | Validates the host's own completion object against retained `offline_expiry_evidence` rows, preserves `completion.CompletedAt` across waits/retries, rejects unverified `tillSessionId` when tills are disabled, checks the half-open `[NotBefore, ValidUntil)` window after waits, and replays stored permits only when every immutable completion identity field matches and the instant lies inside `[NotBefore, ValidUntil)`. |
| Historical proof | `VerifyAsync(signedPermit, expectedScope, at)` | Uses only the shop's retained public keys, the expected scope and the instant being judged. |

### Legacy-fallback contract for `#901` reservation consumers

When `ReserveInvoiceLeaseCommandHandler` calls `CaptureGrantAsync` inside its reservation transaction:

- **May fall back to a legacy, unanchored lease** (only when trading enforcement itself did not block the reservation): `OfflinePermit.SigningUnavailable`, `OfflinePermit.AuthorityUnavailable`, and non-enforced `Licence.*` commercial outcomes (for example, on an observe-only or unprojected shop). Because `CaptureGrantAsync` rolls back its capture savepoint on refusal, the caller can commit the legacy `InvoiceLease` with zero grant, permit or key rows written. `OfflinePermit.SigningUnavailable` is answered only while auth, licence, lease and clock still hold after the signer's wait.
- **Must refuse the reservation (never fall back to a legacy lease)**: `Auth.SessionInvalidated`, `OfflinePermit.ScopeMismatch`, `OfflinePermit.LeaseNotNew`, `OfflinePermit.KeyRebound`, `OfflinePermit.Busy`, `Licence.Busy`, `OfflinePermit.OutsideValidity`, `OfflinePermit.DeadlineReached`, and enforced `Licence.*` denials. A signer that gives up after one of these bounds passed answers the bound, not `OfflinePermit.SigningUnavailable`; `IssueAsync` applies the same precedence to the licence head and the permit window. Caller cancellation is never mapped to either.

`ReserveInvoiceLeaseCommandHandler`, `InvoiceLeaseDto`, the snapshot stream and every existing client are unchanged. Existing leases keep their nullable reserver columns.

## Signing key

The `OfflineLicensePermit` configuration section uses the existing named merchant signing-key facility: `LocalKeyId` + `LocalPrivateKeyPem` (Development and Testing only) or `KeyVaultKeyId` with `PublicKeyId` + `PublicKeyPem` pinning the public half of that exact key version. The key must not be shared with any other signing section. A single five-second budget bounds both key resolution and signing while authority locks are held, and expected key-provider or cryptographic failures (including an unusable local key) map to `OfflinePermit.SigningUnavailable` without logging provider messages or key material while preserving caller cancellation. Every signature is verified against the public key before anything is stored, and the public key (DER SubjectPublicKeyInfo and its SHA-256) is retained per shop in `offline_license_verification_keys` the first time it signs for that shop.

Grants and permits are RS256 compact tokens with distinct protected types (`intelibill-offline-grant+v1`, `intelibill-offline-permit+v1`). The protected header is matched byte-for-byte as canonical ASCII without fallible JSON string decoding, and the payload is canonical: fixed member order, no whitespace, lowercase hyphenated GUIDs, UTC instants with six fractional digits. Anything else is rejected as `OfflinePermit.Malformed`, without key lookup for malformed headers.

## Deadline

`validUntil` is the earliest of the caps below; the window is `[notBefore, validUntil)` with `issuedAt = notBefore`. On a tie the earlier row names `limitingReason`.

| Reason | Source |
|---|---|
| `AuthorityFreshness` | Original grant's `authorityVerifiedAt` + 48 h (the older of the account and shop heads). |
| `LicenceEnd` | Original grant's deadline: access end, next boundary or scheduled suspension. |
| `CurrentLicenceEnd` | The same deadline from the head fenced at issue. It only shortens; a more generous head never extends an old reservation. |
| `DeviceFreshness` | Server clock at the authenticated reservation + 48 h. |
| `AuthDeadline` | Earlier of the session and usable matching refresh-credential ends, frozen at reservation and read again at issue; a session without a usable matching refresh credential is refused. |
| `LeaseExpiry` | The lease's stored expiry. |
| `FiscalYearEnd` | Start of the fiscal year after the leased series (1 April 00:00 IST). |
| `SnapshotLifetime` | Server-recorded snapshot completion (`CompletedAt`) + 48 h. |
| `StockEvidenceExpiry` | Earliest retained lot evidence of the snapshot; absent only for a genuine complete empty snapshot. |
| `TillDayEnd` | End of the caller's open till session's business day, when the shop runs till sessions. |

Reservation follows verification and capture follows reservation, so for facts this server writes `DeviceFreshness`, `SnapshotLifetime` and `StockEvidenceExpiry` can tie with or trail `AuthorityFreshness` but not precede it while their constants stay at 48 hours and 7 days. Their arithmetic and tie order are pinned by `offline-permit-v1.json`.

A new 48 hours, a renewal or a new grace period needs a new verified reservation. A suspension or closure in effect, lost membership or session, an expired/missing refresh credential, a changed membership or session-scope version, or stale or incoherent proof refuses issuance. Refusals write nothing.

## PostgreSQL scenarios (`P0501LicensingTests`)

| Scenario | Expected outcome |
|---|---|
| Head verified 2026-11-20 12:00 UTC, lease reserved 2026-11-22 11:59 UTC | Permit window ends 2026-11-22 12:00:00.000000 UTC, reason `AuthorityFreshness`; stored grant keeps revisions 1/1 and the original verification time. |
| New snapshot at 11:59:59 | New permit, same deadline. |
| Head revision 2 arrives, verified at 11:59:59 | The old reservation still signs revision 1 and ends at 12:00; `currentShopRevision` is 2. |
| 12:00:00 | `OfflinePermit.DeadlineReached`; grant, permit and key row counts unchanged. A new reservation is signed under revision 2. |
| Each reachable cap + shorter refresh credential | Licence end, scheduled suspension, stricter current head, auth end (including a shorter current refresh credential outlived by its session), lease expiry, fiscal year end and till day end each set the deadline; issued one microsecond before it, refused at it with unchanged evidence. |
| Disabled tills with unrelated till ID | Typed refusal (`OfflinePermit.ScopeMismatch`), unchanged evidence; enabled-till coverage stays passing. |
| Snapshot capture provenance | Genuine empty completed stream issues; unknown/incomplete/abandoned, copied and foreign-host captures refuse with zero writes; two same-shop reservations with equal catalogue/counts can only issue or replay their own originating capture with its original completion instant. |
| Backward clock movement | Moving time between capture and `NotBefore` on replay, or moving time backward during signing, refuses with `OfflinePermit.OutsideValidity` matching `VerifyAsync` and leaves permit counts unchanged. |
| Live actor authority before commercial refusal | Against an unprojected or restricted shop, only a valid session receives the commercial licence outcome; a revoked session receives `Auth.SessionInvalidated` and neither writes evidence. |
| Late capture refusal during signing | With a fresh signing key, advancing the clock through licence, auth or lease boundaries during signing refuses atomically; committing a permitted outer legacy reservation leaves the lease with zero grants, permits or keys. |
| Unavailable signer after a bound passed | The signer waits and returns nothing while the fixed clock moves. At reservation: refresh-credential end `Auth.SessionInvalidated`, lease expiry `OfflinePermit.DeadlineReached` (`LeaseExpiry`), licence freshness `Licence.VerificationUnavailable`, backward clock `OfflinePermit.OutsideValidity`; the reservation rolls back, zero evidence. At issuance: the lease, licence and clock cases answer the same codes with grant, permit and key counts unchanged. Still inside every bound, both answer `OfflinePermit.SigningUnavailable` and the reservation may keep its legacy lease. |
| Held completion across host activity | With time and authority fixed, 20,000 unrelated captures begin, three complete and are dropped, and memory is fully collected. The held completion then issues its first permit, the earlier one replays its stored bytes, evidence stays at one grant, two permits, one key, and an equal copy of the held completion is refused. |
| Signer timeouts and failures | Delayed resolver, provider failure and unusable public-only local key return `OfflinePermit.SigningUnavailable` within budget, leave evidence unchanged and promptly release PostgreSQL authority locks. |
| Restriction and issuance queued on the same fence | Restriction first: issuance is refused, zero permits. Issuer first: one permit at the fenced revisions, then the restriction commits and the next attempt is refused. Same for an account restriction delivered through a sibling shop. |
| Revocation, suspension of membership or version change committed while waiting on the actor row | Refused, no rows. |
| Four concurrent first completions | One stored permit; all four callers receive its bytes. |
| Key A rotated to B, A removed from configuration | A's permit still verifies at its historical instant in a fresh host (`WebApplicationFactory` configured only with key B); new permits are signed by B. Stored key rows are public SPKI only. A retained key id cannot be rebound; a key shared with another signing purpose signs nothing. |
| In-place upgrade and isolated downgrade guard | Legacy lease rows are byte-identical. The three tables force row security with `USING` and `WITH CHECK`, refuse `UPDATE`, `DELETE` and `TRUNCATE` and hold their constraints. Downgrade refuses both on retained entitlement evidence (`Retained entitlement evidence prevents downgrade`) and independently when only offline verification keys are retained (`Retained offline licence evidence prevents downgrade`), restoring table contents and forced RLS. |

## Evidence limits

Entitlement projections in `P0501LicensingTests` are applied through the production `EntitlementProjectionStore.ApplyAsync` using a synthetic, already-verified `VerifiedServiceAssertion` (placeholder key metadata, real SHA-256 body digest and literal `fixture-verified-assertion`); P0501 performs no entitlement-signature signing or verification. Entitlement-signature verification over HTTP (`SignedServiceAssertion` through `EntitlementEndpoints`) is covered separately by `P0402LicensingTests`.

Grants (`GrantSnapshotV1`) and permits (`OfflineLicensePermitV1`) in P0501 are signed with locally generated 2048-bit RSA keys through the existing named signing-key facility (`OfflinePermitSigningKeys` / `MerchantBillingDelegationKeys`). Key rotation is verified by disposing the first `WebApplicationFactory` host and creating a second fresh `WebApplicationFactory` configured only with key B in the same test process. The contract vectors in `offline-permit-v1.json` were signed once by a throwaway key whose private half was discarded. No live licensing provider, payment provider or Azure Key Vault was contacted, and nothing here enables production enforcement.

No browser path changed in this slice, so there is no browser evidence. It applies from the slice that delivers or stores a permit in the web client.
