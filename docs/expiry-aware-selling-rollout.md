# Expiry-aware selling and return quarantine

This release supports merchant-managed, nonregulated goods. It does not establish pharmacy/food compliance, date authenticity, stock recoverability, or merchant approval. API and web are included; mobile is outside scope. No production migration, policy enablement, deployment or pilot approval is implied.

## Schema and deployment order

Apply these migrations in order through the deployment migration job before API/readers/web:

1. `20261108182442_AddExpiryPolicies`
2. `20261108184604_AddExpirySaleEvidence`
3. `20261108193354_AddOfflineExpiryEvidence`
4. `20261108210054_AddBatchDispositionEvidence`

They follow `20261108130102_AddTillReconciliationEvidence`. Startup does not migrate. Existing physical/aggregate quantities, dates, issued invoice JSON and monetary facts remain unchanged. Existing lots receive zero quarantine; historical sales have no new expiry decisions. Only an explicit compatible policy/history baseline is seeded for existing shops. No dates, recovered quantities or quarantine events are inferred. New shops initialize their own actor-backed policy/history transactionally; missing retained state/history is an error, never an Ignore fallback.

Deploy every writer and reader before an owner configures expiry, including sales/preview/search/barcode/availability, all single/bulk/inbound-merge/PO receipt paths, both retail/legacy date edits, initial/recovery/review offline posting and catalog streaming, return/void/cancellation, counts, adjustment create/void, supplier shipment and receipt/group reversal, and financial/report/export readers. Drain incompatible workers and old API pods; preserve current till/period gates. Use a drained/maintenance cutover so the first compatible writer cannot create new expiry/quarantine facts while any incompatible writer remains. Ignore is not a write-disable gate: online sales/catalogs retain evidence and expired recovery can quarantine even under Ignore. Deploy the compatible web afterward. An old/disconnected client cannot establish current online approval; rejected operations must use the supported refresh/recovery workflows. Never run pre-feature writers after evidence or quarantine exists.

## Conservative policy and readiness

Defaults are `Mode=Ignore`, `ExpiryRequired=false`, `SellThroughExpiryDate=true`, `WarningDays=7`, `OverrideAllowed=false`; item overrides inherit when absent/reset. There is no global enablement switch for expiry. Owner policy changes require a positive expected catalog version and a 1–500 character reason. All changes, including weakening/resetting policy, retain revision history and advance the catalog.

For non-Ignore policies, expiry occurs after the expiry date when sell-through is true, or on that date when false, using the shop business date/time zone. Required missing dates always hard-block, even under Ignore. Block has no override. Warn expired goods require enabled overrides and the live Owner/Manager's own session, exact batch/summed quantity/xmin/policy/catalog binding and reason. Near-expiry is informational. FEFO sorts eligible dated lots, then undated, then batch number/ID. `includeUnavailable=true` on available batches explains blocked lots with zero available quantity.

Before configuring a shop, an Owner/Manager runs `GET /api/expiry-policies/readiness?page=1&pageSize=50` (maximum100 per page) and verifies source dates and category choices with the merchant. The read-only [audit](../scripts/db/audit-expiry-readiness.sql) requires explicit shop UUID and shop-local business date; use its documented psql command with the same day as the API. Runtime RLS still requires authorized session scope. It reports independent missing-date, expired and quarantined counts, physical/quarantine quantities and all affected lots; missing includes optional dates and Ignore does not hide risks. Invalid historical manufacturing/expiry pairs are additional diagnostics. Empty output means unavailable shop/state/default history, not a ready shop. Retain the audit and tenant-policy verification output with release evidence. Never bulk-fill guessed dates or edit facts in SQL.

## Routes, authority and serialization

- `GET/PUT /api/expiry-policies` and `GET/PUT /api/expiry-policies/items/{id}`: current members read, Owner writes; a null item policy resets inheritance. Readiness requires Owner/Manager.
- `GET/POST /api/inventory/batches/{id}/dispositions`: current members read retained root/event history; Owner alone releases/disposes. POST binds operation key, nonzero expected batch xmin, root ID, Release/Dispose, positive item-scale quantity and reason. `GET /api/financial-operations/batch-disposition/{key}` recovers the saved outcome; operation type27. Live owner/tenant authorization precedes replay. Unknown HTTP outcomes or lookup404 retain the same durable key/payload.

