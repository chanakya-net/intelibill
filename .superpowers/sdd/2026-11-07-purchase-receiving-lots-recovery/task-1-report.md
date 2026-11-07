# Task 1 implementation report

Base: `342d8c8c`. Branch: `codex/purchase-receiving-lots-recovery`.
Scope: backend atomic multi-lot purchase receiving, stable keyed replay/lookup, client lineage, and additive migration. No frontend or mobile implementation.

## Result

- Appended optional `operationId` and per-lot `clientLineId` without changing existing positional parameters or response type.
- Active Owner/Manager authorization precedes execution/replay. Keyed receiving uses `ExecuteAsync<PurchaseOrderDetailDto>` with operation type 13, canonical UUID key, target PO, and canonical business-payload hash. The existing executor owns the transaction and stores the DTO itself, not an `ErrorOr` wrapper. Unkeyed callers retain an explicit transaction around the same body; refusal/exception rolls back and clears tracked changes.
- New postings fence the period before locking the order and lines. Full scalar, line ownership, item ownership, quantity-policy, grouped remaining (wide `long` sum), duplicate client-ID, and trimmed physical-lot validation precedes every inbound call. Repeated PO lines and product barcodes are allowed; physical lot comparison remains ordinal/case sensitive. Inbound processing stages every lot, links receipt values, applies PO quantities, and saves once.
- Handler failures retain stable codes and whitelisted client identity/index/field through additive receiving-only `rowErrors`. Existing validation paths remain available for client normalization.
- Receipt lines persist the supplied client identity, or their own entity ID when omitted. Mapper falls back to entity ID for nullable legacy writes.
- Registered `purchase-order-receipt` lookup through the existing operation store/controller/DTO. Replay skips current status/remaining/period eligibility and returns the saved original PO detail.

## Necessary shared fixes discovered by real database tests

1. `InventoryUpdater` reused an unsaved aggregate across lots, then called `DbSet.Update` for the next lot. That changed its Added state into Modified and produced `Concurrency.Conflict` because the row did not yet exist. `InventoryUpdateContext` now records newly added item IDs; later quantities mutate those tracked aggregates without changing their Added state. Existing persisted aggregates still use the existing update path. No global repository semantics or transaction boundaries changed.
2. The operation executor's default JSON options serialize enums numerically, while the API uses string enum names. A `JsonStringEnumConverter<PurchaseOrderStatus>` on the PO detail DTO's Status property makes the saved response, POST, replay and lookup structurally identical. The shared executor, hash version and unrelated operation payloads are unchanged.

## TDD and verification evidence

Initial behavior red runs:

- `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~ReceivePurchaseOrder`: four expected behavior failures: `SamePurchaseOrderLine_WithDistinctLotsAndRepeatedBarcode_IsValid`, `ExplicitEmptyClientIdentity_IsRejected`, `DuplicateExplicitClientIdentity_IsRejected`, and `GroupedOverRemaining_RejectsEveryLotBeforeProcessing`. Log: `/tmp/task1-app-red.log`.
- `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter FullyQualifiedName~PurchaseOrdersControllerTests`: expected `ReceivePurchaseOrder_ProjectsOnlyWhitelistedRowErrorMetadata` failure; existing 22 passed. Log: `/tmp/task1-api-red.log`.
- Canonical hash red: `EquivalentTrimmedTextDecimalScaleAndUtcInstant_HashAlike` and `OmittedReceivedAt_IsStableAndTransportIdentityIsExcluded` failed against raw command hashing. Both pass with the canonical payload. Log: `/tmp/task1-hash-red.log`.
- PostgreSQL red: initial two-lot posting, same-key concurrency and closed-PO recovery failed with 409. Focused reproduction printed public code `Concurrency.Conflict` (`/tmp/task1-multilot-diagnostic.log`). Preserving Added state made the exact two-lot stock/accounting test pass. Recovery then failed structural lookup response equality (`/tmp/task1-multilot-fix.log`), proving the numeric/string enum mismatch before the DTO converter fixed it.
- The revoked-manager test initially expected 403, but the live authority middleware correctly invalidates the stale JWT and answers 401. The test now asserts 401 for the revoked token and 403 for active Staff; both prove no replay/lookup or accounting effects.

Final required targeted results:

| Command | Result |
|---|---|
| `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~PurchaseOrders` | 101 passed, 0 failed |
| `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter FullyQualifiedName~PurchaseOrdersControllerTests` | 23 passed, 0 failed |
| `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter FullyQualifiedName~PurchaseOrder` | 23 passed, 0 failed |
| `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~PurchaseOrdersControllerTests\|FullyQualifiedName~FinancialOperationTests'` | 71 passed, 0 failed (real PostgreSQL), 1m44s |
| `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~PurchaseOrders\|FullyQualifiedName~Inventory.Services'` | 114 passed at that checkpoint; broader final solution run includes the final inventory/PO tests |
| `dotnet test src/backend/Intelibill.slnx` | Domain390 + Application1735 + API579 passed; full integration still running at commit (session `15643`, log `/tmp/task1-full-suite.log`); parent is monitoring completion |
| `dotnet ef migrations has-pending-model-changes --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api` | No changes detected |
| `git diff --check` | Passed |

