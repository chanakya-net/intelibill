# Reliable Offline Printing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Print and reprint an issued offline invoice with its original seller identity, number and totals through permitted connectivity loss, without a print ever becoming another sale.

**Architecture:** The device freezes a plan20 `InvoiceDocument` (snapshot version 2) into the queued payload at finalization, built by a TypeScript port of `InvoiceDocumentBuilder.Build` from the seller profile the invoice lease froze. Prints render only that document. A dedicated print guard plus `OfflineInvoiceAccessService` let an `offline=1` print open under the existing offline grace when the API is unreachable, and only for the signed-in issuer's own shop and device.

**Tech Stack:** Angular 21, Vitest, IndexedDB, Bun; ASP.NET Core 10, Wolverine, ErrorOr, xUnit.

**Spec:** Backlog item 21 (pasted plan, 2026-11-03) built on [market-readiness review](../../reviews/2026-10-21-market-readiness.md); plan20 contract in [GST capability matrix](../../gst-invoice-capability-matrix.md).

## Global Constraints

- Angular web + .NET API only; mobile excluded.
- Domain ← Application ← Infrastructure ← Api; ErrorOr, no exception-driven business flow; C# warnings are errors.
- Frontend uses Bun only; no migration needed (no schema change: lease profile is already persisted in `invoice_leases`).
- IndexedDB changes are additive: never delete or rewrite unsynced v1 queue rows; legacy (document-less) rows stay printable.
- Print never calls finalization, never writes the queue, never touches money or stock.
- Offline grace is the existing `canUseOfflineSalesAuthGrace` policy (48 h since last API verification, device enabled, usable snapshot, refresh credential unexpired); immediate remote revocation while disconnected is not promised.

## Decisions (deviations from the backlog text, with reasons)

1. **Seller source = the lease's frozen profile, not a new shop record in the stream.** `OfflineSalePoster` issues the synced document under `InvoiceLease.SellerProfile`; printing from any other source could disagree with the synced document. The reserve-lease response (the lease the device numbers from) and the snapshot's `activeLease` records both gain `sellerProfile`.
2. **Customer snapshot gains `gstin`, `stateCode`, `address`.** The server's buyer block, B2B rules and the services place of supply read them; without them the device document would disagree for registered or out-of-state buyers. Snapshot metadata `SchemaVersion` 2 → 3 (fields are optional; readers accept both).
3. **Device refuses a sale it cannot issue as a document**, mirroring online refusals (capability matrix) with the server's `Invoice.*` codes, before the invoice is handed over; the consumed number is rolled back. A lease without a profile (reserved before plan20) refuses with `SELLER_PROFILE_UNAVAILABLE` until the device is re-enabled online.
4. **Parity is pinned by a shared contract** `tests/contracts/offline-invoice-document-v2.json` run by both the xUnit builder tests and the Vitest builder spec — this stands in for the "compare original/synced document facts" browser step.
5. **Browser acceptance:** the plan48 release harness (`tests/ui-audit/release`, production service worker) does not exist yet. The browser journey is covered by unit/component specs here; the release-harness spec is left to plan48.

## Review Focus

- RF1: Expired access token, API unreachable, valid grace → the issuer's own offline invoice prints without login redirection.
- RF2: Wrong user / other shop / other device / missing row / expired grace / explicit 401 → no document data rendered, no default-shop fallback.
- RF3: Seller GSTIN/address changes after the sale → A4 and thermal reprints keep the original values, number and totals.
- RF4: Legacy queued row without a document → explicit "seller details not recorded" note, no GSTIN invented, reconciliation link offered.
- RF5: Print cancel / print again / failed sync → no new finalization, queue row count and number unchanged.

---

## File map

| File | Responsibility |
|---|---|
| `src/backend/Intelibill.Application/Features/Sales/DTOs/InvoiceLeaseDto.cs` | + `InvoiceSellerProfile? SellerProfile = null` |
| `src/backend/Intelibill.Application/Features/Sales/Commands/ReserveInvoiceLease/ReserveInvoiceLeaseCommandHandler.cs` | return `lease.SellerProfile` |
| `src/backend/Intelibill.Application/Features/OfflineSalesSnapshot/DTOs/OfflineSalesSnapshotStreamRecords.cs` | `OfflineActiveLeaseDto` + `SellerProfile`; `OfflineCustomerLiteDto` + `Gstin`, `StateCode`, `Address` |
| `.../OfflineSalesSnapshot/Services/OfflineSalesSnapshotStreamingService.cs` | fill the new fields; `SchemaVersion: 3` |
| `tests/contracts/offline-invoice-document-v2.json` | NEW shared builder cases |
| `src/frontend/src/app/shared/gst/gst-state-codes.ts` | + `isUnionTerritoryWithoutLegislature(code)` |
| `src/frontend/src/app/features/sales/services/offline-invoice-document.builder.ts` | NEW TS port of `Build` |
| `src/frontend/src/app/features/sales/services/offline-sale-finalization.service.ts` | freeze `payload.document` |
| `offline-sale-core.types.ts`, `sale.models.ts`, `core/storage/invoice-lease-indexeddb.service.ts`, `offline-sales-snapshot.schema.ts`, `offline-sales-device-enablement.service.ts`, `offline-sale-state.service.ts` | additive types + reason keys |
| `offline-sale-invoice.mapper.ts`, `components/sale-invoice-document.util.ts` | document passthrough, legacy note |
| `features/sales/services/offline-invoice-access.service.ts` | NEW authorization + read |
| `core/guards/offline-invoice-print.guard.ts` | NEW route guard |
| `app.routes.ts`, print page, A4/thermal templates, `new-sale-page.offline-flow.service.ts` + template | guard wiring, render readiness, delivery state, blocked-popup link |

