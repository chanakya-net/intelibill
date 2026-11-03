# GST Invoice Contract and Fiscal Numbering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Issue stable Indian retail documents (tax invoice, invoice-cum-bill of supply, bill of supply, receipt) with the seller's declared tax identity, component taxes and one fiscal-year number series shared by online posting and offline ranges.

**Architecture:** A shop declares a tax profile (regime, state, GSTIN, legal name, series code, e-invoice/turnover attestation). At posting, `InvoiceDocumentBuilder` applies a capability matrix to the priced lines and either refuses the sale or produces an immutable `IssuedInvoiceSnapshot` (version 2) stored as jsonb on the sale. `IInvoiceNumberAllocator` numbers online sales inside the posting transaction and reserves offline ranges from the same locked `invoice_sequences` row, keyed per shop + fiscal year + series and registered to one seller registration. Fiscal years follow the India business date (UTC+05:30). Print and Tally render the snapshot; sales without one are legacy.

**Tech Stack:** ASP.NET Core 10, Wolverine, ErrorOr, FluentValidation, EF Core/PostgreSQL, xUnit + NSubstitute + Testcontainers; Angular 21, NgRx, PrimeNG, Vitest, Bun.

**Spec:** Backlog item 20 "GST invoice contract and numbering" (pasted brief, 2026-11-03); [market-readiness review](../../reviews/2026-10-21-market-readiness.md); [web evidence](../../reviews/2026-10-21-market-readiness-evidence/web-workflows.md); [business evidence](../../reviews/2026-10-21-market-readiness-evidence/backend-business.md).

## Global Constraints

- Angular web + .NET API only; mobile excluded.
- Domain ← Application ← Infrastructure ← Api; repositories + `IUnitOfWork` writes; ErrorOr, no exception-driven business flow.
- Bun only; C# warnings are errors; one migration `AddIssuedInvoiceSnapshotsAndFiscalSeries`, applied by the migration job, never API startup.
- Never infer a regime from a GSTIN string; never invent zero tax to make a sale fit; never rewrite an issued snapshot.
- Already-posted sales keep their numbers and amounts; they render as legacy (no snapshot) — live master data is never backfilled as history.

## Findings that shape the plan (source at `9120c6607`)

1. Online numbers are random (`INV-yyyyMMdd-xxxxxxxx`, `RecordSaleCommandHandler`); offline leases use `INV-2026-27-` + 6 digits = 18 characters, over Rule 46's 16.
2. `invoice_sequences` is unique on `(shop_id, fiscal_year_start)`; the lease repository locks it under a serializable transaction.
3. `FiscalYear.ForDate` and the lease expiry cap use 1 April 00:00 **UTC**; an Indian sale between 00:00 and 05:30 IST on 1 April lands in the old year. No business-date concept exists (backlog 27 is not built), so this plan adds a fixed India business calendar.
4. `Shop` has free-text `State` and an optional `GstNumber` (regex only, no checksum); `Customer` has no GSTIN or state.
5. Print components read live `ShopDetails`; A4 labels the taxable column "Tax Amount", shows no "unpaid" state, and both prints list credit notes *issued by this sale's returns* (`returns[].creditNote`) instead of those redeemed on it. The frontend model calls the redemption list `creditNoteRedemptionSummaries` with `amount`, but the API sends `creditNoteRedemptions` with `appliedAmount`, so it is never populated.
6. Online pricing yields `lineTotal = taxable + tax` for every line (`tax = max(0, total − taxable)`), so components can be split from the stored line tax without repricing.
7. Offline device prints use a stub shop (name only, no GSTIN); this plan does not change device-side printing (follow-up).

## Decisions