Lock order is operation receipt → reconciliation case when applicable → posting period → expiry fence → till → customer/supplier → document/credit → batch/item. Shared expiry reads use `FOR SHARE`; policy/date edits use `FOR UPDATE` before item/batch locks. Five-second contention answers `Expiry.PolicyBusy` and restores the caller's lock timeout. Recheck authority and reload facts after waiting. Date corrections require expected batch xmin plus the existing prices Reason or legacy Notes; each date change appends history and advances the catalog. It does not release quarantine or change past decisions/documents.

## Offline authority and recovery

Only completed schema4 catalogs activate new authority. Each lot names immutable server-issued policy/date/quantity evidence, persisted before streaming, valid for seven days. The server validates issue time, shop/batch/item, all retained fields, relevant revision history, lease/issuer standing and business date. Already-issued invoice JSON, arithmetic and historical hashes remain frozen. Offline warning approval is unavailable; reconnect and use the approving manager's online session for a new sale.

Disconnected devices cannot learn remote changes instantly. A policy/date change before claimed issuance invalidates stale relevant evidence on sync; a change/expiry after a supported issue does not. Missing, partial, forged or unsupported evidence is retained as `ExpiryReview=14`. Legacy absence/null is accepted only when retained server history proves the migrated pre-feature Ignore baseline at issue; a new shop or changed-back Ignore is not proof. Do not decorate queued envelopes with today's catalog/till/policy facts.

ExpiryReview offers only supported Escalate, exact trusted LinkExistingSale, evidence-backed RetryHistoricalLease and, where independently eligible, ReconcileTillSale. No LinkStockCorrection/AcknowledgeVariance or manager “post anyway” bypass exists. Refresh future-use catalogs; retain old envelopes/proofs. Use the reconciliation console and original operation lookup/key to recover supported outcomes. If historical authority cannot be proved, retain/escalate the case for investigation; a reason does not manufacture proof. Till/closed-period review may independently hold it. Never repair a held envelope or its history directly in the database.

## Physical quarantine and accounting

Physical batch/aggregate stock includes held goods. `0 <= quarantine <= physical`; sellable stock starts from physical minus quarantine and then applies expiry. Recovered Restockable returns and eligible cancellations add physical goods and quarantine when expired (even under Ignore) or missing a required date. Original source lines freeze cause/quantity/root facts. Wastage remains unrestored; cancellation eligibility and settlement limits remain unchanged.

Append-only root events and Release/Dispose/Reverse children conserve each source's balance and the batch quarantine total. Release reduces quarantine only, after a genuine audited correction for a missing-date root and a currently valid, nonexpired, nonblocked/no-approval decision. Originally expired roots cannot be released by changing the current date. Disposal reduces physical and quarantine together and records a dated linked stock-loss adjustment at original recorded batch cost; it has no generic void. Return void reverses its exact original disposition and refuses a consumed root. Counts cannot clear quarantine or set physical below it; ordinary adjustments/supplier shipment consume unquarantined stock. Receipt reversals refuse quarantine history.

Quarantine itself posts no independent loss. Return/cancellation COGS reverses once under existing accounting; explicit disposal recognizes independent loss once on its own business date. Valuation reports quarantine quantity/value separately at recorded cost, without claiming recoverability or impairment assessment. Issued documents and historical source dates remain immutable.

## Forward recovery and release checks

All four Down methods refuse before destructive operations. Retain schema, journals, invoice/offline evidence, operation responses, guards and compatible readers. Rollback is a compatible forward fix or explicit reasoned Owner policy change; Ignore never releases quarantine or erases facts. A pre-feature binary downgrade is unsafe. Recover operator mistakes with supported policy/date correction, eligible Release/Dispose, source-void rules and reconciliation actions; no direct DB fact rewrite.

Before release, finish all changes and run `dotnet build src/backend/Intelibill.slnx` to exit, then the full solution tests with Docker on those stable binaries (`dotnet test src/backend/Intelibill.slnx --no-build`). Never overlap any build/EF compilation with testhosts. Verify no pending EF model change, tenant manifest/RLS, populated pre-feature upgrade, immutable evidence, Down refusal, online/offline/disposition acceptance and accounting. The web gate is `bun run test --watch=false`, `bun run build`, `bun run audit:ui:release` on supported Node. Carry forward unchanged, exact-head evidence rather than rerunning unrelated broad gates. Record failures by name and actual process exits; independent branch review/PR belong to the coordinator.
