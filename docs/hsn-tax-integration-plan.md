# HSN Tax Integration Plan

## Overview
When adding inventory, after the user completes the item name field (on blur), look up HSN codes and tax scenarios from a local DB cache first, falling back to an external HSN service. Auto-apply if result is unambiguous (1 HSN + 1 scenario). Otherwise show a picker. Selected HSN code is stored on the `Item` entity; tax rate continues to live on `InventoryBatch` as today. No changes to tax calculation logic.

## TDD Rule
Write the failing test first. Watch it fail. Then write minimal code to pass. No production code without a failing test first.

---

## Backend

### Step 1 — Unit Tests: `Item` entity (HsnCode)
**Test file**: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/ItemTests.cs`
- [ ] `Create_WithHsnCode_SetsHsnCode`
- [ ] `Create_WithNullHsnCode_SetsHsnCodeToNull`
- [ ] `Update_WithNewHsnCode_UpdatesHsnCode`
- [ ] `Update_WithNullHsnCode_ClearsHsnCode`

### Step 2 — Implement: `Item` entity
**File**: `src/backend/Intelibill.Domain/Entities/Item.cs`
- [ ] Add `public string? HsnCode { get; private set; }`
- [ ] Add `hsnCode` param to `Create()` and `Update()`

---

### Step 3 — Unit Tests: `HsnCache` entity
**Test file**: `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/HsnCacheTests.cs`
- [ ] `Create_WithValidData_SetsAllProperties`
- [ ] `Create_WithMultipleHsnCodes_StoresAll`
- [ ] `Create_WithMultipleTaxScenarios_StoresAll`
- [ ] `Create_SetsCachedAtToNow`

### Step 4 — Implement: `HsnCache` entity
**New file**: `src/backend/Intelibill.Domain/Entities/HsnCache.cs`
- [ ] Create `HsnCache : BaseEntity` with:
  - `string ProductName`
  - `List<string> HsnCodes`
  - `List<HsnTaxScenario> TaxScenarios`
  - `DateTime CachedAt`
- [ ] Create `HsnTaxScenario` (owned, no BaseEntity):
  - `string Condition`
  - `string TaxPercentage`

---

### Step 5 — Unit Tests: `LookupHsnHandler`
**Test file**: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Hsn/LookupHsnHandlerTests.cs`
- [ ] `Handle_CacheHit_ReturnsCachedResultWithoutCallingApi`
- [ ] `Handle_CacheMiss_CallsExternalApiAndReturnsResult`
- [ ] `Handle_CacheMiss_SavesResultToCache`
- [ ] `Handle_CacheMiss_ApiReturnsMultipleHsnCodes_ReturnsAll`
- [ ] `Handle_CacheMiss_ApiReturnsMultipleTaxScenarios_ReturnsAll`
- [ ] `Handle_ApiError_ReturnsError`
- [ ] `Handle_ApiReturnsSuccessFalse_ReturnsError`
- [ ] `Handle_EmptyProductName_ReturnsValidationError`

### Step 6 — Implement: HSN Lookup Query
**New files** in `src/backend/Intelibill.Application/Features/Hsn/Queries/LookupHsn/`:
- [ ] `LookupHsnQuery.cs` — `record LookupHsnQuery(string ProductName)`
- [ ] `LookupHsnResult.cs`:
  ```csharp
  record LookupHsnResult(string[] HsnCodes, HsnTaxScenarioResult[] TaxScenarios);
  record HsnTaxScenarioResult(string Condition, string TaxPercentage);
  ```
- [ ] `LookupHsnHandler.cs` — logic:
  1. Check `IHsnCacheRepository.GetByProductNameAsync(productName)`
  2. Cache hit → map to `LookupHsnResult`, return
  3. Cache miss → call external API via `IHttpClientFactory.CreateClient("HsnService")`
  4. Map response → `HsnCache` entity → `SaveAsync(...)` to DB
  5. Return `LookupHsnResult`
  - Returns `ErrorOr<LookupHsnResult>`

---

### Step 7 — Unit Tests: `AddInventoryBatchHandler` (HsnCode)
**Test file**: `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Inventory/AddInventoryBatchHandlerTests.cs`
*(add to existing)*
- [ ] `Handle_WithHsnCode_UpdatesItemHsnCode`
- [ ] `Handle_WithNullHsnCode_DoesNotUpdateItemHsnCode`

### Step 8 — Implement: `AddInventoryBatch` — store HsnCode on Item
**Edit**: `AddInventoryBatchCommand.cs`
- [ ] Add `string? HsnCode` to `AddInventoryBatchRowCommand`

**Edit**: `AddInventoryBatchHandler.cs`
- [ ] If `HsnCode` provided → call `item.Update(...)` with new HSN code

---

