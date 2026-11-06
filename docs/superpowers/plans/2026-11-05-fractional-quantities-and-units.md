# Fractional quantities and units Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support weighted and measured retail quantities (0.25 kg, 1.5 m) from intake through sale, return, offline and print, and refuse fractions of whole-unit items instead of truncating them.

**Architecture:** Every item owns a quantity precision (`QuantityScale` 0–3) and, when its label is a known unit, a normalized `UnitCode`; a shared `QuantityPolicy` accepts a quantity only when it is positive and fits that scale, never rounding. Every movement path (sale, offline sync, return, intake, count, adjustment) checks the item's policy; the cart sets a line's quantity in one operation instead of ±1 loops. Issued documents print the unit's GST UQC instead of a fixed `NOS`.

**Tech Stack:** ASP.NET Core 10, Wolverine, EF Core/PostgreSQL, ErrorOr, FluentValidation, xUnit/Testcontainers; Angular 21 (signals, PrimeNG, Transloco), IndexedDB, Bun, Vitest, Playwright (mock UI audit).

**Spec:** backlog item 24 (the pasted "Fractional quantities and units" plan) and [market-readiness review](../../reviews/2026-10-21-market-readiness.md) § POS, [WEB-08](../../reviews/2026-10-21-market-readiness-evidence/web-workflows.md).

## Analysis (reverified on `main` at `6495bbfdb`, 2026-11-05)

What the spec assumed vs. what the code does:

| Area | Finding | Consequence for this plan |
|---|---|---|
| Storage | Every stock/sale/return quantity column is `numeric(18,3)`. | Keep scale 3 as the storage ceiling; no conversion of existing data. |
| Item | `items.uom` is free text (dev data: `PCS`, `PKT`, `KG`, `kg`), no precision. | Add `unit_code`, `quantity_scale`, `unit_review_required`; map known labels deterministically. |
| Server sale | `SaleLineValidator` loads the `Item` but never checks precision; services accept any decimal (Npgsql would silently round a 4th decimal). | Precision check per goods line (item scale) and per service line (≤ 3). |
| Offline sync | `OfflineSalePoster` runs the same `SaleLineValidator` (`allowInsufficientStock: true`); arithmetic v1 already uses thousandths. | Online and sync refuse over-precision identically; no arithmetic change. |
| Returns | `SaleReturnValidator` never loads the item; compares decimals exactly. | Load items; effective scale = max(item scale, scale of the sold quantity) so legacy fractional lines stay returnable. |
| Intake | `AddInventoryCommandHandler` and `InboundInventoryLineProcessor` each find-or-create the item and ignore the request's `uom` for an existing item. | Check the resolved item's policy in both, before the batch is built. |
| Count / adjustment | Count allows 3 decimals; adjustment validator allows only **2** while its domain allows 3. | Both check the item's scale; adjustment moves to 3. |
| Purchase orders | PO line quantities are `int` end to end (DB, API, DTOs, mobile). | **Out of scope** (backlog 30): POs stay whole-unit; receipt still passes the item policy. |
| Documents | `InvoiceDocumentBuilder` and the device builder hard-code `NOS` for goods. | Per-item document unit (UQC), pinned by the shared contract. |
| POS web | Cart is a signal service (`SaleCartStateService`), not NgRx. Integer assumptions: `Math.trunc` + ±1 loops (`new-sale-page.cart-selection.service.ts:37`), `Math.trunc` in `cart-submit:18`, `search-customer:60,65`, `setServiceQuantity:331`, picker `min=1`, cart minus disabled at `<= 1`. `AvailableBatchDto`/`SellableDto` carry no unit. | One `setQuantity`, a shared quantity field, unit + scale on sellables, drafts and offline lines. |
| Offline web | Calculator already scales quantity ×1000; snapshot batches already carry `uom`; finalization/shadow stock add floats. | Snapshot gains optional `quantityScale`/`documentUnit` (absent ⇒ whole units, `NOS`); normalize float sums to 3 places. |

## Global Constraints

- Angular web and .NET API only; mobile is excluded, so every API change is additive (new response fields, optional request fields) and existing labels keep working.
- Domain ← Application ← Infrastructure ← Api; repository abstractions and `IUnitOfWork` writes; ErrorOr, no exception-driven business control flow; C# warnings are errors.
- Never round, truncate or clamp a quantity a person entered; reject it with an error that names the unit and the allowed decimals.
- Storage scale stays 3; no migration changes an existing quantity.
- Frontend uses Bun only; migrations run separately from API startup; new i18n keys go into all 9 locale files.
- Commit messages carry no AI attribution (repository rule).

