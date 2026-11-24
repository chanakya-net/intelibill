# P05-05 — Licence history review contract

Apply the merchant migration `20261123030720_AddLicenseHistoryReviewEvidence` before starting the API or deploying the merchant web build. No licensing-service migration, broker, or mobile change is part of this slice.

## Schema and retention guarantees

- `ReconciliationIssueType.LicenseHistoryReview = 15` is used for all new historical licence holds opened by offline sync or recovery bundle submission.
- Partial unique index `ix_reconciliation_issues_shop_id_offline_submission_id` on `reconciliation_issues (shop_id, offline_submission_id) WHERE issue_type = 15 AND offline_submission_id IS NOT NULL` guarantees at most one typed `LicenseHistoryReview` case per held envelope while leaving multiple post-sale variance issues (`PricingVariance`, `DiscountVariance`, `StockVariance`, etc.) unaffected.
- Append-only table `license_history_review_observations` stores deduplicated conflict observations keyed by `(shop_id, case_id, fingerprint)` (SHA-256 over the normalized observation fields excluding `observed_at`), with tenant-scoped composite foreign keys to `reconciliation_issues (id, shop_id)` and `offline_submissions (id, shop_id)`.
- Row-level security (`ENABLE` + `FORCE ROW LEVEL SECURITY`) isolates observations by `app.active_shop_id`. `BEFORE UPDATE OR DELETE` and `BEFORE TRUNCATE` triggers reuse `public.retain_entitlement_evidence()` to keep observations strictly append-only.
- The migration `Down` path is guarded and fails with SQLSTATE `55000` whenever any `license_history_review_observations` row or `issue_type = 15` case exists.
- Older `#903` `ProvenanceReview` cases carrying `OfflineLicenceHistory.*` codes are read compatibly under the same assessment and action policy without rewriting their case ID, type, or audit history; missing observation records surface `hasStoredObservations: false` and an empty `observations` list.

## Intake, concurrency, and immutability

- Offline sync (`POST /api/sales/offline-sync`) runs in two short transactions under a bounded 5-second per-`(shopId, deviceId)` PostgreSQL transaction advisory lock (`LockDeviceIntakeAsync`):
  1. Transaction 1 records and commits the `OfflineSubmission` receipt (`ON CONFLICT DO NOTHING` preserves the earliest `ReceivedAt`, source, and captured `LicencePermitId` / `LicenceGrantId`).
  2. Transaction 2 re-reads the committed receipt, checks for an existing canonical history hold, checks independent structural/lease/timing/collision rules, enters `IShopLicenceFence` (`LicenceAction.Recovery`), runs `IOfflineLicenceHistoryAuthority.AssessAsync` before generic `HistoricalLeaseReviewRequired`, and stages sales behind a `SAVEPOINT` so any hold or security suspension rolls back business writes while committing the case and observation atomically.
- Recovery bundle submission (`POST /api/offline-reconciliation/recovery-submissions`) acquires the same device lock and licence fence and runs historical licence assessment before opening `RecoveryReview`, while preserving independent provenance, arithmetic, timing, expiry, and till checks.
- Repeat identical submissions across sync and recovery (including across lease expiry, escalations, and concurrent races) reuse the single original case, append only genuinely new observations (advancing the case `xmin` version when a new observation is stored), and never mutate `OfflineSubmission.PayloadJson`, `ReceivedAt`, `ClaimedSoldAt`, or `InvoiceNumber`.
- Non-reviewer staff callers who resend another user's held envelope receive `NeedsReview` with `reviewCaseId: null` and cannot discover or access another user's case ID.

## Resolution policy and live authority

- `ReconciliationActionPolicy` classifies a case as a licence history case when `IssueType == LicenseHistoryReview`, `Errors.OfflineLicenceHistory.IsHistoryCode(Code)`, or stored observations exist.
- Permitted actions on a licence history case are strictly:
  - `LinkExistingSale` (preferred alone, without `RetryHistoricalLease`) when an exact posted offline sale matches the held envelope (`ReconciliationLinkRules.CheckHistorySaleAsync`: same shop, `SaleSource.Offline`, same deviceId, `clientSaleId`, recomputed & stored payload hash, exact `invoiceNumber`, microsecond-normalized `soldAt`, consistent recorded issuer, and compatible envelope link). Linking is metadata-only (zero financial, stock, sequence, or posting-period mutations; allowed even when the original posting period is closed).
  - `RetryHistoricalLease` (only when no exact posted sale matches, the envelope is unlinked and valid, an attributable user exists, and live `IOfflineLicenceHistoryAuthority.AssessAsync(..., existingHistoryHold: true)` returns `Supported`). Posting reuses the exact stored envelope payload, hash, invoice number, and claimed `soldAt`, and remains subject to all existing posting-period, expiry, till, stock, tax, and credit gates (`CreditReviewed: false`).
  - `Escalate` while `Status == Open` (the only action staff may take on their own open case; managers/owners may also escalate).
- Redating, renumbering, deleting, acknowledging variance, linking stock corrections, reconciling till sales, or posting anyway are refused (`400 BadRequest` / `403 Forbidden`).
- Observe-only mode never authorizes resolution of an existing history hold, and a later commercial renewal covering only later dates never retroactively proves an earlier uncovered billing instant.
- Both `POST /api/offline-reconciliation/{caseId}/resolve` and `POST /api/offline-reconciliation/recovery-submissions` pass server-read `SessionClaims` and recheck live session validity, login enabled state, active membership/role, case visibility, and live licence fence before execution, under lock, on fenced idempotent replay (`FinancialOperationExecutor.ReplayUnderFenceAsync`), and before commit.

## API DTO v1 (`LicenceHistoryReviewDto`)

`ReconciliationCaseDto` adds an optional trailing property `licenceHistory` (`version: 1`) populated for licence history cases (null for non-history cases):
- `currentReasonCode`, `currentSubreason`, `retryEligible`, `linkEligible`, `hasStoredObservations`, `firstReceivedAt`
- Safe claimed-vs-retained identifiers (`claimedPermitId`, `retainedPermitId`, `claimedGrantId`, `retainedGrantId`, `claimedLeaseId`, `originalLeaseId`, `claimedSnapshotId`, `claimedKeyId`, `retainedKeyId`)
- `permitNotBefore`, `permitValidUntil`, `sourceCompletionCutoff`
- Chronological `observations[]` (`reasonCode`, `subreason`, `observedAt`, `comparedField`, `windowStart`, `windowEnd`, `observedAccountRevision`, `observedShopRevision`, `sourceCompletionCutoff`, `competingSubmissionId`)

No raw signed permits, JWTs, unbounded client strings, commercial plan details, or cross-tenant/inaccessible user details appear in the DTO.

## Verification boundaries

- **Backend**: Verified against the real ASP.NET Core merchant API and real PostgreSQL (Testcontainers, including restricted non-superuser `intelibill_app` runtime role for RLS and append-only triggers) using a fixed `TimeProvider` clock and locally generated signing keys (`P0505LicensingTests`, `P0504LicensingTests`, `OfflineReconciliationControllerTests`). No live external licensing provider is contacted.
- **Merchant web**: Verified with Vitest unit/i18n coverage tests (all 9 locales) and real Chromium (`chromium-desktop` and `chromium-mobile`) on the normal `/sales/offline-reconciliation` route backed by routed fixture HTTP (`tests/ui-audit/mock/offline-reconciliation.spec.ts --grep 'P05-05'`).