Logs: `/tmp/task1-app-final.log`, `/tmp/task1-api-final.log`, `/tmp/task1-domain-final.log`, `/tmp/task1-integration-final.log`, `/tmp/task1-app-inventory.log`, `/tmp/task1-full-suite.log`.

Real PostgreSQL coverage includes:

- One PO line with remaining10 receives A4/cost40 + B6/cost90: one receipt, two lines, two batches, two stock movements, stock10, receipt values130 and payable130, with immutable source links.
- Grouped 6+6 rejection identifies both rows and changes no receipt, PO quantity, batch, movement, receipt value, payable, inventory total or operation receipt.
- A pre-existing second batch makes the second processor invocation fail after the first lot staged its mutations. All new receipt/stock/accounting/operation changes roll back.
- Four concurrent same-key requests from independent HTTP/request scopes converge on identical responses and one posting. Competing keys for the final quantity yield one posting.
- Lost-response replay and lookup after both PO close and month close exactly match the original response, including persisted `operation_receipts.response_body`.
- Changed keyed payload conflicts; cross-shop key isolation; Staff/revoked-member refusal; duplicate trimmed lot/client identity; foreign line/item; scalar/tax/client-ID validation; fractional wire quantities; legacy missing IDs.
- Bonus lots remain stock with no payable and retain independent tax flags; batch names `A` and `a` remain distinct.
- The generated migration is downgraded/upgraded against existing receipt lots to verify backfill. Multiple null writes remain allowed, and GET maps legacy null IDs to line IDs.

## Migration

Generated with:

```sh
dotnet ef migrations add AddPurchaseReceiptClientLineIdentity --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api
```

Migration: `20261107154602_AddPurchaseReceiptClientLineIdentity` plus generated designer/model snapshot.

Inspected SQL with:

```sh
dotnet ef migrations script 20261107050412_AddExpenseTreatment 20261107154602_AddPurchaseReceiptClientLineIdentity --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api --output /tmp/task1-migration.sql
```

SQL contains only the nullable UUID column; per-shop RLS-aware `client_line_id = id` backfill preserving/restoring active-shop session context; unique partial index on `(purchase_order_receipt_id, client_line_id) WHERE client_line_id IS NOT NULL`; and EF history bookkeeping. No amount/quantity/cost columns or existing foreign keys change. Generated one-use index arrays use the repository's existing CA1861 suppression pattern.

Apply the additive migration before new API pods. Once keyed receipts exist, rollback must retain replay/lookup-capable server code. No deployment was performed.

## Changed files

- `.superpowers/sdd/2026-11-07-purchase-receiving-lots-recovery/task-1-report.md`
- `src/backend/Intelibill.Api/Controllers/PurchaseOrdersController.cs`
- `src/backend/Intelibill.Application/Common/Errors/Errors.PurchaseOrder.cs`
- `src/backend/Intelibill.Application/Features/FinancialOperations/FinancialOperationTypes.cs`
- `src/backend/Intelibill.Application/Features/Inventory/Services/IInventoryUpdater.cs`
- `src/backend/Intelibill.Application/Features/Inventory/Services/InventoryUpdater.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommand.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandHandler.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandValidator.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/DTOs/PurchaseOrderDtos.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Services/PurchaseOrderDtoMapper.cs`
- `src/backend/Intelibill.Domain/Entities/PurchaseOrderReceipt.cs`
- `src/backend/Intelibill.Domain/Entities/PurchaseOrderReceiptLine.cs`
- `src/backend/Intelibill.Domain/Enums/FinancialOperationType.cs`
- `src/backend/Intelibill.Infrastructure/Data/Configurations/PurchaseOrderReceiptLineConfiguration.cs`
- `src/backend/Intelibill.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `tests/backend/integration/Intelibill.Integration.Tests/FinancialOperationTests.cs`
- `tests/backend/integration/Intelibill.Integration.Tests/PurchaseOrdersControllerTests.cs`
- `tests/backend/unit/Intelibill.Api.Unit.Tests/Controllers/PurchaseOrdersControllerTests.cs`
- `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandHandlerTests.cs`
- `tests/backend/unit/Intelibill.Domain.Unit.Tests/Entities/PurchaseOrderTests.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/PurchaseOrderReceiptErrors.cs`
- `src/backend/Intelibill.Application/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/PurchaseOrderReceiptPayload.cs`
- `src/backend/Intelibill.Infrastructure/Migrations/20261107154602_AddPurchaseReceiptClientLineIdentity.Designer.cs`
- `src/backend/Intelibill.Infrastructure/Migrations/20261107154602_AddPurchaseReceiptClientLineIdentity.cs`
- `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/PurchaseOrderReceiptPayloadTests.cs`
- `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/PurchaseOrders/Commands/ReceivePurchaseOrder/ReceivePurchaseOrderCommandValidatorTests.cs`

## Concerns

- Required targeted checks are complete and green. The broader solution integration run is still pending; the parent explicitly requested committing now so review can proceed while it monitors that run. Full-suite session: `15643`; log: `/tmp/task1-full-suite.log`.
- No known unresolved Task 1 correctness issue from the completed checks.
- Existing API validation middleware returns indexed FluentValidation paths for malformed scalar/client-ID inputs; business failures expose receiving-only `rowErrors`. Task 2 must normalize those existing paths against its frozen request.
- Full PO lifecycle quantities remain positive whole units; fractional-quantity lifecycle conversion is outside this task.
