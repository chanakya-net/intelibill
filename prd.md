## Problem Statement
Users already use barcode scanning in inventory and sales flows, but QR code scanning is not explicitly supported despite often being used for the same workflows. Users must currently adapt by using barcode inputs only, creating friction when products are labeled with QR codes only.

## Solution
Add QR code support by reusing the existing barcode scanning stack and backend lookup contracts. QR payloads will be treated as barcode-like identifiers and routed through the same product/detail and batch lookup paths, with identical fallback and quick-add behavior when not found.

## User Stories
1. As a shop staff member, I want to scan a QR code using the existing scanner so that I can quickly add or sell products using the same flow as barcode scanning.
2. As a stock operator, I want scanned QR payloads to resolve product/batch lookups through current barcode endpoints so that I can reuse current workflows without changing my process.
3. As a sales clerk, I want QR scans to behave like barcode scans when no item is found, including fallback quick-add behavior, so I can continue working without switching apps.
4. As a frontend user, I want QR and barcode scanning to work with the same existing permissions, fallback, and duplicate-scan protections so behavior remains predictable and reliable.
5. As a backend maintainer, I want no new API contracts or schema changes in phase 1 so QR support is safe, low-risk, and backward-compatible.

## Implementation Decisions
- Modify existing shared scanner modules to include QR-capable formats, not introduce a separate QR-specific flow.
- Keep API contracts unchanged in phase 1. QR payload continues through existing `barcode` request fields.
- Keep data model unchanged: no new identifier columns, no schema migration required.
- Preserve existing per-shop uniqueness and validation behavior of the barcode field in `Item`.
- Preserve legacy fallback behavior:
  - existing external lookup path where applicable,
  - not-found quick-add flow should continue to mirror barcode UX.
- Keep duplicate suppression, scan lifecycle, and rate/guard behavior unchanged from barcode flows.
- Extend existing tests rather than creating parallel QR-specific testing infrastructure.
- No new third-party scanning dependencies.
- Preserve all module boundaries already established in the shared frontend stack and API command/query handlers.

## Testing Decisions
- Test external behavior over internal implementation details.
- Prioritize shared component tests and integration tests around `/api/items/details` and batch lookup behavior with QR-like payloads.
- Reuse existing barcode test suites by adding QR cases.
- Validate unsupported-browser fallback continues to work by covering the existing detector fallback path.
- Add regression checks for unchanged barcode behavior.

## Out of Scope
- QR payload parsing into structured content (URLs, JSON, GS1 fields).
- New item identifier model (`qrCode` column/relationship) in phase 1.
- New API contracts/endpoint versions for QR.
- New scanner screen dedicated only to QR.
- New package dependencies.

## Further Notes
- Existing architecture stack and tenancy/authorization constraints remain unchanged.
- The implementation should be strictly additive and parity-focused:
  - no observable breaking change to barcode flows,
  - no contract/API incompatibility,
  - no schema-breaking migration.