## Review Focus

- RF1 0.25 kg / 1.5 m: sale decrements stock exactly, money uses the v1/online rounding, document prints `KGS`/`MTR`, return restores stock exactly — Task 7 integration test.
- RF2 0.5 of a whole-unit item: explicit `*.QuantityPrecisionExceeded` from sale, sync, intake, count, adjustment; the cart shows the error and keeps the line unchanged — Tasks 1, 4, 6.
- RF3 three-decimal boundary: 0.001 kg accepted, 0.0001 kg refused; `2.000` counts as a whole number (trailing zeros from `numeric(18,3)` are not precision) — Tasks 1, 5.
- RF4 input: `1,5` in en-IN, `NaN`, `Infinity`, `1e3`, empty, `0`, `-1`, pasted `0.25` — Task 5.
- RF5 history: kg→pcs on an item with movements is refused; relabel `kg`→`KGS` is allowed; a legacy item flagged for review may be classified to a scale that covers its on-hand stock; an old offline snapshot without `quantityScale` sells whole units only — Tasks 1, 3, 6.
- Float noise: adding 0.1 then 0.2 of one batch is 0.3 and still fits scale 1 — Task 6.

## Units catalog (v1)

Pinned by NEW `tests/contracts/quantity-units-v1.json` (`{ "contract": "quantity-units/1", "maxScale": 3, "customDocumentUnit": { "whole": "NOS", "fractional": "OTH" }, "units": [{ "code", "scale", "documentUnit", "aliases": [] }] }`). Matching is `trim` + invariant upper-case of the label against `aliases`.

| code | scale | documentUnit | aliases |
|---|---|---|---|
| PCS | 0 | NOS | PCS, PC, PIECE, PIECES, NOS, NO, NUMBER, NUMBERS, UNIT, UNITS, EA, EACH |
| PKT | 0 | PAC | PKT, PACK, PACKS, PACKET, PACKETS, PAC |
| BOX | 0 | BOX | BOX, BOXES |
| DOZ | 0 | DOZ | DOZ, DOZEN, DOZENS |
| PAIR | 0 | PRS | PAIR, PAIRS, PRS |
| SET | 0 | SET | SET, SETS |
| BTL | 0 | BTL | BTL, BOTTLE, BOTTLES |
| BAG | 0 | BAG | BAG, BAGS |
| KG | 3 | KGS | KG, KGS, KILO, KILOS, KILOGRAM, KILOGRAMS |
| G | 0 | GMS | G, GM, GMS, GRAM, GRAMS |
| L | 3 | LTR | L, LTR, LTRS, LITRE, LITRES, LITER, LITERS |
| ML | 0 | MLT | ML, MLT, MILLILITRE, MILLILITRES, MILLILITER, MILLILITERS |
| M | 3 | MTR | M, MTR, MTRS, METRE, METRES, METER, METERS |
| CM | 0 | CMS | CM, CMS, CENTIMETRE, CENTIMETRES, CENTIMETER, CENTIMETERS |

A custom (unrecognized) label has `UnitCode = null`, the merchant's `QuantityScale` (default 0), and document unit `NOS` (scale 0) or `OTH` (scale > 0). Pieces keep `NOS`, so existing documents and contract cases do not change.

---

### Task 1: Domain quantity policy, units catalog and item unit rules

**Files:**
- Create: `src/backend/Intelibill.Domain/ValueObjects/QuantityUnits.cs`, `src/backend/Intelibill.Domain/ValueObjects/QuantityPolicy.cs`, `src/backend/Intelibill.Domain/ValueObjects/ItemStockHistory.cs`, `tests/contracts/quantity-units-v1.json`
- Modify: `src/backend/Intelibill.Domain/Entities/Item.cs`, `src/backend/Intelibill.Domain/Common/Errors.cs`
- Test: `tests/backend/unit/Intelibill.Domain.Unit.Tests/ValueObjects/QuantityPolicyTests.cs`, `.../ValueObjects/QuantityUnitsContractTests.cs`, `.../Entities/ItemUnitTests.cs`