1. **Tax profile on `Shop`:** `LegalName?` (≤200), `StateCode?` (GST state code), `TaxRegime` (`NotDeclared=0`, `Regular=1`, `Composition=2`, `Unregistered=3`), `InvoiceSeriesCode` (default `A`, `^[A-Z0-9]{1,4}$`), `EInvoiceStatus` (`NotAttested=0`, `BelowThreshold=1`, `Exempt=2`, `Required=3`) + `EInvoiceAttestedAt/ByUserId`. Declared through `PUT api/shops/{shopId}/tax-profile` (owner) or an optional `taxProfile` on `POST api/shops`. `Shop.DeclareTaxProfile` validates: regime declared; state code known; Regular/Composition need a checksum-valid GSTIN whose first two digits equal the state code; Unregistered must have no GSTIN; Regular needs an attestation (`NotAttested` refused). Existing shops migrate as `NotDeclared` with series `A`.
2. **Capability matrix (`InvoiceDocumentBuilder`):**

   | Seller | Lines | Document | Gate |
   |---|---|---|---|
   | NotDeclared | any | — | `Invoice.TaxProfileRequired` |
   | Regular, `Required` e-invoice | any | — | `Invoice.EInvoiceUnsupported` (IRN/QR integration out of scope) |
   | Regular | all rate > 0 | Tax invoice | — |
   | Regular | all rate 0 | Bill of supply (s.31(3)(c)) | — |
   | Regular | mixed, unregistered buyer | Invoice-cum-bill of supply (Rule 46A) | — |
   | Regular | mixed, registered buyer | — | `Invoice.MixedSupplyToRegisteredBuyer` (review: Rule 46A is for unregistered recipients) |
   | Composition | any tax > 0 | — | `Invoice.CompositionCannotCollectTax` |
   | Composition | interstate | — | `Invoice.CompositionInterstateUnsupported` |
   | Composition | tax 0, intrastate | Bill of supply + composition declaration | — |
   | Unregistered | any tax > 0 | — | `Invoice.UnregisteredCannotCollectTax` |
   | Unregistered | tax 0 | Receipt (no GSTIN, "not registered under GST") | — |

   Regular extras: registered buyer (customer GSTIN) needs buyer name + address (`Invoice.BuyerDetailsRequired`); unregistered buyer with taxable value ≥ ₹50,000 needs name, address and state (Rule 46(e)); HSN: `BelowThreshold` → B2B lines need a 4/6/8-digit code, `Exempt` (turnover above the e-invoice threshold) → every line 6/8 digits (`Invoice.HsnRequired`); `Exempt` adds the Rule 46 e-invoice exemption declaration. Reverse charge is always `false` (no RCM input exists). Every line must satisfy `total = taxable + tax` and the lines must sum to the sale totals (`Invoice.AmountsDoNotReconcile`).
3. **Place of supply:** `RecordSale` takes optional `placeOfSupplyStateCode`, used as given. Absent → goods: seller state (counter sale, IGST Act s.10(1)(c)); services: the recipient's recorded state, the GSTIN's for a registered buyer, else the seller's (s.12(2), added in review); goods + services whose places differ → `Invoice.PlaceOfSupplyRequired`. Unknown codes (including 96 export, 97 other territory) → `Invoice.PlaceOfSupplyInvalid`. Offline sales use the seller state.
4. **Components (`component-split/1`):** intrastate → CGST + SGST, or CGST + UTGST when the place of supply is a UT without legislature (04, 26, 31, 35, 38); each at rate/2, CGST = line tax / 2 rounded half away from zero, SGST/UTGST = remainder (odd paisa to CGST). Interstate → IGST at full rate = line tax. Base = line taxable. Cess is in the contract but never produced. Rounding adjustment is explicit and currently 0.
5. **Snapshot (`IssuedInvoiceSnapshot`, version 2, jsonb `sales.invoice_document`):** `version, documentType, issuedAt, businessDate, fiscalYearStart, seriesCode, number, seller{legalName, tradeName, address, stateCode, stateName, gstin?, taxRegime}, buyer{name?, phone?, address?, gstin?, stateCode?, stateName?}, placeOfSupplyStateCode, placeOfSupplyStateName, isInterstate, reverseCharge, lines[{lineNumber, description, hsnOrSac?, uom, quantity, unitPrice, discountAmount, taxableValue, taxRatePercent, isExempt, taxComponents[{kind, ratePercent, taxableBase, amount}], lineTotal}], totals{taxableValue, exemptValue, components[], totalTax, roundingAdjustment, invoiceTotal}, declarations[], signatureMode ("AuthorisedSignatory"), taxCalculation ("component-split/1")`. Enums stored as strings. UOM is `NOS` for goods and `NA` for services until backlog 24. Set once (`Sale.AttachInvoiceDocument`); the API returns `document` (null = legacy, `isLegacyDocument`).
6. **Numbering:** format `{series}/{yy}{yy}/{n:D6}` (e.g. `A/2627/000001`), ≤16 characters; capacity = largest `n` that keeps 16 → `Invoice.SeriesExhausted`. `invoice_sequences` gains `series_code` and `seller_registration_key` (normalized GSTIN, or `shop:{id}` without one); unique `(shop_id, fiscal_year_start, series_code)` and `(seller_registration_key, fiscal_year_start, series_code)`. Legacy rows keep their prefix with series `''` and key `legacy:{shop_id}`. A row registered to another shop's GSTIN → `Invoice.SeriesRegisteredToAnotherShop`; this shop's row under a different GSTIN → `Invoice.SeriesRegistrationChanged` (pick a new series code). Gaps are never reused: online numbers are allocated inside the posting transaction (rollback returns nothing), offline ranges are recorded on leases and their unused tails stay unused.
7. **Allocator:** `IInvoiceNumberAllocator.AllocateAsync(shopId, sellerRegistrationKey, seriesCode, fiscalYear, ct)` and `ReserveRangeAsync(..., count, ct)` share one SQL path: `INSERT … ON CONFLICT DO NOTHING`, then `SELECT … FOR UPDATE` on this shop's row, inside the caller's transaction (read committed; the row lock serializes). `RecordSale` now always runs in a transaction and allocates after its validations, before staging; lease reservation uses the same path.
8. **Business date:** `BusinessCalendar` (fixed UTC+05:30, India has no DST). `FiscalYear.ForDate` uses the business date; `FiscalYear.StartsAt`/`NextStartsAt` are 1 April 00:00 IST. Lease expiry caps at the next fiscal start in IST; the offline fiscal-year timing check uses the same rule.
9. **Offline:** lease reservation refuses `NotDeclared` and e-invoice-`Required` shops and freezes the seller profile on the lease (jsonb `invoice_leases.seller_profile`). Sync builds the snapshot from the lease's frozen profile (current shop for legacy leases). A sale already issued on a device always posts; if the matrix refuses it, it posts without a snapshot and files an informational `DocumentReview` (type 11) case the console acknowledges.
10. **Customer:** optional `Gstin` (checksum) and `StateCode`; GSTIN prefix must equal the state; state defaults from the GSTIN.
11. **Tally:** sales vouchers post component ledgers (`Output CGST 9%`, `Output SGST 9%`, `Output UTGST 9%`, `Output IGST 18%`) from the snapshot; legacy sales keep `Output GST {rate}%`. Returns of documented sales reverse into the same component ledgers (added in review); credit-note documents stay in backlog 17.

