## Issue: Enable QR decoding in shared scanner stack

- **Parent:** [prd.md](prd.md)
- **Intended labels:** enhancement, ready-for-agent
- **Blocked by:** None

## What to build

Extend the existing shared barcode scanner stack to accept QR scan formats through the current scanner dialog and detection services, with no new flow and no API contract changes. Keep duplicate suppression, scan lifecycle, and fallback behavior exactly aligned with current barcode handling.

## Agent Routing

```yaml
agent_routing:
  complexity_hint: easy
  required_capability: balanced
  parallel_safe: true
  cost_preference: low
  speed_preference: high
  ownership_scope:
    - src/frontend/src/app/shared/components/barcode-scanner-dialog.component.ts
    - src/frontend/src/app/core/services/barcode-detector.service.ts
    - src/frontend/src/app/core/services/barcode-scanner-dialog.component.spec.ts (or existing adjacent tests)
  verification:
    - extend scanner service/spec tests to include QR detection path
    - extend scanner dialog spec for dedupe and close behavior regression
```

## Technical Context Snapshot

### Current stack in scope

- Frontend framework and libraries: Angular 21 in `src/frontend`, shared dialog component/service pattern.
- Scanner stack in use: `BarcodeDetector` API fallback to ZXing via `@zxing/browser` / `@zxing/library`.
- Backend/runtime stack not modified in this slice.

### Dependencies in scope

- Existing dependencies to reuse:
  - `@zxing/browser`, `@zxing/library`
  - native `BarcodeDetector`
- New dependency additions: **no**
  - Justification: phase 1 is additive behavior using existing scanner infra.

### Architecture alignment

- Keep shared scanner component/service ownership in place.
- Preserve the event contract (`detected`) and emitted payload types.
- No route/component refactor; no API contract changes.

### Integration touchpoints

- Affects shared frontend scanner format support and detection fallback path.
- No API, schema, migration, or DB touchpoints in this slice.
- Backward compatibility expectation: unchanged barcode behavior.

## Acceptance criteria

- [ ] QR is recognized in native detector path where supported by browser, using existing architecture.
- [ ] QR is recognized in ZXing fallback path when native API is unavailable.
- [ ] Existing barcode scan throttling/duplicate suppression behavior remains unchanged.
- [ ] Existing scanner close/failure flow remains unchanged.
- [ ] Scanner service and component tests include at least one QR happy path and one fallback path assertion.

## Blocked by

- None - can start immediately.

---

## Issue: Route QR scans through existing frontend product/batch lookup consumers

- **Parent:** [prd.md](prd.md)
- **Intended labels:** enhancement, ready-for-agent
- **Blocked by:** Enable QR decoding in shared scanner stack

## What to build

Using the existing shared scanner entry points, ensure scanned QR payloads are passed through all existing barcode-powered flows (e.g., inbound inventory and sales) without introducing a new UI flow. Verify the “not found” / quick-add UX is identical to barcode scanning and remains triggered from the same handlers.

## Agent Routing

```yaml
agent_routing:
  complexity_hint: medium
  required_capability: balanced
  parallel_safe: true
  cost_preference: low
  speed_preference: balanced
  ownership_scope:
    - src/frontend/src/app/pages/inventory-batch-page/
    - src/frontend/src/app/pages/new-sale-page/
    - src/frontend/src/app/shared/components/barcode-scanner-dialog.component.ts
    - src/frontend/src/app/pages/add-product-overlay.component.ts
  verification:
    - run existing scanner-driven flow tests for inbound and sales pages with QR payload values
    - confirm fallback/quick-add path uses same modal/copy as barcode
```

## Technical Context Snapshot

### Current stack in scope

- Frontend runtime: Angular 21 SPA with shared components/services.
- Domain interactions: existing inventory and sales pages call shared scanner and existing product/stock lookup APIs.
- API consumption already uses:
  - `/api/items/details`
  - `/api/inventory/batches/available`

### Dependencies in scope