## Task 1: Freeze the printable document

**Interfaces:**
- Produces (backend JSON): `InvoiceLeaseDto.sellerProfile`, `activeLease.sellerProfile` = `{ seller: InvoiceDocumentSeller, eInvoiceStatus: 'NotAttested'|'BelowThreshold'|'Exempt'|'Required', seriesCode: string } | null`; `customer.gstin|stateCode|address: string | null`.
- Produces (TS): `interface InvoiceSellerProfile` in `sale.models.ts`; `InvoiceLeaseSnapshot.sellerProfile?: InvoiceSellerProfile | null`; `OfflineQueuedSalePayload.document?: InvoiceDocument | null` (absent ⇒ legacy v1).
- Produces: `buildOfflineInvoiceDocument(input: OfflineInvoiceDocumentInput): OfflineInvoiceDocumentResult` where input = `{ sellerProfile, fiscalYearStart, soldAt, customer: OfflineCustomerLiteSnapshot | null, customerName, customerPhone, pricing: OfflineFrozenSalePricing }` and result = `{ ok: true; draft: Omit<InvoiceDocument,'number'> } | { ok: false; errorCode: string }`; also exported `splitTax(...)`, `indiaBusinessDate(iso): string`.
- Produces: `OfflineFinalizeResult` failure reasons + `'SELLER_PROFILE_UNAVAILABLE' | 'DOCUMENT_UNAVAILABLE'` with optional `errorCode`.

