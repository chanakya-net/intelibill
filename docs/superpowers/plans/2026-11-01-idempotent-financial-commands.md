# Idempotent Financial Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A customer or supplier payment carries a durable operation key, so a retry after a lost response, a reload or a second operator replays the one committed posting instead of creating another.

**Architecture:** A tenant table `operation_receipts` is unique on `(shop_id, operation_type, operation_key)`. `IFinancialOperationExecutor` opens the transaction, reserves the receipt with `INSERT … ON CONFLICT DO NOTHING` (PostgreSQL blocks on a concurrent uncommitted twin, so no aborted transaction is ever continued), runs the handler's mutation in that same unit of work, then completes the receipt with the serialized response and commits. A conflict rolls back, rereads the committed receipt and replays it or answers `Operation.PayloadMismatch`. The browser freezes each payment as a `FinancialOperationIntent` in `localStorage` before sending and only ever retries that frozen payload.

**Tech Stack:** ASP.NET Core 10, Wolverine, ErrorOr, FluentValidation, EF Core 10/Npgsql, xUnit + NSubstitute, Testcontainers; Angular 21, NgRx, Vitest, Bun.

**Spec:** `pending-tasks/12-idempotent-financial-commands.md` (pasted plan 12, P1); `docs/reviews/2026-10-21-market-readiness.md`.

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; repository abstractions, ErrorOr, `IUnitOfWork`; warnings-as-errors; package versions only in `Directory.Packages.props`.
- Tenant and actor from authenticated context; authorize (membership + Owner/Manager) **before** reading or replaying a receipt.
- New tenant table gets `<table>_shop_isolation` RLS (`shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid`) and a `TenantPolicyManifest` entry.
- Never catch a PostgreSQL unique violation and keep using the transaction.
- Sale deduplication (`RecordSaleCommandHandler`, `(shop, actor, key)`) is untouched.
- Web only (mobile excluded); Bun only.

## Gap analysis (plan 12 → `main` at `bd2ff791`)

| Requirement | State on `main` | This plan |
|---|---|---|
| Shared receipt + executor | Missing | Task 1 |
| Customer / supplier payment keyed | Missing — retry posts twice | Task 2 |
| Read-only recovery `GET financial-operations/{type}/{key}` | Missing | Task 2 |
| Browser intent persisted before send | Missing | Task 3 |
| Returns, voids, inbound, bulk intake, adjustments, PO receipts | Unkeyed | **Deferred to owning plans** 13/16/31/32 — each reshapes its command and payload; they adopt `IFinancialOperationExecutor` from this PR |
| Reconcile-by-other-role policy, retention (plan 40) | Product decision | Receipts are never deleted; any Owner/Manager of the shop may read/replay |

## Contracts

- `FinancialOperationType` (Domain enum, stored as int): `CustomerPayment = 1`, `SupplierPayment = 2`. Route slugs `customer-payment`, `supplier-payment`.
- `OperationReceipt` (Domain entity, table `operation_receipts`): `Id, ShopId, OperationType, OperationKey (≤128), RequestHash (SHA-256 hex), HashVersion (int, 1), ActorUserId, TargetId, Status (Completed), ResponseVersion (int, 1), ResponseBody (jsonb), CreatedAt, CompletedAt`.
- `FinancialOperationScope(Guid ShopId, FinancialOperationType OperationType, string Key)`.
- `IFinancialOperationExecutor.ExecuteAsync<T>(FinancialOperationScope scope, Guid actorUserId, Guid targetId, string requestHash, Func<CancellationToken, Task<ErrorOr<T>>> action, CancellationToken ct) : Task<ErrorOr<T>>`.
- `FinancialOperationHasher.Compute(object payload) : string` — camelCase JSON → SHA-256 hex; payload = target + business fields, never transport metadata.
- Errors: `Operation.PayloadMismatch` (409), `Operation.NotFound` (404), `Operation.InvalidKey` (400), `Operation.UnknownType` (404), `Operation.UserIsNotOwnerOrManager` (403).
- Requests gain optional `operationKey` (1–128 chars after trim). Absent → legacy unkeyed path with a warning log (`legacy unkeyed financial command`) — compatibility until the key is required.

## Review Focus

1. Two concurrent posts, one key → one receipt, one ledger entry, one supplier expense; both answers carry the same entry id (`ConcurrentSameKey_CommitsOnce`).
2. Same key, ₹501 instead of ₹500 → 409, nothing changes (`SameKeyChangedAmount_Conflicts`).
3. Failure after mutation but before commit → no receipt and no posting (`ActionFailure_RollsBackReservationAndPosting`); committed-then-lost → replay without rerunning the action (`LostResponse_ReplaysSavedResult`).
4. Staff, removed member or other shop → 403/404 without the result (`UnauthorizedReplay_DoesNotDiscloseResult`); same key in another shop posts independently (`ScopeIsolation_AllowsSameKeyInOtherShop`).
5. Reload after response loss reuses the frozen payload and key; a shop switch cannot send another shop's intent (`reloadAfterResponseLossReusesFrozenPayload`, `shopSwitchCannotSendForeignIntent`).