## Review Focus

- RF1: Online postings and offline reservations in one fiscal year never share a number; a sale at 00:30 IST on 1 April uses the new year. *(Task 2 integration `online_and_offline_ranges_do_not_overlap`, `fiscal_rollover_uses_shop_business_date`, domain `FiscalYear` tests)*
- RF2: ₹100 at 18% → CGST 9 + SGST 9 intrastate, IGST 18 interstate, CGST 9 + UTGST 9 in Chandigarh, total 118; odd-paisa split reconciles. *(Task 1 `component_tax_reconciles`)*
- RF3: Editing shop and customer after issuance leaves the stored snapshot byte-identical. *(Task 1 `snapshot_survives_master_edits`, Task 2 integration)*
- RF4: Regime selects the document; composition/unregistered sales carrying tax are refused. *(Task 1 `regime_selects_document_type`)*
- RF5: Undeclared profile, e-invoice-required seller, composition interstate and unknown place of supply are refused with no sale, stock or ledger posting. *(Task 1 `unsupported_transaction_is_blocked`, Task 2 handler)*

---

### Task 1: Tax identity, capability matrix and immutable documents (backend)

**Files:**
- Create Domain: `Enums/{TaxRegime,EInvoiceStatus,InvoiceDocumentType,TaxComponentKind}.cs`, `Common/{BusinessCalendar,GstStateCodes,Gstin,InvoiceNumberFormat}.cs`, `ValueObjects/IssuedInvoiceSnapshot.cs` (+ `InvoiceSellerProfile`).
- Modify Domain: `ValueObjects/FiscalYear.cs`, `Entities/{Shop,Customer,Sale}.cs`, `Common/Errors*.cs` (shop/customer profile errors).
- Create Application: `Features/Sales/Services/InvoiceDocumentBuilder.cs` (+ `InvoiceIssueContext`), `Common/Errors/Errors.Invoice.cs`, `Features/Sales/DTOs/InvoiceDocumentDto.cs`, `Features/Shops/Commands/DeclareShopTaxProfile/*`.
- Modify Application: shop create/update/details handlers + `ShopDetailsDto`, customer add/edit commands, validators, DTOs.
- Tests: `tests/backend/unit/Intelibill.Domain.Unit.Tests/{Common/GstinTests,Common/BusinessCalendarTests,ValueObjects/FiscalYearTests,Entities/ShopTaxProfileTests}.cs`, `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Sales/Services/InvoiceDocumentBuilderTests.cs`.