- Existing dependencies to reuse:
  - Existing Angular services/repositories already used by barcode flows.
  - Existing scanner integration hooks in inventory and sale pages.
- New dependency additions: **no**
  - Justification: behavior parity requested, no new module required.

### Architecture alignment

- Preserve current event-driven scan consumption pattern from scanner dialog.
- Keep field labels and add-product overlay semantics unchanged.
- No schema/API contract changes.

### Integration touchpoints

- Affects frontend handlers and service calls for stock lookup and product detail retrieval.
- Affected endpoints remain unchanged; payload now includes QR string through existing `barcode` fields.
- Backward compatibility expectation: all barcode flows continue exactly as before.

## Acceptance criteria

- [ ] Existing scanner entry points in inventory and sales accept QR payloads without UI changes.
- [ ] Not-found case behavior and quick-add flow is identical to barcode behavior.
- [ ] No changes required from users between barcode and QR path in these flows.
- [ ] No regressions in existing barcode-driven flows.

## Blocked by

- [Enable QR decoding in shared scanner stack](./issues.md)

## Note

`run-with-it` remains the final routing authority for runtime execution.

---

## Issue: Keep backend lookup contracts QR-compatible and cover parity tests

- **Parent:** [prd.md](prd.md)
- **Intended labels:** enhancement, ready-for-agent
- **Blocked by:** None

## What to build

Keep barcode lookup contracts unchanged and verify QR payloads are processed through existing barcode lookup paths for both product details and inventory batch availability. Add/adjust tests to ensure QR behaves like barcode input in success/fallback scenarios.

## Agent Routing

```yaml
agent_routing:
  complexity_hint: medium
  required_capability: balanced
  parallel_safe: true
  cost_preference: low
  speed_preference: balanced
  ownership_scope:
    - src/backend/Intelibill.Api/Controllers/ItemsController.cs
    - src/backend/Intelibill.Api/Controllers/InventoryController.cs
    - src/backend/Intelibill.Application/Items/Queries/GetProductDetailsByNameOrBarcode/
    - src/backend/Intelibill.Application/Inventory/Queries/GetAvailableBatches/
    - tests/backend/integration/Intelibill.Integration.Tests/
    - tests/backend/unit/Intelibill.Application.Unit.Tests/
    - tests/backend/unit/Intelibill.Api.Unit.Tests/
  verification:
    - add/extend unit tests to pass QR-like strings through barcode query handlers
    - add/extend integration tests on /api/items/details and batch lookup flows
    - assert external lookup behavior and not-found fallback remain intact
```

## Technical Context Snapshot

### Current stack in scope

- Backend: ASP.NET Core 10 API, Wolverine for command/query orchestration, EF Core 10, ErrorOr.
- Data layer: PostgreSQL via Npgsql; existing item uniqueness and repository patterns.
- Testing: xUnit unit/integration suite with Testcontainers for integration DB-backed scenarios.

### Dependencies in scope

- Existing package dependencies to reuse:
  - ASP.NET Core 10, EF Core 10, Wolverine, ErrorOr, FluentValidation.
- New dependency additions: **no**
  - Justification: this is a behavior-parity slice that uses existing handlers/contracts.

### Architecture alignment

- Respect existing CQRS-style query handlers and controller thinness.
- Do not introduce new DTOs or route changes.
- Keep external lookup service flow intact.

### Integration touchpoints

- Affected endpoints: existing item details and inventory batch availability endpoints.
- Data contracts unchanged; payload continues via existing `barcode` fields.
- Backward compatibility expectation: zero API contract or migration changes.

## Acceptance criteria

- [ ] No API contract or schema changes are introduced.
- [ ] Unit tests cover QR-style payload through existing barcode lookup handlers.
- [ ] Integration tests validate QR-like payload success and not-found/fallback semantics.
- [ ] Existing barcode tests remain valid and still pass.

## Blocked by

- None - can start immediately.

## Note

`run-with-it` remains the final routing authority for runtime execution.