**Interfaces (produces):**
- `public sealed record QuantityUnit(string Code, int Scale, string DocumentUnit, IReadOnlyList<string> Aliases)`
- `public static class QuantityUnits { const int MaxScale = 3; IReadOnlyList<QuantityUnit> All; QuantityUnit? Resolve(string? label); string DocumentUnitFor(string? unitCode, int scale); }`
- `public static class QuantityPolicy { int ScaleOf(decimal value) /* significant places, trailing zeros ignored */; bool FitsScale(decimal value, int scale); ErrorOr<decimal> Validate(decimal value, int scale, string unitLabel); int ReturnScale(int? itemScale, decimal soldQuantity) /* max(itemScale ?? 0, ScaleOf(sold)) */ }`
- Domain errors: `Errors.Quantity.NotPositive(string unitLabel)` code `Quantity.NotPositive`; `Errors.Quantity.PrecisionExceeded(decimal value, string unitLabel, int scale)` code `Quantity.PrecisionExceeded` (description: whole units only, or "at most N decimal places"); `Errors.ItemUnit.ChangeWithHistory` code `Item.UnitChangeWithHistory` (Conflict).
- `public sealed record ItemStockHistory(bool HasMovements, int MaxOnHandScale) { static ItemStockHistory None }`
- `Item`: `string? UnitCode`, `int QuantityScale`, `bool UnitReviewRequired`, computed `string DocumentUnit`; `Item.Create(..., int? quantityScale = null)` resolves the unit (catalog scale wins; custom uses `quantityScale ?? 0`; out-of-range throws `ArgumentOutOfRangeException` as a guard — validators prevent it); `ErrorOr<Success> ChangeUnit(string uom, int? quantityScale, ItemStockHistory history)`; `Item.Update` no longer takes `uom`.

`ChangeUnit` rules, in order: resolve target (catalog → its code/scale; custom → `quantityScale ?? current scale`). Same unit identity (same catalog code, or same custom label ignoring case) **and** same scale → relabel only. No movements → apply and clear review. `UnitReviewRequired` and target scale ≥ `history.MaxOnHandScale` → apply and clear review. Otherwise `Item.UnitChangeWithHistory`.

- [ ] **Step 1: Failing tests** — `QuantityPolicyTests`: `weighted_quantity_preserves_three_places` (0.25m, 1.5m, 0.001m valid at scale 3; 0.0001m → `Quantity.PrecisionExceeded`); `pieces_reject_fraction` (0.5m at scale 0 → `Quantity.PrecisionExceeded`, never 1); `trailing_zeros_are_not_precision` (`2.000m` valid at scale 0, `ScaleOf(0.250m) == 2`); `zero_and_negative_are_not_positive` (0m, -1m → `Quantity.NotPositive`); `return_scale_covers_legacy_fraction` (`ReturnScale(0, 0.5m) == 1`, `ReturnScale(null, 2m) == 0`). `QuantityUnitsContractTests`: the C# catalog equals `quantity-units-v1.json` (codes, scales, document units, aliases); `Resolve(" kg ")`→KG, `Resolve("Kgs")`→KG, `Resolve("BAGGIE")`→null; `DocumentUnitFor(null,0)=="NOS"`, `DocumentUnitFor(null,2)=="OTH"`. `ItemUnitTests`: `kg_item_has_scale_three_and_kgs_document_unit`; `custom_label_keeps_requested_scale`; `unit_change_with_history_is_rejected` (kg item, `HasMovements: true` → `pcs` fails with `Item.UnitChangeWithHistory`, `Uom` still `kg`); `relabel_same_unit_with_history_is_allowed` (kg→KGS); `review_item_can_be_classified_to_covering_scale` (review item scale 0, `MaxOnHandScale: 2` → `kg` ok and flag cleared; → `pcs` refused).
- [ ] **Step 2: Run** `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter "FullyQualifiedName~QuantityPolicyTests|FullyQualifiedName~QuantityUnitsContractTests|FullyQualifiedName~ItemUnitTests"` — expect compile failures.
- [ ] **Step 3: Implement** the interfaces above. The contract test reads the JSON by walking up from `AppContext.BaseDirectory` to the repo root, as `OfflineInvoiceDocumentContractTests` does.
- [ ] **Step 4: Run** the same command — PASS; then the whole Domain unit project.

### Task 2: Persistence, migration and stock-history query