- [ ] Write `InvoiceDocumentBuilderTests`: `regime_selects_document_type` (RF4), `unsupported_transaction_is_blocked` (RF5), `component_tax_reconciles` (RF2 incl. UT and odd paisa), `snapshot_survives_master_edits` (RF3: serialize, mutate shop/customer, rebuild nothing, bytes equal), buyer-detail and HSN rules; domain tests for GSTIN checksum (`27AAPFU0939F1ZV` valid, one-char change invalid), business date, fiscal year at 31 Mar 18:29/18:30 UTC, tax-profile validation. Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~InvoiceDocumentBuilderTests` → red.
- [ ] Implement Decisions 1–5 and 10. Re-run → green; domain tests green.

### Task 2: Unified numbering, posting and offline (backend)

**Files:**
- Create: `Application/Common/Interfaces/IInvoiceNumberAllocator.cs` (beside `ITenantWriteScope`: its refusals are Application errors), `Infrastructure/Services/Invoicing/InvoiceNumberAllocator.cs`, migration `AddIssuedInvoiceSnapshotsAndFiscalSeries`. The brief's separate `InvoiceSeries` entity is folded into `InvoiceSequence` (two unique indexes on one row) so leases keep their foreign key and there is one row to lock.
- Modify: `InvoiceSequence`, `InvoiceLease`, their configurations, `ShopConfiguration`, `CustomerConfiguration`, `SaleConfiguration` (jsonb converter), `InvoiceLeaseRepository`, `ReserveInvoiceLeaseCommandHandler`, `RecordSale{Command,CommandHandler,IdempotencyHasher}`, `SalesController` request, `SaleDto`/`SaleDtoBuilder`, `OfflineSalePoster`, `OfflineIssueTimingPolicy`, `ReconciliationIssueType` (+ `DocumentReview = 11`), `ReconciliationActionPolicy` (informational), DI registration.
- Tests: `RecordSaleCommandHandlerTests`, `ReserveInvoiceLeaseCommandHandlerTests`, `InvoiceLeaseReservationTests` (+ `online_and_offline_ranges_do_not_overlap`, `fiscal_rollover_uses_shop_business_date`), `SalesControllerTests` (numbered snapshot, interstate IGST, refused NotDeclared/composition with no posting, replay keeps number, snapshot unchanged after shop/customer edits), shop creation helpers in integration suites declare a Regular profile.

- [ ] Write tests (red), implement Decisions 6–9, regenerate the migration, inspect its SQL (backfill `seller_registration_key`, drop/replace the sequence index, defaults for existing shops), re-run unit + targeted integration suites → green.

### Task 3: Print, forms and exports (frontend + Tally)

**Files:** `sale.models.ts`, `sale-invoice-a4.component.{ts,html,spec.ts}`, `sale-invoice-thermal.component.{ts,html,spec.ts}`, print page, shop create overlay + manage-shop tax profile form, `shop.service.ts`, customer add/edit overlays + `customer.service.ts`, sale error mapping, offline-reconciliation issue label, `public/assets/i18n/*.json` (all 9 locales), `TallyVoucherBuilder.cs` + export dataset.

- [ ] Specs first: A4/thermal render each document type from the snapshot (seller identity from the snapshot even when `shop` differs — RF3), component summary, place of supply, declarations, signatory; legacy sale still renders from `shop`; taxable column label; paid/part-paid/unpaid; redeemed credit-note codes. Shop and customer form specs for the new fields.
- [ ] Implement; `bun run test --run src/app/features/sales/components`, shops, customers, i18n coverage; `bun run build`.
- [ ] Tally: component ledgers from the snapshot; unit tests for intrastate/interstate/legacy vouchers.

### Task 4: Docs, review, PR

- [ ] `docs/backend-architecture.md` § Issued Invoice Documents and Fiscal Numbering; capability matrix for accountant sign-off in `docs/gst-invoice-capability-matrix.md`; CLAUDE.md endpoints/infrastructure/test counts.
- [ ] Full backend + frontend suites; code review; fix findings; PR. Accountant acceptance of the matrix is a release gate recorded in the PR, not claimed here.

## Rollout, rollback, acceptance

Migration expands only (new nullable/defaulted columns, replaced sequence index); old numbers and leases keep working until they expire. Existing shops must declare a tax profile before their next sale or lease (the UI points owners to shop settings). Rolling back the API leaves snapshots as readable data; the migration's `Down` fails once a shop has opened a fiscal series beside its legacy counter, so roll forward. E-invoice (IRN), dynamic B2C QR, exports/SEZ, reverse charge, cess, per-item UOM, device-side GST printing and credit-note documents (backlog 17) remain out of scope.