### Step 9 — Integration Tests: `POST /api/hsn/lookup`
**Test file**: `tests/backend/integration/Intelibill.Integration.Tests/Features/Hsn/HsnLookupTests.cs`
- [ ] `LookupHsn_Unauthenticated_Returns401`
- [ ] `LookupHsn_CacheMiss_CallsExternalApiAndReturnsResult`
- [ ] `LookupHsn_CacheMiss_SavesResultToDatabase`
- [ ] `LookupHsn_CacheHit_ReturnsFromDatabaseWithoutCallingApi`
- [ ] `LookupHsn_EmptyProductName_Returns400`
- [ ] `LookupHsn_MultipleHsnCodes_ReturnsAll`
- [ ] `LookupHsn_MultipleTaxScenarios_ReturnsAll`

### Step 10 — Integration Tests: `AddInventoryBatch` stores HsnCode
**Test file**: `tests/backend/integration/Intelibill.Integration.Tests/Features/Inventory/AddInventoryBatchTests.cs`
*(add to existing)*
- [ ] `AddBatch_WithHsnCode_StoresHsnCodeOnItem`
- [ ] `AddBatch_WithNullHsnCode_LeavesItemHsnCodeUnchanged`
- [ ] `AddBatch_WithHsnCode_TaxRateStillStoredOnBatch`

### Step 11 — Implement: Infrastructure + Api
**New file**: `src/backend/Intelibill.Infrastructure/Options/HsnServiceOptions.cs`
- [ ] `SectionName = "HsnService"`, `BaseUrl`, `LookupPath`, `ApiKey` — same pattern as `ProductLookupOptions`

**New file**: `src/backend/Intelibill.Infrastructure/Data/Configurations/HsnCacheConfiguration.cs`
- [ ] Map `HsnCodes` → `HasColumnType("text[]")`
- [ ] Map `TaxScenarios` → `OwnsMany(...).ToJson()` (jsonb)
- [ ] Add index on `ProductName`

**Edit**: `src/backend/Intelibill.Infrastructure/Data/Configurations/ItemConfiguration.cs`
- [ ] Map `HsnCode` → nullable, `HasMaxLength(20)`

**Edit**: `src/backend/Intelibill.Infrastructure/Data/AppDbContext.cs`
- [ ] Add `DbSet<HsnCache> HsnCache`

**New file**: `src/backend/Intelibill.Infrastructure/Repositories/HsnCacheRepository.cs`
- [ ] `GetByProductNameAsync(string name)` — case-insensitive `ILIKE`
- [ ] `SaveAsync(HsnCache entry)`