---

### Task 1: Receipt, executor, migration

**Files:** Create `Domain/Entities/OperationReceipt.cs`, `Domain/Enums/FinancialOperationType.cs`, `Domain/Interfaces/Repositories/IOperationReceiptRepository.cs`, `Application/Common/Operations/{IFinancialOperationExecutor,FinancialOperationScope,FinancialOperationHasher}.cs`, `Application/Common/Errors/Errors.Operation.cs`, `Infrastructure/Data/Configurations/OperationReceiptConfiguration.cs`, `Infrastructure/Repositories/OperationReceiptRepository.cs`, `Infrastructure/Services/Operations/FinancialOperationExecutor.cs`, migration `AddOperationReceipts` (+ RLS SQL); modify `ApplicationDbContext`, `DependencyInjection.cs`, `TenantPolicyManifest`.

- [x] Executor algorithm: refuse if a transaction is already open (no nesting) → begin → `INSERT … ON CONFLICT (shop_id, operation_type, operation_key) DO NOTHING` → 0 rows: rollback, clear tracker, read receipt, compare hash (`PayloadMismatch`) or deserialize `ResponseBody` to `T` → 1 row: run action; error ⇒ rollback + clear, return errors; exception ⇒ rollback, rethrow; success ⇒ `UPDATE` receipt (status, body, completed_at) and commit.
- [x] Migration adds table, unique index, `shop_id` index, RLS policy; manifest `Tenant("operation_receipts")`.
- [x] Unit: `FinancialOperationHasher` is stable and changes with amount.

### Task 2: Payments, lookup and database proofs

**Files:** modify both payment commands/validators/handlers and `CustomersController`/`SuppliersController` requests; create `Application/Features/FinancialOperations/Queries/GetFinancialOperation/*`, `Api/Controllers/FinancialOperationsController.cs`, `tests/.../FinancialOperationTests.cs`; extend handler unit tests.

- [x] Handlers authorize and load the target first, then (keyed) route the posting through the executor; unkeyed keeps today's path.
- [x] `GET api/financial-operations/{type}/{key}` (`OwnerOrManager`): membership + role check, then `{operationType, operationKey, targetId, actorUserId, completedAt, response}` or 404.
- [x] Integration tests: Review Focus 1–4, plus lookup found/unknown.

### Task 3: Web financial intents

**Files:** create `core/services/financial-operation-intent.service.ts` (+ spec), `shared/components/pending-financial-operation/*`; modify `customer.service.ts`, `supplier-ledger.service.ts`, `auth.constants.ts`, suppliers actions/effects/facade/detail/payment form, customers page, i18n (nine locales).

- [x] `prepare(scope, type, targetId, payload)` writes a frozen intent to `localStorage` before returning (throws ⇒ nothing sent); `pending(scope, type, targetId)`; `send(scope, intent, sender)` refuses a foreign scope, marks `unknown` on 0/408/429/5xx/non-HTTP, clears on success, discards on other 4xx; `check(scope, intent)` resolves via the lookup.
- [x] Payment forms: an unresolved intent for the target shows amount/time with **Check status** and **Retry same payment** and blocks a new payment for that target.
- [x] Specs: Review Focus 5 + storage-failure-sends-nothing + rejection-discards.

## Verification

`dotnet build src/backend/Intelibill.slnx`; unit test projects; `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "FullyQualifiedName~FinancialOperationTests|FullyQualifiedName~TenantPolicyManifest|FullyQualifiedName~SuppliersControllerTests|FullyQualifiedName~CustomersControllerTests"`; `bun run test`, `bun run build` in `src/frontend`.

## Rollout / rollback

Table and RLS first (this migration), server accepts optional keys, web sends keys; requiring the key is a later change once legacy-unkeyed warnings stop. Rollback never deletes receipts and never strips keys from the web client.

## Deferred

- Returns, voids, single/bulk inbound, adjustments and purchase-order receipts adopt the executor in their owning plans (13, 16, 31, 32); plan 13 keeps per-row receipts, never one batch-wide transaction.
- Requiring `operationKey` (rejecting unkeyed payments) once `Legacy unkeyed financial command` warnings stop.
- Which privileged roles may reconcile another operator's action beyond owner/manager, and the receipt retention policy (plan 40).
- Restricted-role fixtures from plan 48 for `operation_receipts` beyond the manifest-driven RLS suites.
