# Rich Domain Model: Sale & SaleReturn Aggregates — Technical Requirements

## Problem Statement

Business logic for recording a sale and a sale return is scattered across shallow application-layer services (`SaleAggregator`, `SaleInventoryMutator`) and fat handlers. Domain entities are data bags with no invariant enforcement. Invariants (total = paid + due, credit requires due, notes required for wastage/partial/zero refunds, payout method required when payout > 0) live outside the aggregate that owns them.

---

## Decisions

| # | Question | Decision |
|---|----------|----------|
| 1 | Scope | Both `Sale.Record()` and `SaleReturn.Record()` in one refactor |
| 2 | Invoice / return number generation | Handler generates, passes in as parameter |
| 3 | Line item input type | New domain-level `SaleLineInput` record; tax calculation + `SaleItem` creation move inside `Sale.Record()` |
| 4 | `CustomerLedgerEntry` ownership | Handler creates it (cross-aggregate orchestration, not a Sale invariant) |
| 5 | `SaleInventoryMutator` fate | Inlined into handler (3 ops per line after stripping tax + SaleItem creation) |
| 6 | `SaleReturn` invariants scope | Notes required + payout method validation move into `SaleReturn.Record()`; role check stays in handler (authorization) |
| 7 | Test strategy | TDD — write failing domain tests first, then implement |

---

## New Domain Types

### `SaleLineInput` — `Intelibill.Domain/ValueObjects/SaleLineInput.cs`

```csharp
public record SaleLineInput(
    Guid ShopId,
    Guid ItemId,
    Guid InventoryBatchId,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    bool HasPriceMismatch);
```

### `SaleReturnLineInput` — `Intelibill.Domain/ValueObjects/SaleReturnLineInput.cs`

```csharp
public record SaleReturnLineInput(
    Guid ShopId,
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal OriginalCostPrice,
    decimal OriginalSalesPrice,
    decimal OriginalTaxRatePercent,
    bool OriginalIsPriceIncludingTax,
    decimal MaxRefundAmount,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    string? Notes);
```

---

## Domain Changes

### `Sale.cs` — add `Sale.Record()`

**Signature:**
```csharp
public static ErrorOr<Sale> Record(
    Guid shopId,
    string invoiceNumber,
    IReadOnlyList<SaleLineInput> lines,
    Guid? customerId,
    string? customerName,
    string? customerPhone,
    PaymentMethod paymentMethod,
    decimal paidAmount,
    decimal dueAmount,
    DateTimeOffset soldAt)
```

**Internal logic (moves from `SaleAggregator`):**
1. For each `SaleLineInput`, compute tax and create a `SaleItem` internally
2. Compute `totalAmount` — per line: `salesPrice × qty`, add tax if `!IsPriceIncludingTax`
3. Compute `totalTaxAmount` — tax formula:
   - Tax-inclusive: `qty × salesPrice × taxRate / (100 + taxRate)`
   - Tax-exclusive: `qty × salesPrice × taxRate / 100`
4. **Invariant:** `Round(totalAmount, 2) != Round(paidAmount + dueAmount, 2)` → `Errors.Sale.PaidAndDueAmountMismatch`
5. **Invariant:** `paymentMethod == Credit && dueAmount <= 0` → `Errors.Sale.CreditRequiresDueAmount`
6. Return constructed `Sale` with items

**`Sale.Create()` fate:** Remove the public 11-parameter static factory. EF Core uses the `private Sale() {}` parameterless constructor for materialization — no change needed there.

### `SaleReturn.cs` — add `SaleReturn.Record()`

**Signature:**
```csharp
public static ErrorOr<SaleReturn> Record(
    Guid shopId,
    Guid saleId,
    string returnNumber,
    DateTimeOffset processedAt,
    Guid actorUserId,
    string? notes,
    decimal totalRefundAmount,
    decimal dueReductionAmount,
    decimal payoutAmount,
    PaymentMethod? payoutMethod,
    decimal totalTaxableAmount,
    decimal totalTaxAmount,
    decimal customerBalanceBefore,
    decimal customerBalanceAfter,
    IReadOnlyList<SaleReturnLineInput> lines)
```