**Edit**: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs`
- [ ] `AddOptions<HsnServiceOptions>().Bind(...)`
- [ ] `AddHttpClient("HsnService", ...)` — set `BaseAddress` + `X-Api-Key` header
- [ ] Register `IHsnCacheRepository` → `HsnCacheRepository` (scoped)

**Edit**: `src/backend/Intelibill.Api/appsettings.Development.json`
```json
"HsnService": {
  "BaseUrl": "https://localhost:5001",
  "LookupPath": "/api/Hsn",
  "ApiKey": "test-api-key-12345"
}
```
> Production: env vars / secrets manager — `appsettings.json` stays empty

**New file**: `src/backend/Intelibill.Api/Controllers/HsnController.cs`
- [ ] `POST /api/hsn/lookup` — requires JWT auth, body `{ "productName": "..." }`, returns `LookupHsnResult`

**Run**:
- [ ] `dotnet ef migrations add AddHsnCacheAndItemHsnCode --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`

---

### Step 12 — Implement: GetProductDetails — return HsnCode
**Edit**: `ProductDetailsDto` / product details query response
- [ ] Add `string? HsnCode`

---

## Frontend (Angular)

### Step 13 — Tests: `inventory.service.ts`
**Test file**: `src/frontend/src/app/features/inventory/services/inventory.service.spec.ts`
*(add to existing)*
- [ ] `lookupHsn_CallsCorrectEndpointWithProductName`
- [ ] `lookupHsn_ReturnsHsnCodesAndTaxScenarios`
- [ ] `addBatchRow_IncludesHsnCodeInPayload`

### Step 14 — Implement: `inventory.service.ts`
- [ ] Add `HsnTaxScenario` and `HsnLookupResult` interfaces
- [ ] Add `lookupHsn(productName: string): Observable<HsnLookupResult>` → `POST /api/hsn/lookup`
- [ ] Add `hsnCode?: string` to `AddInventoryBatchRowRequest`
- [ ] Add `hsnCode: string | null` to `ProductDetailsDto`

---

### Step 15 — Tests: `inventory-batch-page.component`
**Test file**: `src/frontend/src/app/features/inventory/pages/inventory-batch-page.component.spec.ts`
*(add to existing)*
- [ ] `onItemNameBlur_WithName_CallsLookupHsn`
- [ ] `onItemNameBlur_NameLessThan3Chars_DoesNotCallLookupHsn`
- [ ] `onItemNameBlur_SingleHsnAndSingleScenario_AutoAppliesWithoutShowingPicker`
- [ ] `onItemNameBlur_SingleHsnAndSingleScenario_ShowsChip`
- [ ] `onItemNameBlur_MultipleResults_ShowsPickerCard`
- [ ] `onItemNameBlur_ApiError_DoesNotShowPicker`
- [ ] `onItemNameBlur_ApiError_TaxFieldRemainsEditable`
- [ ] `applyHsnSelection_ParsesTaxPercentageStringToNumber`
- [ ] `applyHsnSelection_PatchesTaxRatePercentFormControl`
- [ ] `applyHsnSelection_SetsSelectedHsnCode`
- [ ] `applyHsnSelection_ClosesPicker`
- [ ] `itemNameChange_AfterApply_ClearsChipAndResetsState`
- [ ] `existingItemWithHsnCode_PrePopulatesChip`
- [ ] `submitForm_IncludesHsnCodeInPayload`
- [ ] `dismissPicker_KeepsTaxFieldEditable`

### Step 16 — Implement: `inventory-batch-page.component.ts`
- [ ] Add state: `hsnResult`, `isLoadingHsn`, `selectedHsnCode`, `pickerOpen`
- [ ] `onItemNameBlur()` — skip if < 3 chars, call `lookupHsn()`, auto-apply or open picker
- [ ] `applyHsnSelection(hsnCode, taxPercentage)` — parse `"18%"` → `18`, patch form, close picker
- [ ] Item name change after apply → clear chip + reset state
- [ ] Existing item with `hsnCode` → pre-populate chip
- [ ] Include `hsnCode` in submit payload

### Step 17 — Implement: `inventory-batch-page.component.html`
- [ ] Add `(blur)="onItemNameBlur()"` to item name field
- [ ] HSN chip below item name: `HSN: 30049069 · 5%` with `change` link
- [ ] Picker card (two-column: HSN codes left, tax scenarios right)
- [ ] `Apply` button + `Enter tax % manually` dismiss link

### Step 18 — i18n locale files
**Files**: `src/frontend/public/assets/i18n/*.json`
- [ ] `hsn.suggestion`, `hsn.hsnCode`, `hsn.taxScenario`, `hsn.apply`
- [ ] `hsn.enterManually`, `hsn.chip`, `hsn.change`, `hsn.loading`

---

## Trigger Logic

| API Result | Behaviour |
|---|---|
| 1 HSN + 1 scenario | Auto-apply, show chip |
| Multiple HSN or scenarios | Show picker card |
| API error / timeout | Silent — tax field stays manual |
| 0 results | Silent — tax field stays manual |

## What Does NOT Change
- `InventoryBatch.TaxRatePercent` — same field, same storage
- `GetTaxAmountPerUnit()` — same calculation
- `TaxIncluded` flag — user still toggles
- Sales, invoices, profit-loss — unchanged

---

## File Summary

### Production Files
| # | File | Action |
|---|---|---|
| 1 | `Domain/Entities/Item.cs` | Edit |
| 2 | `Domain/Entities/HsnCache.cs` | New |
| 3 | `Infrastructure/Data/Configurations/HsnCacheConfiguration.cs` | New |
| 4 | `Infrastructure/Data/Configurations/ItemConfiguration.cs` | Edit |
| 5 | `Infrastructure/Data/AppDbContext.cs` | Edit |
| 6 | `Infrastructure/Options/HsnServiceOptions.cs` | New |
| 7 | `Infrastructure/Repositories/HsnCacheRepository.cs` | New |
| 8 | `Infrastructure/DependencyInjection.cs` | Edit |
| 9 | `Api/appsettings.Development.json` | Edit |
| 10 | `Application/Features/Hsn/Queries/LookupHsn/LookupHsnQuery.cs` | New |
| 11 | `Application/Features/Hsn/Queries/LookupHsn/LookupHsnResult.cs` | New |
| 12 | `Application/Features/Hsn/Queries/LookupHsn/LookupHsnHandler.cs` | New |
| 13 | `Api/Controllers/HsnController.cs` | New |
| 14 | `Application/.../AddInventoryBatchCommand.cs` | Edit |
| 15 | `Application/.../AddInventoryBatchHandler.cs` | Edit |
| 16 | `ProductDetailsDto` + query | Edit |
| 17 | `inventory.service.ts` | Edit |
| 18 | `inventory-batch-page.component.ts` | Edit |
| 19 | `inventory-batch-page.component.html` | Edit |
| 20 | `i18n/*.json` | Edit |

**7 new · 13 edits · 1 migration**

### Test Files
| # | File | Action |
|---|---|---|
| T1 | `unit/.../Entities/ItemTests.cs` | Edit |
| T2 | `unit/.../Entities/HsnCacheTests.cs` | New |
| T3 | `unit/.../Features/Hsn/LookupHsnHandlerTests.cs` | New |
| T4 | `unit/.../Features/Inventory/AddInventoryBatchHandlerTests.cs` | Edit |
| T5 | `integration/.../Features/Hsn/HsnLookupTests.cs` | New |
| T6 | `integration/.../Features/Inventory/AddInventoryBatchTests.cs` | Edit |
| T7 | `frontend/inventory.service.spec.ts` | Edit |
| T8 | `frontend/inventory-batch-page.component.spec.ts` | Edit |

**3 new test files · 5 edits to existing test files**