**Files:**
- Modify: `src/backend/Intelibill.Infrastructure/Data/Configurations/ItemConfiguration.cs`, `src/backend/Intelibill.Domain/Interfaces/Repositories/IItemRepository.cs`, `src/backend/Intelibill.Infrastructure/Repositories/ItemRepository.cs`
- Create: migration `AddItemQuantityPrecision` (via `dotnet ef migrations add`), `scripts/db/backfill-item-quantity-precision.sql`
- Test: `tests/backend/integration/Intelibill.Integration.Tests/ItemQuantityPrecisionUpgradeTests.cs`

**Interfaces:** consumes Task 1. Produces columns `items.unit_code varchar(8) null`, `items.quantity_scale smallint not null default 0` with `ck_items_quantity_scale CHECK (quantity_scale BETWEEN 0 AND 3)`, `items.unit_review_required boolean not null default false`; `Task<ItemStockHistory> IItemRepository.GetStockHistoryAsync(Guid shopId, Guid itemId, CancellationToken ct)` (`HasMovements` = any batch incl. voided or any stock transaction; `MaxOnHandScale` = max `QuantityPolicy.ScaleOf` over non-voided batches with quantity > 0, computed in memory).

Backfill (same statements in the migration and the idempotent script, for items an old API instance creates during rollout): set `unit_code`/`quantity_scale` from a literal `VALUES (alias, code, scale)` list equal to the v1 catalog where `unit_code IS NULL AND upper(btrim(uom)) = alias`; set `unit_review_required = true` where `unit_code IS NULL` (migration only, not the script) or where `quantity_scale = 0` and any `inventory_batches`, `stock_transactions` or `sale_items` row of the item has `quantity <> trunc(quantity)`. No quantity column is touched.

- [ ] **Step 1: Failing test** `Upgrade_MapsKnownUnitsAndFlagsAmbiguousItems` following `InvoiceSeriesUpgradeTests` (own database, migrate to `20261103145848_AddIssuedInvoiceSnapshotsAndFiscalSeries`, seed): items `KG`→(KG,3,false), `kg`→(KG,3,false), `PCS`→(PCS,0,false), `BAGGIE`→(null,0,true), `PCS` with a 2.5 batch →(PCS,0,true); batch quantities unchanged after upgrade.
- [ ] **Step 2: Implement** configuration, generate the migration, hand-add the backfill `Sql`, add the script, implement `GetStockHistoryAsync`. Inspect the generated SQL; confirm the model snapshot diff only touches `items`.
- [ ] **Step 3: Run** `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~ItemQuantityPrecisionUpgradeTests|FullyQualifiedName~UpgradedSchemaRlsTests"` — PASS.

### Task 3: Item contracts carry the unit and precision