**Internal logic (moves from handler):**
1. **Invariant:** Per line, validate required notes:
   - `Condition == Wastage` and `Notes` is null/empty → `Errors.Sale.ReturnNoteRequired("wastage returns")`
   - `ApprovedRefundAmount < MaxRefundAmount` (partial) and no notes → `Errors.Sale.ReturnNoteRequired("partial refunds")`
   - `ApprovedRefundAmount == 0` and no notes → `Errors.Sale.ReturnNoteRequired("zero refunds")`
2. **Invariant:** Payout method validation:
   - `payoutAmount > 0` and no `payoutMethod` → `Errors.Sale.ReturnPayoutMethodRequired`
   - `payoutAmount > 0` and `payoutMethod` not in `{Cash, UPI, Card}` → `Errors.Sale.ReturnPayoutMethodInvalid`
3. Create `SaleReturnItem` list from `lines`
4. Construct and return `SaleReturn`

**`SaleReturn.Create()` fate:** Remove the public static factory, same as `Sale.Create()`.

### `SaleItem.cs` and `SaleReturnItem.cs`

Make `SaleItem.Create()` and `SaleReturnItem.Create()` `internal` — they are called only from within the domain (`Sale.Record()` and `SaleReturn.Record()` respectively), never from the application layer.

---

## Application Layer Changes

### `RecordSaleCommandHandler.cs` — simplified orchestration

**Constructor after refactor:**
`ISaleLineValidator`, `ICustomerResolver`, `ISaleRepository`, `ICustomerLedgerEntryRepository`, `IStockTransactionRepository`, `IInventoryRepository`, `IInventoryBatchRepository`, `IUnitOfWork`

**New flow:**
```
1. saleLineValidator.ValidateLinesAsync(...)
2. Generate invoiceNumber = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N[..8].ToUpper()}"
3. foreach validatedLine:
   a. batch.SubtractQuantity(qty, actorUserId)              → error → return
   b. inventory.SubtractQuantity(qty, actorUserId)          → error → return
   c. StockTransaction.Create(type: Out, ...)               → error → return
   d. stockTransactionRepository.AddAsync(transaction)
   e. Build SaleLineInput from (cmdItem, batch)
4. customerResolver.ResolveAsync(...)
5. Sale.Record(shopId, invoiceNumber, lineInputs, customerId, customerName,
               customerPhone, paymentMethod, paidAmount, dueAmount, soldAt)
6. saleRepository.AddAsync(sale)
7. if sale.DueAmount > 0 && sale.CustomerId.HasValue:
       CustomerLedgerEntry.Create(type: SaleDue, ...)
       customerLedgerEntryRepository.AddAsync(ledgerEntry)
8. unitOfWork.SaveChangesAsync()
9. Build and return SaleDto
```

### `RecordSaleReturnCommandHandler.cs` — simplified orchestration

**Constructor after refactor:** unchanged (role check + saleReturnValidator stay)

**New flow:**
```
1. saleReturnValidator.ValidateAsync(...)
2. Role check: Owner or Manager only (stays in handler — authorization)
3. returnNumber = saleReturnNumberGenerator.Generate(processedAt)
4. foreach line where Condition != Wastage:
   a. StockTransaction.Create(type: Ret, ...)               → error → return
   b. stockTransactionRepository.AddAsync(transaction)
   c. batch.AddQuantity(qty, actorUserId)                   → error → return
   d. inventory.AddQuantity(qty, actorUserId)               → error → return
   e. inventoryBatchRepository.Update(batch)
   f. inventoryRepository.Update(inventory)
   g. Build SaleReturnLineInput from (line, calculated)
5. SaleReturn.Record(shopId, saleId, returnNumber, processedAt, actorUserId,
                     notes, amounts..., lineInputs)
   → enforces notes invariants + payout method invariants
6. saleReturnRepository.AddAsync(saleReturn)
7. if sale.CustomerId && dueReductionAmount > 0:
       CustomerLedgerEntry.Create(type: ReturnCredit, ...)
       customerLedgerEntryRepository.AddAsync(ledgerEntry)
8. unitOfWork.SaveChangesAsync()
```

---

## Deleted Files

| File | Reason |
|------|--------|
| `Application/Sales/Services/SaleAggregator.cs` | Logic moves into `Sale.Record()` |
| `Application/Sales/Services/ISaleAggregator.cs` | Interface deleted with implementation |
| `Application/Sales/Services/SaleInventoryMutator.cs` | Inlined into handler |
| `Application/Sales/Services/ISaleInventoryMutator.cs` | Interface deleted with implementation |
| `Application/Sales/Commands/RecordSale/SaleAggregation.cs` (return type) | Handler builds DTO directly |