- [ ] **Backend tests (red):** `ReserveInvoiceLeaseCommandHandlerTests.HandleAsync_WhenValid_ReturnsTheFrozenSellerProfile` (DTO profile == lease profile); `OfflineSalesSnapshotStreamingServiceTests`: active lease record carries the lease's `SellerProfile`, customer record carries `Gstin/StateCode/Address`, metadata `SchemaVersion == 3`; `InvoiceDocumentBuilderTests.Build_MatchesTheSharedOfflineContract` iterating the contract file.
- [ ] **Contract file:** cases (each: seller, buyer, lines, expected document facts or `errorCode`): regular intrastate goods with odd tax paisa (CGST gets it), UT seller (UTGST), regular service to registered out-of-state buyer (IGST), composition → BillOfSupply + declaration, unregistered → Receipt, regular mixed taxable/exempt to walk-in → InvoiceCumBillOfSupply, B2B line HSN `12` → `Invoice.HsnRequired`, registered buyer without address → `Invoice.BuyerDetailsRequired`, composition collecting tax → `Invoice.CompositionCannotCollectTax`.
- [ ] **Backend implementation:** add DTO fields (default `null` last positional params), fill them, bump schema version. Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter "ReserveInvoiceLease|OfflineSalesSnapshotStreaming|InvoiceDocumentBuilder"` → green.
- [ ] **Frontend tests (red):** `offline-invoice-document.builder.spec.ts` runs every contract case; `offline-sale-finalization.service.spec.ts` adds `finalization_copies_seller_identity_once` (RF3: lease profile A frozen into `payload.document`; replacing the lease's profile with B afterwards leaves the saved payload and mapped `SaleDto.document` at A with the same number/totals), `refuses_without_lease_seller_profile` (no number consumed: rollback called, no queue write), `refuses_document_errors_and_rolls_back` (errorCode passed through); mapper spec: document passes through and `isLegacyDocument` false, legacy payload ⇒ `document: null`; util spec: `missingDocumentNoteKey(sale, true)` ⇒ `'sales.invoice.offlineLegacyNote'` without a document, `null` with one.
- [ ] **Frontend implementation:** port `Build` exactly (UOM `NOS`/`NA`, `component-split/1`, place of supply, buyer from snapshot customer then payload name/phone, Rule 46 checks, HSN digits, reconcile check, totals grouped by kind order Cgst<Sgst<Utgst<Igst<Cess then rate, `fiscalYearStart` parsed from lease label `YYYY-YY`). Finalizer: after consuming the number, build from `consumed.lease.sellerProfile`; on refusal roll back the number and return the reason; else save `{ ...payload, document: { ...draft, number: invoiceNumber } }`. Enablement copies `sellerProfile`. Map new reasons to `sales.newSale.offline.blockSellerProfileUnavailable` / `blockDocumentUnavailable` (en-IN + every locale). Mapper passes `document`. Print page: legacy shop details only from the active shop entry (no default-shop fallback); legacy note + reconciliation link.
- [ ] **Verify:** `bun run test --run offline-invoice-document.builder offline-sale-finalization offline-sale-invoice.mapper sale-invoice-document.util offline-sales-device-enablement offline-sale-state i18n-coverage` → green. Commit.

## Task 2: Local-only print authorization

**Interfaces:**
- Produces: `OfflineInvoiceAccessService.read(clientSaleId: string): Promise<OfflineInvoiceReadResult>` with `{ granted: true; record: OfflineQueuedSaleRecord } | { granted: false; reason: 'NO_SESSION' | 'NO_DEVICE' | 'GRACE_UNAVAILABLE' | 'NOT_FOUND' | 'NOT_OWNER' | 'SCOPE_CHANGED' }`; `canRead(clientSaleId): Promise<boolean>`.
- Produces: `offlineInvoicePrintGuard: CanActivateFn` — non-`offline=1` URLs delegate to `authGuard`; `offline=1`: authenticated ⇒ true; else `bootstrapSessionWithStatus()`: `READY` ⇒ true, `API_UNREACHABLE` ⇒ `canRead(saleId)` or `/login`, anything else (explicit refusal) ⇒ `/login`.

- [ ] **Tests (red):** access spec `grace_allows_owned_local_print` (expired token + grace ⇒ granted with the record) and table-driven `wrong_scope_denied`: other user (`issuedByUserId` differs), other shop (session shop ≠ record shop), other device (settings deviceId ≠ record), missing row, grace false, no session, session switched during the read (`SCOPE_CHANGED`); legacy row without issuer on own shop/device ⇒ granted. Guard spec: online path delegates; `API_UNREACHABLE` + canRead true ⇒ true; false ⇒ `/login`; `REFRESH_FAILED`/`UNAUTHENTICATED` ⇒ `/login` without consulting grace. Print-page spec: denied read ⇒ error key set and `sale()`/`shop()` stay null.
- [ ] **Implement:** read order = session/user/shop → device settings → auth-or-grace → queue read → re-check session user+shop unchanged (scope epoch) → payload shop/device/issuer match. Page `loadOfflineInvoice` uses `read()` only; reason → `sales.invoice.errors.offline{Device|Access|InvoiceNotFound}`. Route `sales/:saleId/print` uses the new guard.
- [ ] **Verify:** `bun run test --run offline-invoice-access offline-invoice-print.guard sale-invoice-print-page auth.guard` → green. Commit.

## Task 3: Reliable rendering

- [ ] **Tests (red):** print-page `print_cancel_and_retry_preserve_identity` (RF5): `window.print` mocked; initial auto-print once after readiness; two `onPrintAgain()` calls; the queue service sees no write and finalization is never injected/called; a `Failed` record still renders with its original number and shows the delivery state outside the invoice article. A4/thermal specs render an offline document with a long seller address, HSN + UOM, CGST/SGST rows, due/paid and redeemed-credit-note codes; neither component renders the queued banner any more. Offline flow spec: `window.open` returning `null` sets `blockedPrintUrl` to the print URL.
- [ ] **Implement:** `awaitPrintReady()` (fonts ready + one animation frame) before the automatic print; page-level screen-only delivery status from `record.status`; drop the in-invoice queued banners; offline flow exposes `blockedPrintUrl` and the confirmation renders a link when set.
- [ ] **Verify:** `bun run test` (full), `bun run build`, `bun run audit:styles`; `dotnet build src/backend/Intelibill.slnx`; `dotnet test` Application + Api unit projects. Commit.

## Rollout / rollback

Readers accept v1 (no document) and v2 payloads; snapshot schema 2 and 3 records both parse. Every lease stored before this release lacks a seller profile (the reserve response never carried one), so every enabled device must have offline billing turned on again, online, by an owner or manager: the POS shows that notice online as soon as it opens, and an offline sale on such a lease is refused with the same instruction. Selecting a customer the offline snapshot does not hold is refused (the server would issue to that customer's live record). Rollback = deploy previous frontend; v2 rows keep extra fields that older readers ignore; queue ownership unchanged.