**Files:**
- Modify: `AddItem/{AddItemCommand,AddItemCommandValidator,AddItemCommandHandler}.cs`, `UpdateItem/{UpdateItemCommand,UpdateItemCommandValidator,UpdateItemCommandHandler}.cs`, `Items/DTOs/ItemDto.cs`, `Items/Queries/GetItems/{ItemCatalogReadModels,GetItemsQueryHandler}.cs` + `Infrastructure` catalog repository projection, `Common/Errors/Errors.Item.cs`, `Api/Controllers/ItemsController.cs`
- Test: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Items/...` (existing handler/validator test files)

**Interfaces:** `AddItemRequest`/`UpdateItemRequest`/commands gain trailing `int? QuantityScale = null`. `ItemDto` and `ItemCatalogReadModel` gain trailing `string? UnitCode, int QuantityScale, bool UnitReviewRequired`. Errors: `Item.QuantityScaleInvalid` (scale not 0–3, Validation), `Item.QuantityScaleMismatch` (scale given for a catalog unit and ≠ its scale, Validation), `Item.UnitChangeWithHistory` (Conflict, from Task 1).

- [ ] **Step 1: Failing tests** — validator: scale 4 → `Item.QuantityScaleInvalid`; `uom: "kg", quantityScale: 0` → `Item.QuantityScaleMismatch`; `uom: "Bundle", quantityScale: 2` valid. Add handler: `kg` returns `UnitCode "KG", QuantityScale 3`. Update handler: kg item with history → `pcs` returns `Item.UnitChangeWithHistory` and saves nothing; unchanged unit never calls `GetStockHistoryAsync`.
- [ ] **Step 2: Implement**; the update handler asks for history only when the resolved unit identity or scale differs.
- [ ] **Step 3: Run** `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter "FullyQualifiedName~Features.Items"` and the Api unit tests — PASS.

### Task 4: Every movement path enforces the item policy; sellables expose it

**Files:**
- Modify: `Sales/Services/SaleLineValidator.cs`, `Sales/Services/Returns/SaleReturnValidator.cs`, `Inventory/Commands/AddInventory/{AddInventoryCommandValidator,AddInventoryCommandHandler}.cs`, `Inventory/Services/InboundInventoryLineProcessor.cs`, `Inventory/Commands/CorrectStockCount/CorrectStockCountCommandHandler.cs`, `Inventory/Commands/CreateAdjustment/{CreateAdjustmentCommandValidator,CreateAdjustmentCommandHandler}.cs`, `Common/Errors/Errors.{Sale,Inventory}.cs`, DTOs `Inventory/DTOs/AvailableBatchDto.cs` + `GetAvailableBatchesQueryHandler`, `Sales/DTOs/SellableDto.cs` + `SearchSellablesQueryHandler`, `OfflineSalesSnapshot/DTOs/OfflineSalesSnapshotStreamRecords.cs` + `OfflineSalesSnapshotStreamingService`, `Sales/DTOs/SaleItemDto.cs` + `GetSaleDetailQueryHandler`
- Test: existing unit test files for each handler/validator.

**Interfaces:** Errors (Validation): `Sale.QuantityPrecisionExceeded(string barcode, string unit, int scale)`, `SaleReturn.QuantityPrecisionExceeded(string lineName, int scale)`, `Inventory.QuantityPrecisionExceeded(string unit, int scale)`. `AdjustmentQuantityScaleInvalid` now allows three places. DTO additions (trailing): `AvailableBatchDto(..., string Uom, int QuantityScale)`; `SellableDto(..., string? Uom = null, int? QuantityScale = null)` (goods only); `OfflineSellableBatchDto(..., int QuantityScale, string DocumentUnit)`; `SaleItemDto.Uom (string?)` and `SaleItemDto.QuantityScale (int)` = `QuantityPolicy.ReturnScale(item?.QuantityScale, Quantity)`.

Rules: goods sale line → `QuantityPolicy.Validate(q, item.QuantityScale, item.Uom)`; service line → fits scale 3. Sync uses the same validator, so an over-precise offline line fails exactly as online. Return line → fits `ReturnScale(item.QuantityScale, saleItem.Quantity)` (items loaded with `GetByIdsAsync`). Intake → fits 3 in the validator, item policy after the item is resolved (both handler paths). Count → counted quantity fits the item scale (zero allowed). Adjustment → item scale.

- [ ] **Step 1: Failing tests** — `SaleLineValidator`: 0.25 of a kg batch ok; 0.5 of a PCS batch → `Sale.QuantityPrecisionExceeded`; service 1.0005 → same code. `SyncOfflineSalesCommandHandler`: 0.5 PCS line is refused with that code. `SaleReturnValidator`: 0.1 of a 0.25 kg line ok; 0.5 of a PCS line → `SaleReturn.QuantityPrecisionExceeded`; 0.5 of a legacy 0.5-sold PCS line ok. Intake: 2.5 into an existing PCS item → `Inventory.QuantityPrecisionExceeded`; 2.5 kg creating a new item ok. Count 2.5 of PCS → refused; adjustment 0.125 kg → ok. Available batches / snapshot / sellables carry `uom`, `quantityScale` (and `documentUnit` in the snapshot).
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Run** `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests` and `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests` — PASS.

### Task 5: Issued documents print the unit (server + device, one contract)

**Files:**
- Modify: `Sales/Services/InvoiceDocumentBuilder.cs`, `RecordSale/RecordSaleCommandHandler.cs`, `SyncOfflineSales/OfflineSalePoster.cs`, `src/frontend/src/app/features/sales/services/offline-invoice-document.builder.ts`, `tests/contracts/offline-invoice-document-v2.json`
- Test: `OfflineInvoiceDocumentContractTests.cs`, `offline-invoice-document.builder.spec.ts`

**Interfaces:** `InvoiceIssueContext(..., IReadOnlyDictionary<Guid, string>? DocumentUnits = null)` keyed by item id; goods line uom = `DocumentUnits[itemId]` else `GoodsUom` (`NOS`). Both call sites pass `validatedLines.Where(goods).ToDictionary(l => l.Item!.Id, l => l.Item!.DocumentUnit)`. Device: the builder's goods line input gains `documentUnit?: string` (absent ⇒ `NOS`). Contract line input gains optional `"documentUnit"`.

- [ ] **Step 1: Failing contract case** `"weighted goods print their unit"`: regularKarnataka, one goods line `quantity 0.25, unitPrice 400, taxable 100, 5% → tax 5, total 105, documentUnit "KGS"`, expected `uom "KGS"`. Both contract tests feed `documentUnit` through.
- [ ] **Step 2: Implement** both builders; **Step 3: Run** `dotnet test ... --filter FullyQualifiedName~OfflineInvoiceDocumentContractTests` and `bun run test --run offline-invoice-document.builder` — PASS.

### Task 6: Quantity input, units catalog and cart setQuantity (web)

**Files:**
- Create: `src/frontend/src/app/shared/quantity/quantity-units.ts`, `quantity-input.ts`, `quantity-field.component.ts` (+ specs). (Shared rather than `features/sales/utils`: item forms and returns use it too.)
- Modify: `features/inventory/services/inventory.models.ts` (`AvailableBatchDto`, `Item`, requests), `features/sales/services/sale.models.ts` (`SellableGoodsDto`, `SaleItemDto`), `core/storage/sales-cart-indexeddb.service.ts`, `features/sales/services/sale-cart-state.service.ts`, `pages/new-sale-page.{cart-selection,cart-submit,search-customer,state,lifecycle}.service.ts`, `components/new-sale/cart-table.component.{ts,html,scss}`, `components/new-sale/batch-picker-dialog.component.{ts,html}`, offline: `services/offline-sale-state.service.ts`, `offline-sale-core.types.ts`, `offline-sale-finalization.service.ts`, `offline-sales-shadow-stock-indexeddb.service.ts`, `utils/offline-sales-snapshot.schema.ts`
- Test: colocated specs (cart-table, sale-cart-state, cart-selection, sales-cart-indexeddb, offline-sale-finalization, quantity-*).

**Interfaces:**
- `quantity-units.ts`: `interface QuantityUnitDefinition { code; scale; documentUnit; aliases }`, `QUANTITY_UNITS`, `QUANTITY_SCALE_MAX = 3`, `resolveQuantityUnit(label: string | null | undefined): QuantityUnitDefinition | null`; spec pins it to `tests/contracts/quantity-units-v1.json`.
- `quantity-input.ts`: `type QuantityParseResult = { ok: true; value: number } | { ok: false; error: 'empty' | 'invalid' | 'notPositive' | 'tooPrecise' }`; `parseQuantityInput(text: string, locale: string, scale: number)`; `quantityStep(scale)`; `fitsQuantityScale(value, scale)`; `normalizeQuantity(value, scale)` (only for results of arithmetic on on-scale values); `formatQuantity(value, scale, locale)`. Accepted syntax: digits with at most one decimal separator (the locale's, plus `.`), no grouping, sign, exponent, spaces or words; trailing zeros do not count as precision.
- `QuantityFieldComponent` (`app-quantity-field`): inputs `value`, `scale`, `max: number | null`, `unitLabel`, `inputId`, `ariaLabel`; output `committed: number`; text input `inputmode` decimal/numeric, commits on blur/Enter, shows translated error (`aria-invalid`, `aria-describedby`) and emits nothing on a bad value.
- Cart: `SalesCartDraftItem` + `uom: string; quantityScale: number; documentUnit: string` (restore defaults `''`, `0`, `'NOS'` without dropping the draft). `SaleCartStateService.setQuantity(lineKey: string, value: number): QuantityChangeResult` where `QuantityChangeResult = 'ok' | 'unchanged' | 'notFound' | 'notPositive' | 'tooPrecise' | 'exceedsStock'`; `canDecreaseCartItem(index)` = `quantity - 1 >= quantityStep(scale)`. ± buttons stay ±1 for every unit. `AvailableBatchDto` + `uom?`, `quantityScale?`, `documentUnit?` (absent ⇒ `''`, 0, `NOS`). Offline frozen line + `documentUnit`.

- [ ] **Step 1: Failing tests** — `parseQuantityInput`: `'0.25'`/kg→0.25; `'1,5'`/en-IN→invalid; `''`→empty; `'0'`,`'-1'`→notPositive; `'NaN'`,`'Infinity'`,`'1e3'`,`'1 000'`→invalid; `'0.5'`/scale 0→tooPrecise; `'0.0001'`/scale 3→tooPrecise; `'2.000'`/scale 0→2. Cart: `edit_quantity_sets_exact_fraction` (setQuantity 0.25 on kg line → 'ok', quantity 0.25, one draft write); `piece_increment_is_one`; `invalid_numeric_input_is_rejected` (0.5 on PCS → 'tooPrecise', unchanged); over-stock → 'exceedsStock'; `large_change_is_one_operation` (1→40 makes one state update); adding 0.1 then 0.2 of one batch → 0.3; legacy draft without scale restores as scale 0. Cart table emits `quantityChanged` once with the field's committed value. Offline: snapshot batch without `quantityScale` maps to scale 0; finalization sums 0.1+0.2 as 0.3.
- [ ] **Step 2: Implement.** **Step 3: Run** `bun run test --run src/app/shared/quantity src/app/features/sales src/app/core/storage` — PASS.

### Task 7: Item forms, returns, intake errors, i18n (web)

**Files:**
- Modify: `features/inventory/components/{add-product-overlay,edit-item-overlay}.component.{ts,html}`, `features/inventory/state/inventory.effects.ts` (error mapping), `features/inventory/components/inventory-table.component.html` (review badge), `features/sales/components/sale-detail/sale-return-preview-dialog.component.{ts,html}`, `public/assets/i18n/*.json` (9 locales)
- Test: overlay specs, return dialog spec, `i18n-coverage.spec.ts`.

**Behavior:** forms keep the free-text UOM; a precision select (whole, 0.1, 0.01, 0.001) is set from `resolveQuantityUnit` and disabled for catalog units; request sends `quantityScale` only for custom labels. Edit shows a review note when `unitReviewRequired`. Map `Item.UnitChangeWithHistory`, `Item.QuantityScaleInvalid`, `Item.QuantityScaleMismatch`, `Inventory.QuantityPrecisionExceeded`, `Sale.QuantityPrecisionExceeded`, `SaleReturn.QuantityPrecisionExceeded` to messages. Return quantity input uses `step = quantityStep(item.quantityScale)` and a `tooPrecise` validation message.

- [ ] **Step 1: Failing tests** — add overlay sends `quantityScale: null` for `kg` and `2` for custom `Bundle`/0.01; edit overlay shows the 409 message; return dialog flags 0.5 on a scale-0 line as too precise and accepts 0.1 on a scale-3 line.
- [ ] **Step 2: Implement** and add keys to all 9 locales. **Step 3: Run** `bun run test` (full) and `bun run build` — PASS.

### Task 8: End-to-end proof and documentation

**Files:**
- Create: `tests/backend/integration/Intelibill.Integration.Tests/FractionalQuantityTests.cs`, `src/frontend/tests/ui-audit/mock/new-sale-fractional-quantity.spec.ts`
- Modify: `docs/backend-architecture.md` (§ Quantity Precision), `docs/frontend-architecture.md` (§ Quantity Input), `AGENTS.md` (endpoints/infrastructure/domain lines)

- [ ] **Step 1:** `fractional_sale_return_reconciles_stock`: receive 10 kg, sell 0.25 (stock 9.75, document line `KGS` 0.25, totals from the server), return 0.10 (stock 9.85); 0.5 of a PCS item → 400 `Sale.QuantityPrecisionExceeded`; 0.0001 kg → 400; `PATCH` kg→pcs after the sale → 409 `Item.UnitChangeWithHistory`, sale document still `KGS`. Run `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~FractionalQuantityTests"`.
- [ ] **Step 2:** mock UI spec (reuse `new-sale-pos.fixture.ts`): kg line accepts typed 0.25 and shows the line total; PCS line refuses 0.5 with the error text and keeps 1. Run `bun run audit:ui:mocked tests/ui-audit/mock/new-sale-fractional-quantity.spec.ts` from `src/frontend` (mocked suite, `playwright.config.ts`).
- [ ] **Step 3:** docs; full `dotnet test src/backend/Intelibill.slnx` and `bun run test`, `bun run build`.

## Rollout and rollback

Additive migration (defaults keep old instances working); run `scripts/db/backfill-item-quantity-precision.sql` once after the new API is live, for items an old instance created meanwhile. Rollback = deploy the previous API: it ignores the new columns, fractional data stays valid in `numeric(18,3)`, and no quantity is downgraded. Purchase-order fractional quantities (int columns), carton↔piece conversion, scale integration and barcode-embedded weight are separate contracts (backlog 30/28) and are not advertised.