---

## Test Plan (TDD Order)

### Phase 1 — Write failing domain tests first

**`SaleTests.cs` — new `Sale.Record()` tests:**
- Total mismatch → `PaidAndDueAmountMismatch`
- Credit payment, `dueAmount == 0` → `CreditRequiresDueAmount`
- Tax-inclusive single line: `totalAmount` and `totalTaxAmount` computed correctly
- Tax-exclusive single line: `totalAmount` and `totalTaxAmount` computed correctly
- Valid cash sale, single line → returns `Sale` with correct totals and one `SaleItem`
- Valid credit sale with due → returns `Sale`
- Multi-line sale → totals are sum of all lines

**`SaleReturnTests.cs` — new `SaleReturn.Record()` tests:**
- Wastage line, no notes → `ReturnNoteRequired`
- Partial refund line, no notes → `ReturnNoteRequired`
- Zero refund line, no notes → `ReturnNoteRequired`
- `payoutAmount > 0`, no `payoutMethod` → `ReturnPayoutMethodRequired`
- `payoutAmount > 0`, `payoutMethod = Credit` → `ReturnPayoutMethodInvalid`
- Valid return, all notes present → returns `SaleReturn` with correct items

### Phase 2 — Implement domain methods until tests pass

Implement `Sale.Record()` and `SaleReturn.Record()`. Keep existing `Sale.Create()` and `SaleReturn.Create()` alive until handlers are updated.

### Phase 3 — Update handler tests and implementations

Update `RecordSaleCommandHandler` and `RecordSaleReturnCommandHandler` to use the new domain methods. Update handler unit tests to remove `ISaleAggregator` / `ISaleInventoryMutator` mocks.

### Phase 4 — Delete obsolete code and tests

- Remove `SaleAggregator`, `ISaleAggregator`, `SaleInventoryMutator`, `ISaleInventoryMutator`, `SaleAggregation`
- Remove public `Sale.Create()` and `SaleReturn.Create()`
- Delete or prune `SalesServicesTests.cs` sections covering deleted services

---

## Files Touched (Summary)

| File | Change |
|------|--------|
| `Domain/Entities/Sale.cs` | Add `Sale.Record()`, remove public `Sale.Create()` |
| `Domain/Entities/SaleReturn.cs` | Add `SaleReturn.Record()`, remove public `SaleReturn.Create()` |
| `Domain/Entities/SaleItem.cs` | Make `SaleItem.Create()` `internal` |
| `Domain/Entities/SaleReturnItem.cs` | Make `SaleReturnItem.Create()` `internal` |
| `Domain/ValueObjects/SaleLineInput.cs` | **New** |
| `Domain/ValueObjects/SaleReturnLineInput.cs` | **New** |
| `Application/.../RecordSaleCommandHandler.cs` | Simplified orchestration, inline mutation loop |
| `Application/.../RecordSaleReturnCommandHandler.cs` | Simplified orchestration, inline restock loop |
| `Application/Sales/Services/SaleAggregator.cs` | **Delete** |
| `Application/Sales/Services/ISaleAggregator.cs` | **Delete** |
| `Application/Sales/Services/SaleInventoryMutator.cs` | **Delete** |
| `Application/Sales/Services/ISaleInventoryMutator.cs` | **Delete** |
| `Domain.Unit.Tests/Entities/SaleTests.cs` | Add `Sale.Record()` invariant tests |
| `Domain.Unit.Tests/Entities/SaleReturnTests.cs` | Add `SaleReturn.Record()` invariant tests |
| `Application.Unit.Tests/.../RecordSaleCommandHandlerTests.cs` | Update mocks, remove service mocks |
| `Application.Unit.Tests/.../RecordSaleReturnCommandHandlerTests.cs` | Remove inline invariant tests |
| `Application.Unit.Tests/Sales/Services/SalesServicesTests.cs` | Delete aggregator + mutator sections |

---

## Non-Goals

- No API contract changes (`POST /api/sales`, `POST /api/sales/{id}/returns` unchanged)
- No database schema or migration changes
- No changes to `SaleLineValidator`, `CustomerResolver`, `SaleReturnValidator`, `SaleReturnNumberGenerator`, `SaleReturnCalculator`
- No domain events pattern introduced
